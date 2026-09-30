using System.Collections;
using System.Collections.Frozen;
using System.Numerics;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Transit.Net.Tests;

/// <summary>
/// Pins the wire format of every type a fast dispatch path in <c>Marshal</c> would bypass the
/// handler table for, and pins the cases where the handler table must still win: custom handlers
/// overriding built-in types, caller-supplied handler tables, and transforms. The corpus values
/// were captured from the handler-driven implementation, so a divergence fails loudly rather than
/// silently changing what goes on the wire.
/// </summary>
[TestClass]
public class DispatchEquivalenceTests
{
    private const string NoKey = "<null-key-unsupported>";

    private static object? Value(string name) => name switch
    {
        "null" => null,
        "empty-string" => "",
        "short-string" => "abc",
        "tilde-string" => "~tilde",
        "caret-string" => "^caret",
        "backtick-string" => "`tick",
        "long-zero" => 0L,
        "long-one" => 1L,
        "long-neg" => -1L,
        "long-min" => long.MinValue,
        "long-max" => long.MaxValue,
        "long-js-max" => 9007199254740991L,
        "long-js-over" => 9007199254740992L,
        "long-js-min" => -9007199254740991L,
        "long-js-under" => -9007199254740992L,
        "int-zero" => 0,
        "int-max" => int.MaxValue,
        "int-min" => int.MinValue,
        "bool-true" => true,
        "bool-false" => false,
        "double-zero" => 0.0d,
        "double-negzero" => -0.0d,
        "double-frac" => 1.5d,
        "double-nan" => double.NaN,
        "double-posinf" => double.PositiveInfinity,
        "double-neginf" => double.NegativeInfinity,
        "double-epsilon" => double.Epsilon,
        "double-max" => double.MaxValue,
        "float-frac" => 1.5f,
        "float-nan" => float.NaN,
        "float-posinf" => float.PositiveInfinity,
        "float-neginf" => float.NegativeInfinity,
        "char" => 'x',
        "decimal" => 1.25m,
        "biginteger" => BigInteger.Parse("123456789012345678901234567890"),
        "keyword" => TransitFactory.Keyword("kw"),
        "symbol" => TransitFactory.Symbol("sym"),
        "guid" => Guid.Parse("00112233-4455-6677-8899-aabbccddeeff"),
        "uri" => new Uri("https://example.com/a?b=c"),
        _ => throw new ArgumentOutOfRangeException(nameof(name), name),
    };

