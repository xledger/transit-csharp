using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Frozen;

namespace Transit.Net.Impl;

/// <summary>
/// Base class for transit emitters. Uses FrozenDictionary for handler lookup
/// and a ConcurrentDictionary to cache resolved handlers for derived types.
/// </summary>
internal abstract class AbstractEmitter : IEmitter
{
    private readonly FrozenDictionary<Type, IWriteHandler> _handlers;
    private readonly IWriteHandler? _defaultWriteHandler;
    private readonly Func<object, object>? _transform;
    // Caches resolved handlers for types not in the FrozenDictionary (subclasses / interface matches).
    // Growth is bounded by the number of distinct serialised types, which is small in practice.
    private readonly ConcurrentDictionary<Type, IWriteHandler?> _handlerCache = new();

    protected AbstractEmitter(FrozenDictionary<Type, IWriteHandler> handlers, IWriteHandler? defaultWriteHandler = null, Func<object, object>? transform = null)
    {
        _handlers = handlers;
        _defaultWriteHandler = defaultWriteHandler;
        _transform = transform;
    }

    private IWriteHandler? CheckBaseClasses(Type type)
    {
        var baseType = type.BaseType;
        while (baseType != null && baseType != typeof(object))
        {
            if (_handlers.TryGetValue(baseType, out var handler))
                return handler;
            baseType = baseType.BaseType;
        }
        return null;
    }

    private IWriteHandler? CheckBaseTypes(Type type, IEnumerable<Type> baseTypes)
    {
        IWriteHandler? found = null;

        foreach (var bt in baseTypes)
        {
            if (_handlers.TryGetValue(bt, out var h))
            {
                if (found != null)
                    throw new TransitException("More than one match for " + type);
                found = h;
            }
        }

        return found;
    }

    private IWriteHandler? CheckBaseInterfaces(Type type)
        => CheckBaseTypes(type, type.GetInterfaces());

    private IWriteHandler? CheckBaseGenericInterfaces(Type type)
        => CheckBaseTypes(type, type.GetInterfaces()
            .Where(i => i.IsGenericType)
            .Select(i => i.GetGenericTypeDefinition()));

    private IWriteHandler? GetHandler(object? obj)
    {
        var type = obj?.GetType() ?? typeof(NullType);

        if (_handlers.TryGetValue(type, out var handler) && handler is not IAbstractEmitterAware)
            return handler;

        if (_handlerCache.TryGetValue(type, out var cached))
            return cached;

        handler ??= CheckBaseClasses(type)
               ?? CheckBaseGenericInterfaces(type)
               ?? CheckBaseInterfaces(type)
               ?? _defaultWriteHandler;

        if (handler is IAbstractEmitterAware aware)
            handler = aware.BindTo(this);

        _handlerCache[type] = handler;
        return handler;
    }

    public string? GetTag(object obj)
    {
        var handler = GetHandler(obj);
        return handler?.Tag(obj);
    }

    protected string Escape(string s)
    {
        if (s.Length > 0)
        {
            char c = s[0];
            if (c == Constants.Esc || c == Constants.Sub || c == Constants.Reserved)
                return string.Create(s.Length + 1, s, static (span, src) =>
                {
                    span[0] = Constants.Esc;
                    src.AsSpan().CopyTo(span[1..]);
                });
        }
        return s;
    }

    protected virtual void EmitTagged(string t, object obj, bool ignored, WriteCache cache)
    {
        EmitListStart(2L);
        EmitString(Constants.EscTag, t, "", false, cache);
        Marshal(obj, false, cache);
        EmitListEnd();
    }

    protected void EmitEncoded(string t, IWriteHandler handler, object obj, bool asDictionaryKey, WriteCache cache)
    {
        if (t.Length == 1)
        {
            var r = handler.Representation(obj);
            if (r is string rs)
            {
                EmitString(Constants.EscStr, t, rs, asDictionaryKey, cache);
            }
            else if (PrefersStrings() || asDictionaryKey)
            {
                var sr = handler.StringRepresentation(obj);
                if (sr != null)
                    EmitString(Constants.EscStr, t, sr, asDictionaryKey, cache);
                else
                    throw new TransitException("Cannot be encoded as a string " + obj);
            }
            else
            {
                EmitTagged(t, r, asDictionaryKey, cache);
            }
        }
        else
        {
            if (asDictionaryKey)
                throw new TransitException("Cannot be used as a map key " + obj);
            EmitTagged(t, handler.Representation(obj), asDictionaryKey, cache);
        }
    }

    protected void EmitDictionary(IDictionary dict, bool ignored, WriteCache cache)
    {
        EmitDictionary(EnumerateDict(dict), ignored, cache);
    }

    private static IEnumerable<KeyValuePair<object, object>> EnumerateDict(IDictionary dict)
    {
        foreach (DictionaryEntry entry in dict)
            yield return new KeyValuePair<object, object>(entry.Key, entry.Value!);
    }

