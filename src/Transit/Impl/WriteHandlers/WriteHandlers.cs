using System.Globalization;

namespace Transit.Net.Impl.WriteHandlers;

internal sealed class NullWriteHandler : AbstractWriteHandler
{
    public override string Tag(object obj) => "_";
    public override object Representation(object obj) => null!;
    public override string? StringRepresentation(object obj) => "";
}

internal sealed class BooleanWriteHandler : AbstractWriteHandler
{
    public override string Tag(object obj) => "?";
    public override object Representation(object obj) => obj;
    public override string? StringRepresentation(object obj) => ((bool)obj) ? "t" : "f";
}

internal sealed class ToStringWriteHandler : AbstractWriteHandler
{
    private readonly string _tag;
    public ToStringWriteHandler(string tag) => _tag = tag;
    public override string Tag(object obj) => _tag;
    public override object Representation(object obj) => ToInvariantString(obj);
    public override string? StringRepresentation(object obj) => ToInvariantString(obj);

    private static string ToInvariantString(object obj) => obj switch
    {
        string s => s,
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => obj.ToString()!,
    };
}

internal sealed class IntegerWriteHandler : AbstractWriteHandler
{
    private readonly string _tag;
    public IntegerWriteHandler(string tag) => _tag = tag;
    public override string Tag(object obj) => _tag;
    public override object Representation(object obj) => obj;
    public override string? StringRepresentation(object obj) => Convert.ToString(obj, CultureInfo.InvariantCulture);
}

internal sealed class FloatWriteHandler : AbstractWriteHandler
{
    public override string Tag(object obj)
    {
        float f = (float)obj;
        return float.IsNaN(f) || float.IsInfinity(f) ? "z" : "d";
    }
    public override object Representation(object obj)
    {
        float f = (float)obj;
        if (float.IsNaN(f)) return "NaN";
        if (float.IsPositiveInfinity(f)) return "INF";
        if (float.IsNegativeInfinity(f)) return "-INF";
        return obj;
    }
    public override string? StringRepresentation(object obj) => Convert.ToString(Representation(obj), CultureInfo.InvariantCulture);
}

internal sealed class DoubleWriteHandler : AbstractWriteHandler
{
    public override string Tag(object obj)
    {
        double d = (double)obj;
        return double.IsNaN(d) || double.IsInfinity(d) ? "z" : "d";
    }
    public override object Representation(object obj)
    {
        double d = (double)obj;
        if (double.IsNaN(d)) return "NaN";
        if (double.IsPositiveInfinity(d)) return "INF";
        if (double.IsNegativeInfinity(d)) return "-INF";
        return obj;
    }
    public override string? StringRepresentation(object obj) => Convert.ToString(Representation(obj), CultureInfo.InvariantCulture);
}

internal sealed class BinaryWriteHandler : AbstractWriteHandler
{
    public override string Tag(object obj) => "b";
    public override object Representation(object obj) => obj;
    public override string? StringRepresentation(object obj) => Convert.ToBase64String((byte[])obj);
}

internal sealed class GuidWriteHandler : AbstractWriteHandler
{
    public override string Tag(object obj) => "u";
    public override object Representation(object obj) => obj;
    public override string? StringRepresentation(object obj) => ((Guid)obj).ToString();
}

internal sealed class DateTimeWriteHandler : AbstractWriteHandler
{
    private sealed class VerboseDateTimeWriteHandler : AbstractWriteHandler
    {
        public override string Tag(object obj) => "t";
        public override object Representation(object obj) => AbstractParser.FormatDateTime((DateTime)obj);
        public override string? StringRepresentation(object obj) => Convert.ToString(Representation(obj), CultureInfo.InvariantCulture);
    }

    private static readonly IWriteHandler VerboseHandler = new VerboseDateTimeWriteHandler();

    public override string Tag(object obj) => "m";
    public override object Representation(object obj) => Transit.Net.Java.Convert.ToJavaTime((DateTime)obj);
    public override string? StringRepresentation(object obj) => Convert.ToString(Representation(obj), CultureInfo.InvariantCulture);
    public override IWriteHandler? GetVerboseHandler() => VerboseHandler;
}

internal sealed class QuoteWriteHandler : AbstractWriteHandler
{
    public override string Tag(object obj) => "'";
    public override object Representation(object obj) => ((Quote)obj).Value!;
}

internal sealed class TaggedValueWriteHandler : AbstractWriteHandler
{
    public override string Tag(object obj) => ((ITaggedValue)obj).Tag;
    public override object Representation(object obj) => ((ITaggedValue)obj).Representation;
}

internal sealed class RatioWriteHandler : AbstractWriteHandler
{
    public override string Tag(object obj) => "ratio";
    public override object Representation(object obj)
    {
        var r = (IRatio)obj;
        return TransitFactory.TaggedValue("array", new List<object>
        {
            r.Numerator,
            r.Denominator,
        });
    }
}

internal sealed class LinkWriteHandler : AbstractWriteHandler
{
    private static readonly IKeyword HrefKeyword = TransitFactory.Keyword("href");
    private static readonly IKeyword RelKeyword = TransitFactory.Keyword("rel");
    private static readonly IKeyword NameKeyword = TransitFactory.Keyword("name");
    private static readonly IKeyword PromptKeyword = TransitFactory.Keyword("prompt");
    private static readonly IKeyword RenderKeyword = TransitFactory.Keyword("render");