    private static readonly (string Name, string Json, string Verbose, string AsKeyJson, string AsKeyVerbose)[] Corpus =
    {
        ("null", "[\"~#'\",null]", "{\"~#'\":null}", NoKey, NoKey),
        ("empty-string", "[\"~#'\",\"\"]", "{\"~#'\":\"\"}", "[\"^ \",\"\",\"v\"]", "{\"\":\"v\"}"),
        ("short-string", "[\"~#'\",\"abc\"]", "{\"~#'\":\"abc\"}", "[\"^ \",\"abc\",\"v\"]", "{\"abc\":\"v\"}"),
        ("tilde-string", "[\"~#'\",\"~~tilde\"]", "{\"~#'\":\"~~tilde\"}", "[\"^ \",\"~~tilde\",\"v\"]", "{\"~~tilde\":\"v\"}"),
        ("caret-string", "[\"~#'\",\"~^caret\"]", "{\"~#'\":\"~^caret\"}", "[\"^ \",\"~^caret\",\"v\"]", "{\"~^caret\":\"v\"}"),
        ("backtick-string", "[\"~#'\",\"~`tick\"]", "{\"~#'\":\"~`tick\"}", "[\"^ \",\"~`tick\",\"v\"]", "{\"~`tick\":\"v\"}"),
        ("long-zero", "[\"~#'\",0]", "{\"~#'\":0}", "[\"^ \",\"~i0\",\"v\"]", "{\"~i0\":\"v\"}"),
        ("long-one", "[\"~#'\",1]", "{\"~#'\":1}", "[\"^ \",\"~i1\",\"v\"]", "{\"~i1\":\"v\"}"),
        ("long-neg", "[\"~#'\",-1]", "{\"~#'\":-1}", "[\"^ \",\"~i-1\",\"v\"]", "{\"~i-1\":\"v\"}"),
        ("long-min", "[\"~#'\",\"~i-9223372036854775808\"]", "{\"~#'\":\"~i-9223372036854775808\"}", "[\"^ \",\"~i-9223372036854775808\",\"v\"]", "{\"~i-9223372036854775808\":\"v\"}"),
        ("long-max", "[\"~#'\",\"~i9223372036854775807\"]", "{\"~#'\":\"~i9223372036854775807\"}", "[\"^ \",\"~i9223372036854775807\",\"v\"]", "{\"~i9223372036854775807\":\"v\"}"),
        ("long-js-max", "[\"~#'\",9007199254740991]", "{\"~#'\":9007199254740991}", "[\"^ \",\"~i9007199254740991\",\"v\"]", "{\"~i9007199254740991\":\"v\"}"),
        ("long-js-over", "[\"~#'\",\"~i9007199254740992\"]", "{\"~#'\":\"~i9007199254740992\"}", "[\"^ \",\"~i9007199254740992\",\"v\"]", "{\"~i9007199254740992\":\"v\"}"),
        ("long-js-min", "[\"~#'\",-9007199254740991]", "{\"~#'\":-9007199254740991}", "[\"^ \",\"~i-9007199254740991\",\"v\"]", "{\"~i-9007199254740991\":\"v\"}"),
        ("long-js-under", "[\"~#'\",\"~i-9007199254740992\"]", "{\"~#'\":\"~i-9007199254740992\"}", "[\"^ \",\"~i-9007199254740992\",\"v\"]", "{\"~i-9007199254740992\":\"v\"}"),
        ("int-zero", "[\"~#'\",0]", "{\"~#'\":0}", "[\"^ \",\"~i0\",\"v\"]", "{\"~i0\":\"v\"}"),
        ("int-max", "[\"~#'\",2147483647]", "{\"~#'\":2147483647}", "[\"^ \",\"~i2147483647\",\"v\"]", "{\"~i2147483647\":\"v\"}"),
        ("int-min", "[\"~#'\",-2147483648]", "{\"~#'\":-2147483648}", "[\"^ \",\"~i-2147483648\",\"v\"]", "{\"~i-2147483648\":\"v\"}"),
        ("bool-true", "[\"~#'\",true]", "{\"~#'\":true}", "[\"^ \",\"~?t\",\"v\"]", "{\"~?t\":\"v\"}"),
        ("bool-false", "[\"~#'\",false]", "{\"~#'\":false}", "[\"^ \",\"~?f\",\"v\"]", "{\"~?f\":\"v\"}"),
        ("double-zero", "[\"~#'\",0]", "{\"~#'\":0}", "[\"^ \",\"~d0\",\"v\"]", "{\"~d0\":\"v\"}"),
        ("double-negzero", "[\"~#'\",-0]", "{\"~#'\":-0}", "[\"^ \",\"~d-0\",\"v\"]", "{\"~d-0\":\"v\"}"),
        ("double-frac", "[\"~#'\",1.5]", "{\"~#'\":1.5}", "[\"^ \",\"~d1.5\",\"v\"]", "{\"~d1.5\":\"v\"}"),
        ("double-nan", "[\"~#'\",\"~zNaN\"]", "{\"~#'\":\"~zNaN\"}", "[\"^ \",\"~zNaN\",\"v\"]", "{\"~zNaN\":\"v\"}"),
        ("double-posinf", "[\"~#'\",\"~zINF\"]", "{\"~#'\":\"~zINF\"}", "[\"^ \",\"~zINF\",\"v\"]", "{\"~zINF\":\"v\"}"),
        ("double-neginf", "[\"~#'\",\"~z-INF\"]", "{\"~#'\":\"~z-INF\"}", "[\"^ \",\"~z-INF\",\"v\"]", "{\"~z-INF\":\"v\"}"),
        ("double-epsilon", "[\"~#'\",5E-324]", "{\"~#'\":5E-324}", "[\"^ \",\"~d5E-324\",\"v\"]", "{\"~d5E-324\":\"v\"}"),
        ("double-max", "[\"~#'\",1.7976931348623157E+308]", "{\"~#'\":1.7976931348623157E+308}", "[\"^ \",\"~d1.7976931348623157E+308\",\"v\"]", "{\"~d1.7976931348623157E+308\":\"v\"}"),
        ("float-frac", "[\"~#'\",1.5]", "{\"~#'\":1.5}", "[\"^ \",\"~d1.5\",\"v\"]", "{\"~d1.5\":\"v\"}"),
        ("float-nan", "[\"~#'\",\"~zNaN\"]", "{\"~#'\":\"~zNaN\"}", "[\"^ \",\"~zNaN\",\"v\"]", "{\"~zNaN\":\"v\"}"),
        ("float-posinf", "[\"~#'\",\"~zINF\"]", "{\"~#'\":\"~zINF\"}", "[\"^ \",\"~zINF\",\"v\"]", "{\"~zINF\":\"v\"}"),
        ("float-neginf", "[\"~#'\",\"~z-INF\"]", "{\"~#'\":\"~z-INF\"}", "[\"^ \",\"~z-INF\",\"v\"]", "{\"~z-INF\":\"v\"}"),
        ("char", "[\"~#'\",\"~cx\"]", "{\"~#'\":\"~cx\"}", "[\"^ \",\"~cx\",\"v\"]", "{\"~cx\":\"v\"}"),
        ("decimal", "[\"~#'\",\"~f1.25\"]", "{\"~#'\":\"~f1.25\"}", "[\"^ \",\"~f1.25\",\"v\"]", "{\"~f1.25\":\"v\"}"),
        ("biginteger", "[\"~#'\",\"~n123456789012345678901234567890\"]", "{\"~#'\":\"~n123456789012345678901234567890\"}", "[\"^ \",\"~n123456789012345678901234567890\",\"v\"]", "{\"~n123456789012345678901234567890\":\"v\"}"),
        ("keyword", "[\"~#'\",\"~:kw\"]", "{\"~#'\":\"~:kw\"}", "[\"^ \",\"~:kw\",\"v\"]", "{\"~:kw\":\"v\"}"),
        ("symbol", "[\"~#'\",\"~$sym\"]", "{\"~#'\":\"~$sym\"}", "[\"^ \",\"~$sym\",\"v\"]", "{\"~$sym\":\"v\"}"),
        ("guid", "[\"~#'\",\"~u00112233-4455-6677-8899-aabbccddeeff\"]", "{\"~#'\":\"~u00112233-4455-6677-8899-aabbccddeeff\"}", "[\"^ \",\"~u00112233-4455-6677-8899-aabbccddeeff\",\"v\"]", "{\"~u00112233-4455-6677-8899-aabbccddeeff\":\"v\"}"),
        ("uri", "[\"~#'\",\"~rhttps://example.com/a?b=c\"]", "{\"~#'\":\"~rhttps://example.com/a?b=c\"}", "[\"^ \",\"~rhttps://example.com/a?b=c\",\"v\"]", "{\"~rhttps://example.com/a?b=c\":\"v\"}"),
    };

