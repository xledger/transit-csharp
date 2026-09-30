using System.Collections;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Transit.Net.Impl;

namespace Transit.Net.Tests;

/// <summary>
/// Covers the write cache keyed on (prefix, tag, value) and the read path that resolves
/// substitution codes straight from UTF-8, including the cases where either must fall back.
/// </summary>
[TestClass]
public class CacheEncodingTests
{
    private static string Write(object obj, TransitFactory.Format format)
    {
        using var output = new MemoryStream();
        using var w = TransitFactory.Writer<object>(format, output, ownsStream: false);
        w.Write(obj);

        output.Position = 0;
        using var sr = new StreamReader(output, leaveOpen: true);
        return sr.ReadToEnd();
    }

    private static object? Read(string transit, TransitFactory.Format format)
    {
        using var input = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(transit));
        return TransitFactory.Reader(format, input).Read<object>();
    }

    private static void AssertRoundTrips(IDictionary expected)
    {
        foreach (var format in new[] { TransitFactory.Format.Json, TransitFactory.Format.JsonVerbose })
        {
            var actual = (IDictionary)Read(Write(expected, format), format)!;

            Assert.AreEqual(expected.Count, actual.Count, $"{format}: entry count");
            foreach (DictionaryEntry entry in expected)
                Assert.AreEqual(entry.Value, actual[entry.Key!], $"{format}: value for {entry.Key}");
        }
    }

    [TestMethod]
    public void TestRepeatedKeywordKeysRoundTrip()
    {
        // The same keyword keys recur across many nested maps, so every one after the first is
        // written as a substitution code and read back through the UTF-8 fast path.
        var rows = new List<object>();
        for (var i = 0; i < 50; i++)
        {
            rows.Add(new Dictionary<object, object>
            {
                { TransitFactory.Keyword("id"), (long)i },
                { TransitFactory.Keyword("name"), "row-" + i },
                { TransitFactory.Keyword("active"), i % 2 == 0 },
            });
        }

        var payload = new Dictionary<object, object>
        {
            { TransitFactory.Keyword("rows"), rows },
            { TransitFactory.Keyword("count"), (long)rows.Count },
        };

        foreach (var format in new[] { TransitFactory.Format.Json, TransitFactory.Format.JsonVerbose })
        {
            var actual = (IDictionary)Read(Write(payload, format), format)!;
            var actualRows = (IList)actual[TransitFactory.Keyword("rows")]!;

            Assert.AreEqual(50L, actual[TransitFactory.Keyword("count")], $"{format}: count");
            Assert.AreEqual(50, actualRows.Count, $"{format}: row count");

            for (var i = 0; i < 50; i++)
            {
                var row = (IDictionary)actualRows[i]!;
                Assert.AreEqual((long)i, row[TransitFactory.Keyword("id")], $"{format}: id {i}");
                Assert.AreEqual("row-" + i, row[TransitFactory.Keyword("name")], $"{format}: name {i}");
                Assert.AreEqual(i % 2 == 0, row[TransitFactory.Keyword("active")], $"{format}: active {i}");
            }
        }
    }

    [TestMethod]
    public void TestStringsResemblingCacheCodesRoundTrip()
    {
        // These are escaped on write, so the reader must not mistake them for substitution codes.
        AssertRoundTrips(new Dictionary<object, object>
        {
            { "^0", "^0" },
            { "^ ", "^ " },
            { "^", "^" },
            { "~:foo", "~:foo" },
            { "^abcdef", "^abcdef" },
            { TransitFactory.Keyword("real-keyword"), "^1" },
        });
    }

    [TestMethod]
    public void TestEscapedKeysRoundTrip()
    {
        // Quotes, backslashes and control characters make Utf8JsonReader report the token as
        // escaped, which must send the reader down the string path rather than the span path.
        AssertRoundTrips(new Dictionary<object, object>
        {
            { "quote\"key", "quote\"value" },
            { "back\\slash", "back\\slash" },
            { "new\nline", "tab\there" },
            { "unicodecontrol", "ok" },
        });
    }

    [TestMethod]
    public void TestNonAsciiKeysRoundTrip()
    {
        AssertRoundTrips(new Dictionary<object, object>
        {
            { "nøkkel", "verdi" },
            { "键", "值" },
            { "emoji-🔑", "🙂" },
            { TransitFactory.Keyword("æøå"), "nordic" },
        });
    }

    [TestMethod]
    public void TestCacheWrapAroundRoundTrips()
    {
        // More distinct cacheable keys than the cache holds, so it resets partway through.
        var payload = new Dictionary<object, object>();
        for (var i = 0; i < WriteCache.MaxCacheEntries + 100; i++)
            payload[TransitFactory.Keyword("key-" + i)] = (long)i;

        AssertRoundTrips(payload);
    }

    [TestMethod]
    public void TestDistinctTriplesComposingAlikeStayDistinct()
    {
        // A keyword and a symbol of the same name share no cache slot, and neither collides
        // with a plain string carrying the composed text.
        AssertRoundTrips(new Dictionary<object, object>
        {
            { TransitFactory.Keyword("shared"), "keyword-value" },
            { TransitFactory.Symbol("shared"), "symbol-value" },
            { "shared", "string-value" },
            { "~:shared", "escaped-string-value" },
        });
    }
}
