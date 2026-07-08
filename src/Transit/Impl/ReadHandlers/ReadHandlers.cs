using System.Globalization;
using System.Numerics;
using Transit.Net.Numerics;

namespace Transit.Net.Impl.ReadHandlers;

internal sealed class KeywordReadHandler : IReadHandler
{
    public object FromRepresentation(object representation)
        => new Keyword((string)representation);
}

internal sealed class SymbolReadHandler : IReadHandler
{
    public object FromRepresentation(object representation)
        => new Symbol((string)representation);
}

internal sealed class IntegerReadHandler : IReadHandler
{
    public object FromRepresentation(object representation)
        => long.Parse((string)representation);
}

internal sealed class BooleanReadHandler : IReadHandler
{
    public object FromRepresentation(object representation)
        => ((string)representation) == "t";
}

internal sealed class NullReadHandler : IReadHandler
{
    public object FromRepresentation(object representation) => null!;
}

internal sealed class BigRationalReadHandler : IReadHandler
{
    public object FromRepresentation(object representation)
        => new BigRational(decimal.Parse((string)representation, CultureInfo.InvariantCulture));
}

internal sealed class BigIntegerReadHandler : IReadHandler
{
    public object FromRepresentation(object representation)
        => BigInteger.Parse((string)representation);
}

internal sealed class DoubleReadHandler : IReadHandler
{
    public object FromRepresentation(object representation)
    {
        if (representation is double d) return d;
        return double.Parse((string)representation, CultureInfo.InvariantCulture);
    }
}

internal sealed class SpecialNumberReadHandler : IReadHandler
{
    public object FromRepresentation(object representation)
    {
        return (string)representation switch
        {
            "NaN" => double.NaN,
            "INF" => double.PositiveInfinity,
            "-INF" => double.NegativeInfinity,
            _ => throw new TransitException("Unknown special number: " + representation)
        };
    }
}

internal sealed class CharacterReadHandler : IReadHandler
{
    public object FromRepresentation(object representation)
        => ((string)representation)[0];
}

internal sealed class VerboseDateTimeReadHandler : IReadHandler
{
    public object FromRepresentation(object representation)
        => DateTimeOffset.Parse((string)representation, CultureInfo.InvariantCulture)
            .LocalDateTime;
}

internal sealed class DateTimeReadHandler : IReadHandler
{
    public object FromRepresentation(object representation)
        => Transit.Net.Java.Convert.FromJavaTime(System.Convert.ToInt64(representation));
}

internal sealed class UriReadHandler : IReadHandler
{
    public object FromRepresentation(object representation)
        => new Uri((string)representation);
}

internal sealed class GuidReadHandler : IReadHandler
{
    public object FromRepresentation(object representation)
    {
        if (representation is IList<object> list)
        {
            long msb = System.Convert.ToInt64(list[0]);
            long lsb = System.Convert.ToInt64(list[1]);
            return Transit.Net.Java.Uuid.ToGuid(msb, lsb);
        }
        return Guid.Parse((string)representation);
    }
}

internal sealed class BinaryReadHandler : IReadHandler
{
    public object FromRepresentation(object representation)
        => System.Convert.FromBase64String((string)representation);
}

internal sealed class IdentityReadHandler : IReadHandler
{
    public object FromRepresentation(object representation) => representation;
}

internal sealed class SetReadHandler : IListReadHandler
{
    public object FromRepresentation(object representation) => representation;

    public IListReader ListReader() => new SetListReader();

    private sealed class SetListReader : IListReader
    {
        public object Init() => new HashSet<object>();
        public object Add(object list, object item) { ((HashSet<object>)list).Add(item); return list; }
        public object Complete(object list) => list;
    }
}

internal sealed class ListReadHandler : IListReadHandler
{
    public object FromRepresentation(object representation) => representation;

    public IListReader ListReader() => new ListWrapperReader();

    private sealed class ListWrapperReader : IListReader
    {
        public object Init() => new ListWrapper();
        public object Add(object list, object item) { ((ListWrapper)list).Add(item); return list; }
        public object Complete(object list) => list;
    }
}

internal sealed class RatioReadHandler : IListReadHandler
{
    public object FromRepresentation(object representation) => representation;

    public IListReader ListReader() => new RatioListReader();

    private sealed class RatioListReader : IListReader
    {
        private BigInteger _numerator;
        private int _count;

        public object Init() { _count = 0; return null!; }
        public object Add(object list, object item)
        {
            if (_count == 0) _numerator = (BigInteger)item;
            _count++;
            if (_count == 2) return new Ratio(_numerator, (BigInteger)item);
            return null!;
        }
        public object Complete(object list) => list;
    }
}

internal sealed class CDictionaryReadHandler : IListReadHandler
{
    public object FromRepresentation(object representation) => representation;

    public IListReader ListReader() => new CDictListReader();

    private sealed class CDictListReader : IListReader
    {
        private object? _key;
        private bool _hasKey;

        public object Init() => new NullKeyDictionary();
        public object Add(object list, object item)
        {
            var d = (NullKeyDictionary)list;
            if (!_hasKey)
            {
                _key = item;
                _hasKey = true;
            }
            else
            {
                d[_key] = item;
                _hasKey = false;
            }
            return d;
        }
        public object Complete(object list) => list;
    }
}

internal sealed class LinkReadHandler : IDictionaryReadHandler
{
    public object FromRepresentation(object representation) => representation;

    public IDictionaryReader DictionaryReader() => new LinkDictReader();

    private sealed class LinkDictReader : IDictionaryReader
    {
        public object Init() => new Dictionary<object, object>();
        public object Add(object dictionary, object key, object value)
        {
            ((Dictionary<object, object>)dictionary)[key] = value;
            return dictionary;
        }

        private static object? FindValue(Dictionary<object, object> d, string keyName)
        {
            // Try string key first
            if (d.TryGetValue(keyName, out var val))
                return val;
            // Then try keyword key
            foreach (var kv in d)
            {
                if (kv.Key is IKeyword k && k.Value == keyName)
                    return kv.Value;
            }
            return null;
        }

        public object Complete(object dictionary)
        {
            var d = (Dictionary<object, object>)dictionary;
            var href = FindValue(d, "href");
            var rel = FindValue(d, "rel");
            var name = FindValue(d, "name");
            var prompt = FindValue(d, "prompt");
            var render = FindValue(d, "render");

            return new Link(
                href is Uri u ? u : new Uri((string)href!),
                (string)rel!,
                (string?)name,
                (string?)prompt,
                (string?)render
            );
        }
    }
}

internal sealed class DefaultReadHandler : IDefaultReadHandler<ITaggedValue>
{
    public ITaggedValue FromRepresentation(string tag, object representation)
        => TransitFactory.TaggedValue(tag, representation);
}

internal sealed class TimeSpanReadHandler : IReadHandler
{
    public object FromRepresentation(object representation)
        => TimeSpan.Parse((string)representation, CultureInfo.InvariantCulture);
}

internal sealed class DateTimeOffsetReadHandler : IReadHandler
{
    public object FromRepresentation(object representation)
        => DateTimeOffset.Parse((string)representation, CultureInfo.InvariantCulture);
}