    private static string Write(object? obj, TransitFactory.Format format,
        IDictionary<Type, IWriteHandler>? customHandlers = null, Func<object, object>? transform = null)
    {
        using var output = new MemoryStream();
        using var w = TransitFactory.Writer<object>(format, output, customHandlers, null, transform, ownsStream: false);
        w.Write(obj!);

        output.Position = 0;
        using var sr = new StreamReader(output, leaveOpen: true);
        return sr.ReadToEnd();
    }

    /// <summary>
    /// The same values nested one level inside a list. A top-level scalar is dispatched by
    /// <c>MarshalTop</c>, which never reaches the primitive fast path — only a nested value does,
    /// so without these the fast path is pinned nowhere.
    /// </summary>
    private static readonly (string Name, string Json, string Verbose)[] NestedCorpus =
    {
        ("null", "[null]", "[null]"),
        ("empty-string", "[\"\"]", "[\"\"]"),
        ("short-string", "[\"abc\"]", "[\"abc\"]"),
        ("tilde-string", "[\"~~tilde\"]", "[\"~~tilde\"]"),
        ("caret-string", "[\"~^caret\"]", "[\"~^caret\"]"),
        ("backtick-string", "[\"~`tick\"]", "[\"~`tick\"]"),
        ("long-zero", "[0]", "[0]"),
        ("long-one", "[1]", "[1]"),
        ("long-neg", "[-1]", "[-1]"),
        ("long-min", "[\"~i-9223372036854775808\"]", "[\"~i-9223372036854775808\"]"),
        ("long-max", "[\"~i9223372036854775807\"]", "[\"~i9223372036854775807\"]"),
        ("long-js-max", "[9007199254740991]", "[9007199254740991]"),
        ("long-js-over", "[\"~i9007199254740992\"]", "[\"~i9007199254740992\"]"),
        ("long-js-min", "[-9007199254740991]", "[-9007199254740991]"),
        ("long-js-under", "[\"~i-9007199254740992\"]", "[\"~i-9007199254740992\"]"),
        ("int-zero", "[0]", "[0]"),
        ("int-max", "[2147483647]", "[2147483647]"),
        ("int-min", "[-2147483648]", "[-2147483648]"),
        ("bool-true", "[true]", "[true]"),
        ("bool-false", "[false]", "[false]"),
        ("double-zero", "[0]", "[0]"),
        ("double-negzero", "[-0]", "[-0]"),
        ("double-frac", "[1.5]", "[1.5]"),
        ("double-nan", "[\"~zNaN\"]", "[\"~zNaN\"]"),
        ("double-posinf", "[\"~zINF\"]", "[\"~zINF\"]"),
        ("double-neginf", "[\"~z-INF\"]", "[\"~z-INF\"]"),
        ("double-epsilon", "[5E-324]", "[5E-324]"),
        ("double-max", "[1.7976931348623157E+308]", "[1.7976931348623157E+308]"),
        ("float-frac", "[1.5]", "[1.5]"),
        ("float-nan", "[\"~zNaN\"]", "[\"~zNaN\"]"),
        ("float-posinf", "[\"~zINF\"]", "[\"~zINF\"]"),
        ("float-neginf", "[\"~z-INF\"]", "[\"~z-INF\"]"),
        ("char", "[\"~cx\"]", "[\"~cx\"]"),
        ("decimal", "[\"~f1.25\"]", "[\"~f1.25\"]"),
        ("biginteger", "[\"~n123456789012345678901234567890\"]", "[\"~n123456789012345678901234567890\"]"),
        ("keyword", "[\"~:kw\"]", "[\"~:kw\"]"),
        ("symbol", "[\"~$sym\"]", "[\"~$sym\"]"),
        ("guid", "[\"~u00112233-4455-6677-8899-aabbccddeeff\"]", "[\"~u00112233-4455-6677-8899-aabbccddeeff\"]"),
        ("uri", "[\"~rhttps://example.com/a?b=c\"]", "[\"~rhttps://example.com/a?b=c\"]"),
    };