    protected abstract void EmitDictionary(IEnumerable<KeyValuePair<object, object>> keyValuePairs,
        bool ignored, WriteCache cache);

    protected void EmitList(object o, bool ignored, WriteCache cache)
    {
        // Get count efficiently if possible
        long length = o switch
        {
            ICollection c => c.Count,
            _ => -1 // unknown size — JSON doesn't require it
        };

        EmitListStart(length);

        switch (o)
        {
            case IEnumerable<int> ints:
                foreach (var n in ints) EmitInteger(n, false, cache);
                break;
            case IEnumerable<short> shorts:
                foreach (var n in shorts) EmitInteger(n, false, cache);
                break;
            case IEnumerable<long> longs:
                foreach (var n in longs) EmitInteger(n, false, cache);
                break;
            case IEnumerable<float> floats:
                foreach (var n in floats) EmitDouble(n, false, cache);
                break;
            case IEnumerable<double> doubles:
                foreach (var n in doubles) EmitDouble(n, false, cache);
                break;
            case IEnumerable<bool> bools:
                foreach (var n in bools) EmitBoolean(n, false, cache);
                break;
            case IEnumerable<char> chars:
                foreach (var n in chars) Marshal(n, false, cache);
                break;
            default:
                foreach (var n in (IEnumerable)o) Marshal(n, false, cache);
                break;
        }

        EmitListEnd();
    }

    protected void Marshal(object? o, bool asDictionaryKey, WriteCache cache)
    {
        if (_transform != null && o != null)
            o = _transform(o);

        var h = GetHandler(o);
        if (h == null)
            throw new NotSupportedException("Not supported: " + (o?.GetType().ToString() ?? "null"));

        var t = h.Tag(o!);
        if (t == null)
            throw new NotSupportedException("Not supported: " + (o?.GetType().ToString() ?? "null"));

        if (t.Length == 1)
        {
            switch (t[0])
            {
                case '_': EmitNull(asDictionaryKey, cache); break;
                case 's': EmitString(null, null, Escape((string)h.Representation(o!)), asDictionaryKey, cache); break;
                case '?': EmitBoolean((bool)h.Representation(o!), asDictionaryKey, cache); break;
                case 'i': EmitInteger(h.Representation(o!), asDictionaryKey, cache); break;
                case 'd': EmitDouble(h.Representation(o!), asDictionaryKey, cache); break;
                case 'b': EmitBinary(h.Representation(o!), asDictionaryKey, cache); break;
                case '\'': EmitTagged(t, h.Representation(o!), false, cache); break;
                default: EmitEncoded(t, h, o!, asDictionaryKey, cache); break;
            }
        }
        else
        {
            if (t == "array")
                EmitList(h.Representation(o!), asDictionaryKey, cache);
            else if (t == "map")
                EmitDictionary((IDictionary)h.Representation(o!), asDictionaryKey, cache);
            else
                EmitEncoded(t, h, o!, asDictionaryKey, cache);
        }
    }

    protected void MarshalTop(object? obj, WriteCache cache)
    {
        if (_transform != null && obj != null)
            obj = _transform(obj);

        var handler = GetHandler(obj);
        if (handler == null)
            throw new NotSupportedException($"Cannot marshal type {obj?.GetType()} ({obj})");

        var tag = handler.Tag(obj!);
        if (tag == null)
            throw new NotSupportedException($"Cannot marshal type {obj?.GetType()} ({obj})");

        if (tag.Length == 1)
            obj = new Quote(obj);

        Marshal(obj, false, cache);
    }

    public abstract void Emit(object obj, bool asDictionaryKey, WriteCache cache);
    public abstract void EmitNull(bool asDictionaryKey, WriteCache cache);
    public abstract void EmitString(string? prefix, string? tag, string s, bool asDictionaryKey, WriteCache cache);
    public abstract void EmitBoolean(bool b, bool asDictionaryKey, WriteCache cache);
    public abstract void EmitInteger(object o, bool asDictionaryKey, WriteCache cache);
    public abstract void EmitInteger(long i, bool asDictionaryKey, WriteCache cache);
    public abstract void EmitDouble(object d, bool asDictionaryKey, WriteCache cache);
    public abstract void EmitDouble(float d, bool asDictionaryKey, WriteCache cache);
    public abstract void EmitDouble(double d, bool asDictionaryKey, WriteCache cache);
    public abstract void EmitBinary(object b, bool asDictionaryKey, WriteCache cache);
    public abstract void EmitListStart(long size);
    public abstract void EmitListEnd();
    public abstract void EmitDictionaryStart(long size);
    public abstract void EmitDictionaryEnd();
    public abstract bool PrefersStrings();
    public abstract void FlushWriter();
}