    public override string Tag(object obj) => "link";
    public override object Representation(object obj)
    {
        var link = (ILink)obj;
        var d = new Dictionary<object, object>
        {
            { HrefKeyword, link.Href.ToString() },
            { RelKeyword, link.Rel },
        };
        if (link.Name != null) d[NameKeyword] = link.Name;
        if (link.Prompt != null) d[PromptKeyword] = link.Prompt;
        if (link.Render != null) d[RenderKeyword] = link.Render;
        return TransitFactory.TaggedValue("map", d);
    }
}

internal sealed class SetWriteHandler : AbstractWriteHandler
{
    public override string Tag(object obj) => "set";
    public override object Representation(object obj)
        => TransitFactory.TaggedValue("array", obj);
}

internal sealed class ListWriteHandler : AbstractWriteHandler
{
    public override string Tag(object obj) => "array";
    public override object Representation(object obj) => obj;
}

internal sealed class EnumerableWriteHandler : AbstractWriteHandler
{
    public override string Tag(object obj) => "list";
    public override object Representation(object obj)
        => TransitFactory.TaggedValue("array", obj);
}

internal sealed class DictionaryWriteHandler : AbstractWriteHandler, IAbstractEmitterAware
{
    private readonly AbstractEmitter? _emitter;
    private bool? _lastStringableKeys;
    private Type? _lastCheckedDictType;
    private bool _canSkipFullKeyCheck;

    public DictionaryWriteHandler(AbstractEmitter? emitter = null) => _emitter = emitter;

    public IWriteHandler BindTo(AbstractEmitter emitter) => new DictionaryWriteHandler(emitter);

    private static bool HasUntaggedNonNullableKeyType(Type dictType)
    {
        Type? keyType = null;
        foreach (var i in dictType.GetInterfaces())
        {
            if (!i.IsGenericType || i.GetGenericTypeDefinition() != typeof(IDictionary<,>))
                continue;
            // Give up if dict inherits multiple concrete IDictionary<,> interfaces
            if (keyType != null)
                return false;
            keyType = i.GetGenericArguments()[0];
        }

        return keyType is { IsValueType: true }
            && Nullable.GetUnderlyingType(keyType) is null
            // ITaggedValue is the one handler whose tag comes from the instance, not the type.
            && !typeof(ITaggedValue).IsAssignableFrom(keyType);
    }

    private bool StringableKeys(System.Collections.IDictionary d)
    {
        var dictType = d.GetType();
        if (dictType != _lastCheckedDictType)
        {
            _canSkipFullKeyCheck = HasUntaggedNonNullableKeyType(dictType);
            _lastCheckedDictType = dictType;
        }

        // Held locally: a dictionary-valued key re-enters this method on the same bound
        // instance and would leave the fields describing the inner dictionary.
        var canSkipFullKeyCheck = _canSkipFullKeyCheck;

        // Keys of the same runtime type resolve to the same handler, and every handler but
        // TaggedValueWriteHandler derives its tag from the type rather than the instance, so
        // one verdict covers every later key of that type.
        Type? settled = null;

        foreach (var key in d.Keys)
        {
            if (key is not null && key.GetType() == settled)
                continue;

            var tag = _emitter!.GetTag(key);
            if (tag != null && tag.Length > 1)
                return false;
            if (tag == null && key is not string)
                return false;

            if (canSkipFullKeyCheck)
                return true;
            if (key is not null and not ITaggedValue)
                settled = key.GetType();
        }
        return true;
    }

    public override string Tag(object obj)
    {
        _lastStringableKeys = StringableKeys((System.Collections.IDictionary)obj);
        return _lastStringableKeys.Value ? "map" : "cmap";
    }

    public override object Representation(object obj)
    {
        var dict = (System.Collections.IDictionary)obj;
        if (_lastStringableKeys ?? StringableKeys(dict))
        {
            _lastStringableKeys = null;
            return dict;
        }

        _lastStringableKeys = null;
        var list = new List<object>();
        foreach (System.Collections.DictionaryEntry entry in dict)
        {
            list.Add(entry.Key);
            list.Add(entry.Value!);
        }
        return TransitFactory.TaggedValue("array", list);
    }
}

internal sealed class TimeSpanWriteHandler : AbstractWriteHandler
{
    public override string Tag(object obj) => "duration";
    public override object Representation(object obj) => ((TimeSpan)obj).ToString("c", CultureInfo.InvariantCulture);
    public override string? StringRepresentation(object obj) => ((TimeSpan)obj).ToString("c", CultureInfo.InvariantCulture);
}

internal sealed class DateTimeOffsetWriteHandler : AbstractWriteHandler
{
    public override string Tag(object obj) => "dto";
    public override object Representation(object obj) => ((DateTimeOffset)obj).ToString("O");
    public override string? StringRepresentation(object obj) => ((DateTimeOffset)obj).ToString("O");
}

internal sealed class EnumWriteHandler : AbstractWriteHandler
{
    public override string Tag(object obj) => "s";
    public override object Representation(object obj) => obj.ToString()!;
    public override string? StringRepresentation(object obj) => obj.ToString();
}

internal sealed class TupleWriteHandler : AbstractWriteHandler
{
    public override string Tag(object obj) => "array";
    public override object Representation(object obj)
    {
        if (obj is System.Runtime.CompilerServices.ITuple tuple)
        {
            var list = new List<object?>(tuple.Length);
            for (int i = 0; i < tuple.Length; i++)
                list.Add(tuple[i]);
            return list;
        }
        return obj;
    }
}