    private static object? Read(string transit, TransitFactory.Format format)
    {
        using var input = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(transit));
        return TransitFactory.Reader(format, input).Read<object>();
    }

    [TestMethod]
    public void TestCorpusWireFormatIsStable()
    {
        foreach (var (name, json, verbose, asKeyJson, asKeyVerbose) in Corpus)
        {
            var value = Value(name);

            Assert.AreEqual(json, Write(value, TransitFactory.Format.Json), $"{name}: json");
            Assert.AreEqual(verbose, Write(value, TransitFactory.Format.JsonVerbose), $"{name}: verbose");

            if (asKeyJson == NoKey)
                continue;

            // As a dictionary key the encoding differs: numbers and booleans become tagged strings.
            var asKey = new Dictionary<object, object> { { value!, "v" } };
            Assert.AreEqual(asKeyJson, Write(asKey, TransitFactory.Format.Json), $"{name}: json key");
            Assert.AreEqual(asKeyVerbose, Write(asKey, TransitFactory.Format.JsonVerbose), $"{name}: verbose key");
        }
    }

    [TestMethod]
    public void TestCustomHandlerOverridesBuiltInTypes()
    {
        var custom = new Dictionary<Type, IWriteHandler>
        {
            [typeof(string)] = new PrefixingHandler("S:"),
            [typeof(long)] = new PrefixingHandler("L:"),
            [typeof(bool)] = new PrefixingHandler("B:"),
        };

        Assert.AreEqual("[\"~#'\",\"S:abc\"]", Write("abc", TransitFactory.Format.Json, custom));
        Assert.AreEqual("[\"~#'\",\"L:42\"]", Write(42L, TransitFactory.Format.Json, custom));
        Assert.AreEqual("[\"~#'\",\"B:True\"]", Write(true, TransitFactory.Format.Json, custom));
        Assert.AreEqual("{\"~#'\":\"S:abc\"}", Write("abc", TransitFactory.Format.JsonVerbose, custom));
    }

    [TestMethod]
    public void TestNestedCorpusWireFormatIsStable()
    {
        foreach (var (name, json, verbose) in NestedCorpus)
        {
            var nested = new List<object?> { Value(name) };
            Assert.AreEqual(json, Write(nested, TransitFactory.Format.Json), $"{name}: nested json");
            Assert.AreEqual(verbose, Write(nested, TransitFactory.Format.JsonVerbose), $"{name}: nested verbose");
        }
    }

    [TestMethod]
    public void TestCustomHandlerOverridesBuiltInTypesWhenNested()
    {
        // The nested position is the one that reaches the primitive fast path, so this is where
        // the guard on that path is actually exercised.
        var custom = new Dictionary<Type, IWriteHandler>
        {
            [typeof(string)] = new PrefixingHandler("S:"),
            [typeof(long)] = new PrefixingHandler("L:"),
            [typeof(bool)] = new PrefixingHandler("B:"),
            [typeof(int)] = new PrefixingHandler("I:"),
            [typeof(double)] = new PrefixingHandler("D:"),
        };

        var nested = new List<object> { "abc", 42L, true, 7, 1.5d };
        Assert.AreEqual("[\"S:abc\",\"L:42\",\"B:True\",\"I:7\",\"D:1.5\"]",
            Write(nested, TransitFactory.Format.Json, custom));
        Assert.AreEqual("[\"S:abc\",\"L:42\",\"B:True\",\"I:7\",\"D:1.5\"]",
            Write(nested, TransitFactory.Format.JsonVerbose, custom));

        // Same again as dictionary values, the other nested position.
        var asValues = new Dictionary<object, object> { { "k", 42L } };
        Assert.AreEqual("[\"^ \",\"S:k\",\"L:42\"]", Write(asValues, TransitFactory.Format.Json, custom));
    }

    [TestMethod]
    public void TestEachFastPathedTypeIsGuardedIndividually()
    {
        // One override at a time. Overriding several at once hides a type dropping out of the
        // guard's list, because any one of the others still disables the fast path wholesale.
        (Type Type, object Value, string Expected)[] cases =
        {
            (typeof(string), "abc", "X:abc"),
            (typeof(long), 42L, "X:42"),
            (typeof(int), 7, "X:7"),
            (typeof(bool), true, "X:True"),
            (typeof(double), 1.5d, "X:1.5"),
            (typeof(IKeyword), TransitFactory.Keyword("kw"), "X:kw"),
        };

        foreach (var (type, value, expected) in cases)
        {
            var custom = new Dictionary<Type, IWriteHandler> { [type] = new PrefixingHandler("X:") };
            var nested = new List<object> { value };

            Assert.AreEqual($"[\"{expected}\"]",
                Write(nested, TransitFactory.Format.Json, custom), $"{type.Name} nested value");
            Assert.AreEqual($"[\"{expected}\"]",
                Write(nested, TransitFactory.Format.JsonVerbose, custom), $"{type.Name} nested verbose");
        }
    }

    [TestMethod]
    public void TestCustomHandlerRegisteredByInterfaceWins()
    {
        // Keyword and Symbol are internal structs, so a consumer can only register against the
        // interface. Registering their concrete types in the stock table would shadow this.
        var custom = new Dictionary<Type, IWriteHandler>
        {
            [typeof(IKeyword)] = new PrefixingHandler("K:"),
            [typeof(ISymbol)] = new PrefixingHandler("Y:"),
        };

        Assert.AreEqual("[\"~#'\",\"K:kw\"]",
            Write(TransitFactory.Keyword("kw"), TransitFactory.Format.Json, custom));
        Assert.AreEqual("[\"~#'\",\"Y:sym\"]",
            Write(TransitFactory.Symbol("sym"), TransitFactory.Format.Json, custom));
        Assert.AreEqual("[\"^ \",\"K:kw\",\"v\"]",
            Write(new Dictionary<object, object> { { TransitFactory.Keyword("kw"), "v" } },
                TransitFactory.Format.Json, custom));
    }

    [TestMethod]
    public void TestFrozenTableInterfaceOverrideWins()
    {
        // A caller who builds a frozen table from the defaults and overrides IKeyword would
        // otherwise be shadowed by the concrete-struct entry the defaults carry.
        var table = TransitFactory.DefaultWriteHandlers().ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
        table[typeof(IKeyword)] = new PrefixingHandler("K:");
        var frozen = table.ToFrozenDictionary();

        Assert.AreEqual("[\"~#'\",\"K:kw\"]",
            Write(TransitFactory.Keyword("kw"), TransitFactory.Format.Json, frozen));

        // A frozen table with no such override is still used verbatim.
        var untouched = TransitFactory.DefaultWriteHandlers().ToFrozenDictionary(kvp => kvp.Key, kvp => kvp.Value);
        Assert.AreEqual("[\"~#'\",\"~:kw\"]",
            Write(TransitFactory.Keyword("kw"), TransitFactory.Format.Json, untouched));
    }

    [TestMethod]
    public void TestTransformAppliedOncePerValue()
    {
        // Counts invocations rather than asserting output, because an idempotent transform
        // cannot distinguish one application from two.
        var counts = new Dictionary<string, int>();
        object Count(object o)
        {
            if (o is string s && s.StartsWith("t-", StringComparison.Ordinal))
                counts[s] = counts.TryGetValue(s, out var n) ? n + 1 : 1;
            return o;
        }

        // Top-level scalar.
        counts.Clear();
        Write("t-scalar", TransitFactory.Format.Json, null, Count);
        Assert.AreEqual(1, counts["t-scalar"], "top-level scalar");

        // Top-level collection, and a value nested inside it.
        counts.Clear();
        Write(new Dictionary<object, object> { { "t-key", "t-value" } },
            TransitFactory.Format.Json, null, Count);
        Assert.AreEqual(1, counts["t-key"], "dictionary key");
        Assert.AreEqual(1, counts["t-value"], "dictionary value");
    }

    [TestMethod]
    public void TestLongTaggedValuesExceedingComposeBuffer()
    {
        // Longer than the stack compose buffer, so it takes the heap fallback. Written twice so
        // the cache-hit path over an oversized value is covered too.
        var longKeyword = TransitFactory.Keyword(new string('k', 400));
        var longUri = new Uri("https://example.com/" + new string('p', 400));
        var blob = new byte[400];
        for (var i = 0; i < blob.Length; i++) blob[i] = (byte)i;

        foreach (var value in new object[] { longKeyword, longUri, blob })
        {
            var twice = new List<object> { value, value };
            var json = Write(twice, TransitFactory.Format.Json);
            var verbose = Write(twice, TransitFactory.Format.JsonVerbose);

            Assert.IsTrue(json.Length > 256, $"{value.GetType().Name}: json length");
            // Round-tripping proves the composed text and any cache code agree with the reader.
            var back = (IList)Read(json, TransitFactory.Format.Json)!;
            Assert.AreEqual(2, back.Count, $"{value.GetType().Name}: json roundtrip count");
            var backVerbose = (IList)Read(verbose, TransitFactory.Format.JsonVerbose)!;
            Assert.AreEqual(2, backVerbose.Count, $"{value.GetType().Name}: verbose roundtrip count");
        }
    }

    [TestMethod]
    public void TestTaggedValueDictionaryKeysOfOneType()
    {
        // ITaggedValue is the one handler whose tag comes from the instance, which is why the
        // StringableKeys memo excludes it. Two keys of the same runtime type, differing tag
        // lengths, in both orders.
        var shortTag = TransitFactory.TaggedValue("u", "00112233-4455-6677-8899-aabbccddeeff");
        var longTag = TransitFactory.TaggedValue("point", new List<object> { 1L, 2L });

        var shortFirst = new Dictionary<object, object> { { shortTag, "a" }, { longTag, "b" } };
        var longFirst = new Dictionary<object, object> { { longTag, "b" }, { shortTag, "a" } };

        // A composite key forces cmap whichever order the scan encounters it in.
        StringAssert.Contains(Write(shortFirst, TransitFactory.Format.Json), "~#cmap");
        StringAssert.Contains(Write(longFirst, TransitFactory.Format.Json), "~#cmap");
    }

    [TestMethod]
    public void TestCustomHandlerAlongsideStockHandlers()
    {
        // The shape xledger uses: a handful of overrides merged over the stock table.
        var custom = new Dictionary<Type, IWriteHandler>
        {
            [typeof(decimal)] = new PrefixingHandler("D:"),
        };

        Assert.AreEqual("[\"~#'\",\"D:1.25\"]", Write(1.25m, TransitFactory.Format.Json, custom));
        // Everything not overridden must still use the stock encoding.
        Assert.AreEqual("[\"~#'\",\"abc\"]", Write("abc", TransitFactory.Format.Json, custom));
        Assert.AreEqual("[\"~#'\",42]", Write(42L, TransitFactory.Format.Json, custom));
        Assert.AreEqual("[\"~#'\",true]", Write(true, TransitFactory.Format.Json, custom));
        Assert.AreEqual("[\"~#'\",\"~:kw\"]", Write(TransitFactory.Keyword("kw"), TransitFactory.Format.Json, custom));
    }

    [TestMethod]
    public void TestCallerSuppliedTableWithoutStringHandler()
    {
        // A frozen table is taken verbatim, so it can legitimately lack a type the stock table has.
        var table = TransitFactory.DefaultWriteHandlers()
            .Where(kvp => kvp.Key != typeof(string))
            .ToFrozenDictionary(kvp => kvp.Key, kvp => kvp.Value);

        Assert.AreEqual("[\"~#'\",42]", Write(42L, TransitFactory.Format.Json, table));
        // string is gone from the table, so it resolves through IEnumerable to a list of chars.
        // A fast path keyed on the CLR type rather than the table would wrongly emit "abc".
        Assert.AreEqual("[\"~#list\",[\"~ca\",\"~cb\",\"~cc\"]]",
            Write("abc", TransitFactory.Format.Json, table));
    }

    [TestMethod]
    public void TestTransformRunsBeforeDispatch()
    {
        // The transform can change an object's type, so dispatch must see its result.
        static object Transform(object o) => o is long l ? "transformed-" + l : o;

        Assert.AreEqual("[\"~#'\",\"transformed-7\"]",
            Write(7L, TransitFactory.Format.Json, null, Transform));
        Assert.AreEqual("[\"^ \",\"transformed-7\",\"abc\"]",
            Write(new Dictionary<object, object> { { 7L, "abc" } }, TransitFactory.Format.Json, null, Transform));
    }

    private sealed class PrefixingHandler : IWriteHandler
    {
        private readonly string _prefix;
        public PrefixingHandler(string prefix) => _prefix = prefix;

        public string Tag(object obj) => "s";
        public object Representation(object obj) => _prefix + obj;
        public string? StringRepresentation(object obj) => _prefix + obj;
        public IWriteHandler? GetVerboseHandler() => null;
    }
}
