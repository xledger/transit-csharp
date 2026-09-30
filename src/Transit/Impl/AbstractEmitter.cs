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
    private readonly bool _stockPrimitives;

    protected AbstractEmitter(FrozenDictionary<Type, IWriteHandler> handlers, IWriteHandler? defaultWriteHandler = null, Func<object, object>? transform = null)
    {
        _handlers = handlers;
        _defaultWriteHandler = defaultWriteHandler;
        _transform = transform;
        _stockPrimitives = UsesStockPrimitives(handlers);
    }

    /// <summary>
    /// The types <see cref="TryMarshalPrimitive"/> encodes without consulting a handler. Its
    /// output must stay identical to what these handlers produce.
    /// </summary>
    private static readonly Type[] PrimitiveTypes =
    {
        typeof(NullType), typeof(string), typeof(long), typeof(int),
        typeof(bool), typeof(Keyword), typeof(double),
    };

    /// <summary>
    /// Whether every type the fast path covers still maps to the handler it was written against.
    /// A caller may override any of them, and a caller-supplied table is taken verbatim, so the
    /// check is per type and by identity rather than "no custom handlers were passed".
    /// </summary>
    private static bool UsesStockPrimitives(FrozenDictionary<Type, IWriteHandler> handlers)
    {
        var stock = WriterFactory.DefaultHandlers();
        if (ReferenceEquals(handlers, stock))
            return true;

        foreach (var type in PrimitiveTypes)
        {
            if (!handlers.TryGetValue(type, out var handler)
                || !stock.TryGetValue(type, out var expected)
                || !ReferenceEquals(handler, expected))
                return false;
        }

        // An IKeyword override is mirrored onto the struct by MergedHandlers, so checking the
        // concrete entry is enough to notice one.
        return true;
    }

    /// <summary>
    /// Emits the handful of types that dominate a document without the type lookup and two
    /// interface calls the handler path costs. Returns false for anything it does not cover.
    /// </summary>
    private bool TryMarshalPrimitive(object? o, bool asDictionaryKey, WriteCache cache)
    {
        switch (o)
        {
            case null:
                EmitNull(asDictionaryKey, cache);
                return true;
            case string s:
                EmitString(null, null, Escape(s), asDictionaryKey, cache);
                return true;
            case long l:
                EmitInteger(l, asDictionaryKey, cache);
                return true;
            case int i:
                EmitInteger(i, asDictionaryKey, cache);
                return true;
            case bool b:
                EmitBoolean(b, asDictionaryKey, cache);
                return true;
            case Keyword k:
                EmitString(Constants.EscStr, ":", k.ToString()!, asDictionaryKey, cache);
                return true;
            // Non-finite doubles are retagged "z" by DoubleWriteHandler, so they stay on the
            // handler path. float stays off the fast path entirely for the same reason.
            case double d when double.IsFinite(d):
                EmitDouble(d, asDictionaryKey, cache);
                return true;
            default:
                return false;
        }
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

    public string? GetTag(object? obj)
    {
        var handler = GetHandler(obj);
        // A null obj resolves to NullWriteHandler, whose Tag ignores its argument.
        return handler?.Tag(obj!);
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

    protected virtual void EmitTaggedStart(string t, WriteCache cache)
    {
        EmitListStart(2L);
        EmitString(Constants.EscTag, t, "", false, cache);
    }

    protected virtual void EmitTaggedEnd() => EmitListEnd();

    protected void EmitTagged(string t, object obj, bool ignored, WriteCache cache)
    {
        EmitTaggedStart(t, cache);
        Marshal(obj, false, cache);
        EmitTaggedEnd();
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

    protected abstract void EmitDictionary(IDictionary dict, bool ignored, WriteCache cache);

    /// <summary>
    /// Marshals every key/value pair of <paramref name="dict"/>. Reads the enumerator's
    /// <see cref="IDictionaryEnumerator.Key"/> and <see cref="IDictionaryEnumerator.Value"/>
    /// directly, because a foreach over <see cref="IDictionary"/> boxes a
    /// <see cref="DictionaryEntry"/> for every entry.
    /// </summary>
    protected void MarshalEntries(IDictionary dict, WriteCache cache)
    {
        var entries = dict.GetEnumerator();
        try
        {
            while (entries.MoveNext())
            {
                Marshal(entries.Key, true, cache);
                Marshal(entries.Value, false, cache);
            }
        }
        finally
        {
            (entries as IDisposable)?.Dispose();
        }
    }

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

        // After the transform: it can change the object's type, so dispatch must see its result.
        if (_stockPrimitives && TryMarshalPrimitive(o, asDictionaryKey, cache))
            return;

        var h = GetHandler(o);
        if (h == null)
            throw new NotSupportedException("Not supported: " + (o?.GetType().ToString() ?? "null"));

        var t = h.Tag(o!);
        if (t == null)
            throw new NotSupportedException("Not supported: " + (o?.GetType().ToString() ?? "null"));

        MarshalResolved(o, h, t, asDictionaryKey, cache);
    }

    protected void MarshalResolved(object? o, IWriteHandler h, string t, bool asDictionaryKey, WriteCache cache)
    {
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

        // Both branches dispatch with the handler and tag already resolved, so the value is
        // neither transformed nor re-tagged a second time. A scalar is quoted on the way out so
        // that the document is always a collection.
        if (tag.Length == 1)
            EmitQuoted(obj, handler, tag, cache);
        else
            MarshalResolved(obj, handler, tag, false, cache);
    }

    /// <summary>
    /// Emits <paramref name="obj"/> wrapped in a quote tag. Mirrors what <see cref="EmitTagged"/>
    /// does for <see cref="Quote"/>, but takes the already-resolved handler and tag.
    /// </summary>
    protected void EmitQuoted(object? obj, IWriteHandler handler, string tag, WriteCache cache)
    {
        EmitTaggedStart("'", cache);
        MarshalResolved(obj, handler, tag, false, cache);
        EmitTaggedEnd();
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
