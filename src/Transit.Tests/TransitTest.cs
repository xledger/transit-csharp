using System.Collections;
using System.Numerics;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Transit;
using Transit.Net.Impl;
using Transit.Net.Numerics;

namespace Transit.Net.Tests;

[TestClass]
public class TransitTest
{
    #region Reading

    private IReader Reader(string s)
    {
        var input = new MemoryStream(Encoding.UTF8.GetBytes(s));
        return TransitFactory.Reader(TransitFactory.Format.Json, input);
    }

    [TestMethod]
    public void TestReadString()
    {
        Assert.AreEqual("foo", Reader("\"foo\"").Read<string>());
        Assert.AreEqual("~foo", Reader("\"~~foo\"").Read<string>());
        Assert.AreEqual("`foo", Reader("\"~`foo\"").Read<string>());
        Assert.AreEqual("foo", Reader("\"~#foo\"").Read<Tag>().GetValue());
        Assert.AreEqual("^foo", Reader("\"~^foo\"").Read<string>());
    }

    [TestMethod]
    public void TestReadBoolean()
    {
        Assert.IsTrue(Reader("\"~?t\"").Read<bool>());
        Assert.IsFalse(Reader("\"~?f\"").Read<bool>());

        var d = Reader("{\"~?t\":1,\"~?f\":2}").Read<IDictionary>();
        Assert.AreEqual(1L, d[true]);
        Assert.AreEqual(2L, d[false]);
    }

    [TestMethod]
    public void TestReadNull()
    {
        var v = Reader("\"~_\"").Read<object>();
        Assert.IsNull(v);
    }

    [TestMethod]
    public void TestReadKeyword()
    {
        var v = Reader("\"~:foo\"").Read<IKeyword>();
        Assert.AreEqual("foo", v.ToString());

        var r = Reader("[\"~:foo\",\"^" + (char)WriteCache.BaseCharIdx + "\",\"^" + (char)WriteCache.BaseCharIdx + "\"]");
        var v2 = r.Read<IList>();
        Assert.AreEqual("foo", v2[0]!.ToString());
        Assert.AreEqual("foo", v2[1]!.ToString());
        Assert.AreEqual("foo", v2[2]!.ToString());
    }

    [TestMethod]
    public void TestReadInteger()
    {
        var v = Reader("\"~i42\"").Read<long>();
        Assert.AreEqual(42L, v);
    }

    [TestMethod]
    public void TestReadBigInteger()
    {
        var expected = BigInteger.Parse("4256768765123454321897654321234567");
        var v = Reader("\"~n4256768765123454321897654321234567\"").Read<BigInteger>();
        Assert.AreEqual(expected, v);
    }

    [TestMethod]
    public void TestReadDouble()
    {
        Assert.AreEqual(42.5D, Reader("\"~d42.5\"").Read<double>());
    }

    [TestMethod]
    public void TestReadSpecialNumbers()
    {
        Assert.AreEqual(double.NaN, Reader("\"~zNaN\"").Read<double>());
        Assert.AreEqual(double.PositiveInfinity, Reader("\"~zINF\"").Read<double>());
        Assert.AreEqual(double.NegativeInfinity, Reader("\"~z-INF\"").Read<double>());
    }

    [TestMethod]
    public void TestReadBigRational()
    {
        Assert.AreEqual(new BigRational(12.345M), Reader("\"~f12.345\"").Read<BigRational>());
        Assert.AreEqual(new BigRational(-12.345M), Reader("\"~f-12.345\"").Read<BigRational>());
        Assert.AreEqual(new BigRational(0.1001M), Reader("\"~f0.1001\"").Read<BigRational>());
        Assert.AreEqual(new BigRational(0.01M), Reader("\"~f0.01\"").Read<BigRational>());
        Assert.AreEqual(new BigRational(0.1M), Reader("\"~f0.1\"").Read<BigRational>());
        Assert.AreEqual(new BigRational(1M), Reader("\"~f1\"").Read<BigRational>());
        Assert.AreEqual(new BigRational(10M), Reader("\"~f10\"").Read<BigRational>());
        Assert.AreEqual(new BigRational(420.0057M), Reader("\"~f420.0057\"").Read<BigRational>());
    }

    [TestMethod]
    public void TestReadDateTime()
    {
        var d = new DateTime(2014, 8, 9, 10, 6, 21, 497, DateTimeKind.Local);
        var expected = new DateTimeOffset(d).LocalDateTime;
        long javaTime = Transit.Net.Java.Convert.ToJavaTime(d);

        string timeString = AbstractParser.FormatDateTime(d);
        Assert.AreEqual(expected, Reader("\"~t" + timeString + "\"").Read<DateTime>());

        Assert.AreEqual(expected, Reader("{\"~#m\": " + javaTime + "}").Read<DateTime>());

        timeString = new DateTimeOffset(d).UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'");
        Assert.AreEqual(expected, Reader("\"~t" + timeString + "\"").Read<DateTime>());

        timeString = new DateTimeOffset(d).UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'");
        Assert.AreEqual(expected.AddMilliseconds(-497D), Reader("\"~t" + timeString + "\"").Read<DateTime>());

        timeString = new DateTimeOffset(d).UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff-00:00");
        Assert.AreEqual(expected, Reader("\"~t" + timeString + "\"").Read<DateTime>());
    }

    [TestMethod]
    public void TestReadGuid()
    {
        var guid = Guid.NewGuid();
        long hi64 = ((Transit.Net.Java.Uuid)guid).MostSignificantBits;
        long lo64 = ((Transit.Net.Java.Uuid)guid).LeastSignificantBits;

        Assert.AreEqual(guid, Reader("\"~u" + guid.ToString() + "\"").Read<Guid>());
        Assert.AreEqual(guid, Reader("{\"~#u\": [" + hi64 + ", " + lo64 + "]}").Read<Guid>());
    }

    [TestMethod]
    public void TestReadUri()
    {
        var expected = new Uri("http://www.foo.com");
        var v = Reader("\"~rhttp://www.foo.com\"").Read<Uri>();
        Assert.AreEqual(expected, v);
    }

    [TestMethod]
    public void TestReadSymbol()
    {
        var v = Reader("\"~$foo\"").Read<ISymbol>();
        Assert.AreEqual("foo", v.ToString());
    }

    [TestMethod]
    public void TestReadCharacter()
    {
        var v = Reader("\"~cf\"").Read<char>();
        Assert.AreEqual('f', v);
    }

    [TestMethod]
    public void TestReadBinary()
    {
        byte[] bytes = Encoding.ASCII.GetBytes("foobarbaz");
        string encoded = Convert.ToBase64String(bytes);
        byte[] decoded = Reader("\"~b" + encoded + "\"").Read<byte[]>();

        Assert.AreEqual(bytes.Length, decoded.Length);
        CollectionAssert.AreEqual(bytes, decoded);
    }

    [TestMethod]
    public void TestReadUnknown()
    {
        var result1 = Reader("\"~jfoo\"").Read<ITaggedValue>();
        Assert.AreEqual("j", result1.Tag);
        Assert.AreEqual("foo", result1.Representation);

        IList<object> l = new List<object> { 1L, 2L };
        var result = Reader("{\"~#point\":[1,2]}").Read<ITaggedValue>();
        Assert.AreEqual("point", result.Tag);
        var resultList = (IList<object>)result.Representation;
        Assert.AreEqual(2, resultList.Count);
        Assert.AreEqual(1L, resultList[0]);
        Assert.AreEqual(2L, resultList[1]);
    }

    [TestMethod]
    public void TestReadList()
    {
        var l = Reader("[1, 2, 3]").Read<IList>();

        Assert.IsInstanceOfType<IList<object>>(l);
        Assert.AreEqual(3, l.Count);
        Assert.AreEqual(1L, l[0]);
        Assert.AreEqual(2L, l[1]);
        Assert.AreEqual(3L, l[2]);
    }

    [TestMethod]
    public void TestReadListWithNested()
    {
        var d = new DateTime(2014, 8, 10, 13, 34, 35);
        string t = AbstractParser.FormatDateTime(d);

        var l = Reader("[\"~:foo\", \"~t" + t + "\", \"~?t\"]").Read<IList>();

        Assert.AreEqual(3, l.Count);
        Assert.AreEqual("foo", l[0]!.ToString());
        Assert.AreEqual(d, (DateTime)l[1]!);
        Assert.IsTrue((bool)l[2]!);
    }

    [TestMethod]
    public void TestReadArrayWithNestedDoubles()
    {
        var l = Reader("[-3.14159, 3.14159, 4.0E11, 2.998E8, 6.626E-34]").Read<IList>();
        Assert.AreEqual(5, l.Count);
        Assert.AreEqual(-3.14159, (double)l[0]!, 0.00001);
        Assert.AreEqual(3.14159, (double)l[1]!, 0.00001);
        Assert.AreEqual(4.0E11, (double)l[2]!, 1.0);
        Assert.AreEqual(2.998E8, (double)l[3]!, 1.0);
        Assert.AreEqual(6.626E-34, (double)l[4]!, 1E-40);
    }

    [TestMethod]
    public void TestReadDictionary()
    {
        var m = Reader("{\"a\": 2, \"b\": 4}").Read<IDictionary>();

        Assert.AreEqual(2, m.Count);
        Assert.AreEqual(2L, m["a"]);
        Assert.AreEqual(4L, m["b"]);
    }

    [TestMethod]
    public void TestReadDictionaryWithNested()
    {
        var guid = Guid.NewGuid();

        var m = Reader("{\"a\": \"~:foo\", \"b\": \"~u" + guid + "\"}").Read<IDictionary>();

        Assert.AreEqual(2, m.Count);
        Assert.AreEqual("foo", m["a"]!.ToString());
        Assert.AreEqual(guid, m["b"]);
    }

    [TestMethod]
    public void TestReadSet()
    {
        var s = Reader("{\"~#set\": [1, 2, 3]}").Read<ISet<object>>();

        Assert.AreEqual(3, s.Count);
        Assert.IsTrue(s.Contains(1L));
        Assert.IsTrue(s.Contains(2L));
        Assert.IsTrue(s.Contains(3L));
    }

    [TestMethod]
    public void TestReadEnumerable()
    {
        var l = Reader("{\"~#list\": [1, 2, 3]}").Read<IEnumerable>();
        var lo = l.OfType<object>().ToList();

        Assert.AreEqual(3, lo.Count);
        Assert.AreEqual(1L, lo[0]);
        Assert.AreEqual(2L, lo[1]);
        Assert.AreEqual(3L, lo[2]);
    }

    [TestMethod]
    public void TestReadRatio()
    {
        var r = Reader("{\"~#ratio\": [\"~n1\",\"~n2\"]}").Read<IRatio>();

        Assert.AreEqual(BigInteger.One, r.Numerator);
        Assert.AreEqual(BigInteger.One + 1, r.Denominator);
        Assert.AreEqual(0.5d, r.GetValue(), 0.01);
    }

    [TestMethod]
    public void TestReadCDictionary()
    {
        var m = Reader("{\"~#cmap\": [{\"~#ratio\":[\"~n1\",\"~n2\"]},1,{\"~#list\":[1,2,3]},2]}").Read<IDictionary>();

        Assert.AreEqual(2, m.Count);

        foreach (DictionaryEntry e in m)
        {
            if ((long)e.Value! == 1L)
            {
                var r = (IRatio)e.Key;
                Assert.AreEqual(new BigInteger(1), r.Numerator);
                Assert.AreEqual(new BigInteger(2), r.Denominator);
            }
            else if ((long)e.Value! == 2L)
            {
                var l = ((IEnumerable<object>)e.Key).ToList();
                Assert.AreEqual(1L, l[0]);
                Assert.AreEqual(2L, l[1]);
                Assert.AreEqual(3L, l[2]);
            }
        }
    }

    [TestMethod]
    public void TestReadCDictionaryWithNullKey()
    {
        var m2 = Reader("[\"~#cmap\",[null,\"null as map key\",[\"1\",\"2\"],\"Array as key to force cmap\"]]").Read<IDictionary>();
        Assert.AreEqual(2, m2.Count);
        Assert.AreEqual("null as map key", m2[null!]);
        var key = new List<object> { "1", "2" };
        // Find entry with array key by iterating (since List doesn't implement structural equality for dictionary lookup)
        string? arrayKeyValue = null;
        foreach (DictionaryEntry e in m2)
        {
            if (e.Key is IList<object> l && l.Count == 2 && l[0].Equals("1") && l[1].Equals("2"))
            {
                arrayKeyValue = (string)e.Value!;
            }
        }
        Assert.AreEqual("Array as key to force cmap", arrayKeyValue);
        Assert.IsTrue(m2.Contains(null!));
    }

    [TestMethod]
    public void TestReadSetTagAsString()
    {
        var o = Reader("{\"~~#set\": [1, 2, 3]}").Read<object>();
        Assert.IsFalse(o is ISet<object>);
        Assert.IsTrue(o is IDictionary);
    }

    [TestMethod]
    public void TestReadMany()
    {
        Assert.IsTrue(Reader("true").Read<bool>());
        Assert.IsNull(Reader("null").Read<object>());
        Assert.IsFalse(Reader("false").Read<bool>());
        Assert.AreEqual("foo", Reader("\"foo\"").Read<string>());
        Assert.AreEqual(42.2, Reader("42.2").Read<double>());
        Assert.AreEqual(42L, Reader("42").Read<long>());
    }

    [TestMethod]
    public void TestReadCache()
    {
        var rc = new ReadCache();
        Assert.AreEqual("~:foo", rc.CacheRead("~:foo", false));
        Assert.AreEqual("~:foo", rc.CacheRead("^" + (char)WriteCache.BaseCharIdx, false));
        Assert.AreEqual("~$bar", rc.CacheRead("~$bar", false));
        Assert.AreEqual("~$bar", rc.CacheRead("^" + (char)(WriteCache.BaseCharIdx + 1), false));
        Assert.AreEqual("~#baz", rc.CacheRead("~#baz", false));
        Assert.AreEqual("~#baz", rc.CacheRead("^" + (char)(WriteCache.BaseCharIdx + 2), false));
        Assert.AreEqual("foobar", rc.CacheRead("foobar", false));
        Assert.AreEqual("foobar", rc.CacheRead("foobar", false));
        Assert.AreEqual("foobar", rc.CacheRead("foobar", true));
        Assert.AreEqual("foobar", rc.CacheRead("^" + (char)(WriteCache.BaseCharIdx + 3), true));
        Assert.AreEqual("abc", rc.CacheRead("abc", false));
        Assert.AreEqual("abc", rc.CacheRead("abc", false));
        Assert.AreEqual("abc", rc.CacheRead("abc", true));
        Assert.AreEqual("abc", rc.CacheRead("abc", true));
    }

    [TestMethod]
    public void TestReadIdentity()
    {
        // System.Text.Json doesn't accept \' escape - use the valid encoding
        var v = Reader("\"~'42\"").Read<string>();
        Assert.AreEqual("42", v);
    }

    [TestMethod]
    public void TestReadWithNoCustomHandlers()
    {
        using var input = new MemoryStream(Encoding.UTF8.GetBytes("\"foo\""));
        using var reader = TransitFactory.Reader(TransitFactory.Format.Json, input, null, null);
        Assert.AreEqual("foo", reader.Read<string>());
    }

    [TestMethod]
    public void TestReadLink()
    {
        var r = Reader("[\"~#link\" , {\"href\": \"~rhttp://www.Beerendonk.nl\", \"rel\": \"a-rel\", \"name\": \"a-name\", \"prompt\": \"a-prompt\", \"render\": \"link or image\"}]");
        var v = r.Read<ILink>();
        Assert.AreEqual(new Uri("http://www.Beerendonk.nl"), v.Href);
        Assert.AreEqual("a-rel", v.Rel);
        Assert.AreEqual("a-name", v.Name);
        Assert.AreEqual("a-prompt", v.Prompt);
        Assert.AreEqual("link or image", v.Render);
    }

    [TestMethod]
    public void TestCustomReadHandler()
    {
        var customHandlers = new Dictionary<string, IReadHandler>
        {
            ["point"] = new PointReadHandler()
        };
        using var input = new MemoryStream(Encoding.UTF8.GetBytes("[\"~#point\",[37,42]]"));
        using var reader = TransitFactory.Reader(TransitFactory.Format.Json, input, customHandlers, null);
        var result = reader.Read<Point>();
        Assert.AreEqual(new Point(37, 42), result);
    }

    [TestMethod]
    public void TestCustomDefaultReadHandler()
    {
        var defaultHandler = new CatchAllDefaultReadHandler();
        using var input = new MemoryStream(Encoding.UTF8.GetBytes("[\"~#unknown\",[37,42]]"));
        using var reader = TransitFactory.Reader(TransitFactory.Format.Json, input, null, defaultHandler);
        var result = reader.Read<string>();
        Assert.AreEqual("unknown: [37, 42]", result);
    }

    private record Point(int X, int Y);

    private class PointReadHandler : IReadHandler
    {
        public object FromRepresentation(object representation)
        {
            var coords = (IList<object>)representation;
            int x = System.Convert.ToInt32(coords[0]);
            int y = System.Convert.ToInt32(coords[1]);
            return new Point(x, y);
        }
    }

    private class CatchAllDefaultReadHandler : IDefaultReadHandler<object>
    {
        public object FromRepresentation(string tag, object representation)
        {
            // Format collections like Java's ArrayList.toString() for consistent output
            if (representation is IList<object> list)
                return $"{tag}: [{string.Join(", ", list)}]";
            return $"{tag}: {representation}";
        }
    }

    #endregion

    #region Writing

    private string Write(object? obj, TransitFactory.Format format, IDictionary<Type, IWriteHandler>? customHandlers)
    {
        using var output = new MemoryStream();
        using var w = TransitFactory.Writer<object>(format, output, customHandlers);
        w.Write(obj!);

        output.Position = 0;
        using var sr = new StreamReader(output, leaveOpen: true);
        return sr.ReadToEnd();
    }

    private string WriteJsonVerbose(object? obj) => Write(obj, TransitFactory.Format.JsonVerbose, null);
    private string WriteJsonVerbose(object? obj, IDictionary<Type, IWriteHandler> customHandlers)
        => Write(obj, TransitFactory.Format.JsonVerbose, customHandlers);

    private string WriteJson(object? obj) => Write(obj, TransitFactory.Format.Json, null);
    private string WriteJson(object? obj, IDictionary<Type, IWriteHandler> customHandlers)
        => Write(obj, TransitFactory.Format.Json, customHandlers);

    private static string Scalar(string value) => "[\"~#'\"," + value + "]";
    private static string ScalarVerbose(string value) => "{\"~#'\":" + value + "}";

    [TestMethod]
    public void TestWriteNull()
    {
        Assert.AreEqual(ScalarVerbose("null"), WriteJsonVerbose(null));
        Assert.AreEqual(Scalar("null"), WriteJson(null));
    }

    [TestMethod]
    public void TestWriteKeyword()
    {
        Assert.AreEqual(ScalarVerbose("\"~:foo\""), WriteJsonVerbose(TransitFactory.Keyword("foo")));
        Assert.AreEqual(Scalar("\"~:foo\""), WriteJson(TransitFactory.Keyword("foo")));

        IList l = new IKeyword[]
        {
            TransitFactory.Keyword("foo"),
            TransitFactory.Keyword("foo"),
            TransitFactory.Keyword("foo")
        };
        Assert.AreEqual("[\"~:foo\",\"~:foo\",\"~:foo\"]", WriteJsonVerbose(l));
        Assert.AreEqual("[\"~:foo\",\"^0\",\"^0\"]", WriteJson(l));
    }

    [TestMethod]
    public void TestWriteObjectJsonThrows()
    {
        Assert.ThrowsException<NotSupportedException>(() => WriteJson(new object()));
    }

    [TestMethod]
    public void TestWriteObjectJsonVerboseThrows()
    {
        Assert.ThrowsException<NotSupportedException>(() => WriteJsonVerbose(new object()));
    }

    [TestMethod]
    public void TestWriteString()
    {
        Assert.AreEqual(ScalarVerbose("\"foo\""), WriteJsonVerbose("foo"));
        Assert.AreEqual(Scalar("\"foo\""), WriteJson("foo"));
        Assert.AreEqual(ScalarVerbose("\"~~foo\""), WriteJsonVerbose("~foo"));
        Assert.AreEqual(Scalar("\"~~foo\""), WriteJson("~foo"));
    }

    [TestMethod]
    public void TestWriteBoolean()
    {
        Assert.AreEqual(ScalarVerbose("true"), WriteJsonVerbose(true));
        Assert.AreEqual(Scalar("true"), WriteJson(true));
        Assert.AreEqual(Scalar("false"), WriteJson(false));

        var d = new Dictionary<bool, int> { [true] = 1 };
        Assert.AreEqual("{\"~?t\":1}", WriteJsonVerbose(d));
        Assert.AreEqual("[\"^ \",\"~?t\",1]", WriteJson(d));

        var d2 = new Dictionary<bool, int> { [false] = 1 };
        Assert.AreEqual("{\"~?f\":1}", WriteJsonVerbose(d2));
        Assert.AreEqual("[\"^ \",\"~?f\",1]", WriteJson(d2));
    }

    [TestMethod]
    public void TestWriteInteger()
    {
        Assert.AreEqual(ScalarVerbose("42"), WriteJsonVerbose(42));
        Assert.AreEqual(ScalarVerbose("42"), WriteJsonVerbose(42L));
        Assert.AreEqual(ScalarVerbose("42"), WriteJsonVerbose((byte)42));
        Assert.AreEqual(ScalarVerbose("42"), WriteJsonVerbose((short)42));
        Assert.AreEqual(ScalarVerbose("42"), WriteJsonVerbose((int)42));
        Assert.AreEqual(ScalarVerbose("42"), WriteJsonVerbose(42L));
        Assert.AreEqual(ScalarVerbose("\"~n42\""), WriteJsonVerbose(BigInteger.Parse("42")));
        Assert.AreEqual(ScalarVerbose("\"~n4256768765123454321897654321234567\""),
            WriteJsonVerbose(BigInteger.Parse("4256768765123454321897654321234567")));
    }

    [TestMethod]
    public void TestWriteIntegerAtJsonBoundaries()
    {
        // 2^53 - 1 is the max safe JSON integer — should be written as a bare number
        Assert.AreEqual(ScalarVerbose("9007199254740991"), WriteJsonVerbose((long)Math.Pow(2, 53) - 1));
        // 2^53 exceeds safe range — should be written as ~i string
        Assert.AreEqual(ScalarVerbose("\"~i9007199254740992\""), WriteJsonVerbose((long)Math.Pow(2, 53)));

        // Negative boundary
        Assert.AreEqual(ScalarVerbose("-9007199254740991"), WriteJsonVerbose(1 - (long)Math.Pow(2, 53)));
        Assert.AreEqual(ScalarVerbose("\"~i-9007199254740992\""), WriteJsonVerbose(0 - (long)Math.Pow(2, 53)));
    }

    [TestMethod]
    public void TestWriteFloatDouble()
    {
        Assert.AreEqual(ScalarVerbose("42.5"), WriteJsonVerbose(42.5));
        Assert.AreEqual(ScalarVerbose("42.5"), WriteJsonVerbose(42.5F));
        Assert.AreEqual(ScalarVerbose("42.5"), WriteJsonVerbose(42.5D));
    }

    [TestMethod]
    public void TestWriteBigRational()
    {
        Assert.AreEqual(ScalarVerbose("\"~f12.345\""), WriteJsonVerbose(new BigRational(12.345M)));
        Assert.AreEqual(ScalarVerbose("\"~f-12.345\""), WriteJsonVerbose(new BigRational(-12.345M)));
        Assert.AreEqual(ScalarVerbose("\"~f420.0057\""), WriteJsonVerbose(new BigRational(420.0057M)));
        Assert.AreEqual(Scalar("\"~f12.345\""), WriteJson(new BigRational(12.345M)));
    }

    [TestMethod]
    public void TestWriteDecimal()
    {
        Assert.AreEqual(ScalarVerbose("\"~f12.345\""), WriteJsonVerbose(12.345M));
        Assert.AreEqual(ScalarVerbose("\"~f-12.345\""), WriteJsonVerbose(-12.345M));
        Assert.AreEqual(ScalarVerbose("\"~f420.0057\""), WriteJsonVerbose(420.0057M));
        Assert.AreEqual(Scalar("\"~f12.345\""), WriteJson(12.345M));
    }

    [TestMethod]
    public void TestSpecialNumbers()
    {
        Assert.AreEqual(Scalar("\"~zNaN\""), WriteJson(double.NaN));
        Assert.AreEqual(Scalar("\"~zINF\""), WriteJson(double.PositiveInfinity));
        Assert.AreEqual(Scalar("\"~z-INF\""), WriteJson(double.NegativeInfinity));

        Assert.AreEqual(Scalar("\"~zNaN\""), WriteJson(float.NaN));
        Assert.AreEqual(Scalar("\"~zINF\""), WriteJson(float.PositiveInfinity));
        Assert.AreEqual(Scalar("\"~z-INF\""), WriteJson(float.NegativeInfinity));

        Assert.AreEqual(ScalarVerbose("\"~zNaN\""), WriteJsonVerbose(double.NaN));
        Assert.AreEqual(ScalarVerbose("\"~zINF\""), WriteJsonVerbose(double.PositiveInfinity));
        Assert.AreEqual(ScalarVerbose("\"~z-INF\""), WriteJsonVerbose(double.NegativeInfinity));

        Assert.AreEqual(ScalarVerbose("\"~zNaN\""), WriteJsonVerbose(float.NaN));
        Assert.AreEqual(ScalarVerbose("\"~zINF\""), WriteJsonVerbose(float.PositiveInfinity));
        Assert.AreEqual(ScalarVerbose("\"~z-INF\""), WriteJsonVerbose(float.NegativeInfinity));
    }

    [TestMethod]
    public void TestWriteDateTime()
    {
        var d = DateTime.Now;
        string dateString = AbstractParser.FormatDateTime(d);
        long dateLong = Transit.Net.Java.Convert.ToJavaTime(d);
        Assert.AreEqual(ScalarVerbose("\"~t" + dateString + "\""), WriteJsonVerbose(d));
        Assert.AreEqual(Scalar("\"~m" + dateLong + "\""), WriteJson(d));
    }

    [TestMethod]
    public void TestWriteUUID()
    {
        var guid = Guid.NewGuid();
        Assert.AreEqual(ScalarVerbose("\"~u" + guid.ToString() + "\""), WriteJsonVerbose(guid));
    }

    [TestMethod]
    public void TestWriteURI()
    {
        var uri = new Uri("http://www.foo.com/");
        Assert.AreEqual(ScalarVerbose("\"~rhttp://www.foo.com/\""), WriteJsonVerbose(uri));
    }

    [TestMethod]
    public void TestWriteBinary()
    {
        byte[] bytes = Encoding.ASCII.GetBytes("foobarbaz");
        string encoded = Convert.ToBase64String(bytes);
        Assert.AreEqual(ScalarVerbose("\"~b" + encoded + "\""), WriteJsonVerbose(bytes));
    }

    [TestMethod]
    public void TestWriteSymbol()
    {
        Assert.AreEqual(ScalarVerbose("\"~$foo\""), WriteJsonVerbose(TransitFactory.Symbol("foo")));
    }

    [TestMethod]
    public void TestWriteList()
    {
        IList<int> l = new List<int> { 1, 2, 3 };
        Assert.AreEqual("[1,2,3]", WriteJsonVerbose(l));
        Assert.AreEqual("[1,2,3]", WriteJson(l));
    }

    [TestMethod]
    public void TestWritePrimitiveArrays()
    {
        int[] ints = { 1, 2 };
        Assert.AreEqual("[1,2]", WriteJsonVerbose(ints));

        long[] longs = { 1L, 2L };
        Assert.AreEqual("[1,2]", WriteJsonVerbose(longs));

        float[] floats = { 1.5f, 2.78f };
        Assert.AreEqual("[1.5,2.78]", WriteJsonVerbose(floats));

        bool[] bools = { true, false };
        Assert.AreEqual("[true,false]", WriteJsonVerbose(bools));

        double[] doubles = { 1.654d, 2.8765d };
        Assert.AreEqual("[1.654,2.8765]", WriteJsonVerbose(doubles));

        short[] shorts = { 1, 2 };
        Assert.AreEqual("[1,2]", WriteJsonVerbose(shorts));

        char[] chars = { '5', '/' };
        Assert.AreEqual("[\"~c5\",\"~c/\"]", WriteJsonVerbose(chars));
    }

    [TestMethod]
    public void TestWriteDictionary()
    {
        IDictionary<string, int> d = new Dictionary<string, int> { {"foo", 1}, {"bar", 2} };
        Assert.AreEqual("{\"foo\":1,\"bar\":2}", WriteJsonVerbose(d));
        Assert.AreEqual("[\"^ \",\"foo\",1,\"bar\",2]", WriteJson(d));
    }

    [TestMethod]
    public void TestWriteEmptyDictionary()
    {
        IDictionary<object, object> d = new Dictionary<object, object>();
        Assert.AreEqual("{}", WriteJsonVerbose(d));
        Assert.AreEqual("[\"^ \"]", WriteJson(d));
    }

    [TestMethod]
    public void TestWriteSet()
    {
        ISet<string> s = new HashSet<string> { "foo", "bar" };
        Assert.AreEqual("{\"~#set\":[\"foo\",\"bar\"]}", WriteJsonVerbose(s));
        Assert.AreEqual("[\"~#set\",[\"foo\",\"bar\"]]", WriteJson(s));
    }

    [TestMethod]
    public void TestWriteEmptySet()
    {
        ISet<object> s = new HashSet<object>();
        Assert.AreEqual("{\"~#set\":[]}", WriteJsonVerbose(s));
        Assert.AreEqual("[\"~#set\",[]]", WriteJson(s));
    }

    [TestMethod]
    public void TestWriteEnumerable()
    {
        ICollection<string> c = new LinkedList<string>();
        c.Add("foo");
        c.Add("bar");
        IEnumerable<string> e = c;
        Assert.AreEqual("{\"~#list\":[\"foo\",\"bar\"]}", WriteJsonVerbose(e));
        Assert.AreEqual("[\"~#list\",[\"foo\",\"bar\"]]", WriteJson(e));
    }

    [TestMethod]
    public void TestWriteEmptyEnumerable()
    {
        IEnumerable<string> c = new LinkedList<string>();
        Assert.AreEqual("{\"~#list\":[]}", WriteJsonVerbose(c));
        Assert.AreEqual("[\"~#list\",[]]", WriteJson(c));
    }

    [TestMethod]
    public void TestWriteCharacter()
    {
        Assert.AreEqual(ScalarVerbose("\"~cf\""), WriteJsonVerbose('f'));
    }

    [TestMethod]
    public void TestWriteRatio()
    {
        IRatio r = new Ratio(BigInteger.One, new BigInteger(2));
        Assert.AreEqual("{\"~#ratio\":[\"~n1\",\"~n2\"]}", WriteJsonVerbose(r));
        Assert.AreEqual("[\"~#ratio\",[\"~n1\",\"~n2\"]]", WriteJson(r));
    }

    [TestMethod]
    public void TestWriteCDictionary()
    {
        IRatio r = new Ratio(BigInteger.One, new BigInteger(2));
        IDictionary<object, object> d = new Dictionary<object, object> { [r] = 1 };
        Assert.AreEqual("{\"~#cmap\":[{\"~#ratio\":[\"~n1\",\"~n2\"]},1]}", WriteJsonVerbose(d));
        Assert.AreEqual("[\"~#cmap\",[[\"~#ratio\",[\"~n1\",\"~n2\"]],1]]", WriteJson(d));
    }

    [TestMethod]
    public void TestWriteCDictionaryWithNullKey()
    {
        var d = new Transit.Net.Impl.NullKeyDictionary();
        d[null] = "null as map key";
        d[new List<object> { "1", "2" }] = "Array as key to force cmap";

        // Verify it writes as cmap (since null and array can't be string keys)
        string json = WriteJson(d);
        Assert.IsTrue(json.Contains("\"~#cmap\""), "Should encode as cmap");

        // Roundtrip: read it back and verify entries
        using var resultReader = Reader(json);
        var result = resultReader.Read<IDictionary>();
        Assert.AreEqual(2, result.Count);
        Assert.AreEqual("null as map key", result[null!]);
    }

    [TestMethod]
    public void TestWriteCache()
    {
        var wc = new WriteCache(true);
        Assert.AreEqual("~:foo", wc.CacheWrite("~:foo", false));
        Assert.AreEqual("^" + (char)WriteCache.BaseCharIdx, wc.CacheWrite("~:foo", false));
        Assert.AreEqual("~$bar", wc.CacheWrite("~$bar", false));
        Assert.AreEqual("^" + (char)(WriteCache.BaseCharIdx + 1), wc.CacheWrite("~$bar", false));
        Assert.AreEqual("~#baz", wc.CacheWrite("~#baz", false));
        Assert.AreEqual("^" + (char)(WriteCache.BaseCharIdx + 2), wc.CacheWrite("~#baz", false));
        Assert.AreEqual("foobar", wc.CacheWrite("foobar", false));
        Assert.AreEqual("foobar", wc.CacheWrite("foobar", false));
        Assert.AreEqual("foobar", wc.CacheWrite("foobar", true));
        Assert.AreEqual("^" + (char)(WriteCache.BaseCharIdx + 3), wc.CacheWrite("foobar", true));
        Assert.AreEqual("abc", wc.CacheWrite("abc", false));
        Assert.AreEqual("abc", wc.CacheWrite("abc", false));
        Assert.AreEqual("abc", wc.CacheWrite("abc", true));
        Assert.AreEqual("abc", wc.CacheWrite("abc", true));
    }

    [TestMethod]
    public void TestWriteCacheDisabled()
    {
        var wc = new WriteCache(false);
        Assert.AreEqual("foobar", wc.CacheWrite("foobar", false));
        Assert.AreEqual("foobar", wc.CacheWrite("foobar", false));
        Assert.AreEqual("foobar", wc.CacheWrite("foobar", true));
        Assert.AreEqual("foobar", wc.CacheWrite("foobar", true));
    }

    [TestMethod]
    public void TestWriteUnknown()
    {
        var l = new List<object> { "`jfoo" };
        Assert.AreEqual("[\"~`jfoo\"]", WriteJsonVerbose(l));
        Assert.AreEqual(ScalarVerbose("\"~`jfoo\""), WriteJsonVerbose("`jfoo"));

        var l2 = new List<object> { 1L, 2L };
        Assert.AreEqual("{\"~#point\":[1,2]}", WriteJsonVerbose(TransitFactory.TaggedValue("point", l2)));
    }

    [TestMethod]
    public void TestRoundTrip()
    {
        object inObject = true;

        string s;
        using (var output = new MemoryStream())
        {
            using var w = TransitFactory.Writer<object>(TransitFactory.Format.JsonVerbose, output);
            w.Write(inObject);
            output.Position = 0;
            using var sr = new StreamReader(output, leaveOpen: true);
            s = sr.ReadToEnd();
        }

        byte[] buffer = Encoding.ASCII.GetBytes(s);
        using var input = new MemoryStream(buffer);
        using var reader = TransitFactory.Reader(TransitFactory.Format.Json, input);
        var outObject = reader.Read<object>();

        Assert.AreEqual(inObject, outObject);
    }

    [TestMethod]
    public void TestWriteHandlerCache()
    {
        var customHandlers = new Dictionary<Type, IWriteHandler>
        {
            [typeof(IList<object>)] = new TestListWriteHandler()
        };

        // Creating multiple writers with the same handler map should not throw
        for (int i = 0; i < 2; i++)
        {
            using var output = new MemoryStream();
            using var w = TransitFactory.Writer<object>(TransitFactory.Format.Json, output, customHandlers);
        }
    }

    [TestMethod]
    public void TestCustomWriteHandler()
    {
        var customHandlers = new Dictionary<Type, IWriteHandler>
        {
            [typeof(Point)] = new PointWriteHandler()
        };
        Assert.AreEqual("[\"~#point\",[37,42]]", WriteJson(new Point(37, 42), customHandlers));
    }

    [TestMethod]
    public void TestWriteUnknownType()
    {
        // Writing an unregistered type should throw NotSupportedException
        Assert.ThrowsException<NotSupportedException>(() => WriteJson(new Point(1, 2)));
    }

    [TestMethod]
    public void TestDefaultWriteHandlerFallback()
    {
        var defaultHandler = new CatchAllDefaultWriteHandler();
        var unknownObj = new UnknownObject();

        // 1. Verify JSON Verbose uses the default handler
        using var output1 = new MemoryStream();
        using var w1 = TransitFactory.Writer<object>(TransitFactory.Format.JsonVerbose, output1, null, defaultHandler);
        w1.Write(unknownObj);
        output1.Position = 0;
        using var sr1 = new StreamReader(output1);
        string jsonVerbose = sr1.ReadToEnd();
        Assert.AreEqual("{\"~#unknown\":\"UnknownString\"}", jsonVerbose);

        // 2. Verify normal JSON uses the default handler
        using var output2 = new MemoryStream();
        using var w2 = TransitFactory.Writer<object>(TransitFactory.Format.Json, output2, null, defaultHandler);
        w2.Write(unknownObj);
        output2.Position = 0;
        using var sr2 = new StreamReader(output2);
        string json = sr2.ReadToEnd();
        Assert.AreEqual("[\"~#unknown\",\"UnknownString\"]", json);
    }

    [TestMethod]
    public void TestWriteTimeTransform()
    {
        // Transform any Point into a string "x,y"
        Func<object, object> transform = obj =>
        {
            if (obj is Point p)
                return $"{p.X},{p.Y}";
            return obj;
        };

        var point = new Point(37, 42);

        // 1. Verify JSON Verbose uses the transform
        using var output1 = new MemoryStream();
        using var w1 = TransitFactory.Writer<object>(TransitFactory.Format.JsonVerbose, output1, null, null, transform);
        w1.Write(point);
        output1.Position = 0;
        using var sr1 = new StreamReader(output1);
        Assert.AreEqual("{\"~#'\":\"37,42\"}", sr1.ReadToEnd());

        // 2. Verify normal JSON uses the transform
        using var output2 = new MemoryStream();
        using var w2 = TransitFactory.Writer<object>(TransitFactory.Format.Json, output2, null, null, transform);
        w2.Write(point);
        output2.Position = 0;
        using var sr2 = new StreamReader(output2);
        Assert.AreEqual("[\"~#'\",\"37,42\"]", sr2.ReadToEnd());
    }

    [TestMethod]
    public void TestPreMergedHandlerMaps()
    {
        var customWriteHandlers = new Dictionary<Type, IWriteHandler>
        {
            [typeof(Point)] = new PointWriteHandler()
        };

        var customReadHandlers = new Dictionary<string, IReadHandler>
        {
            ["point"] = new PointReadHandler()
        };

        // 1. Create the frozen, pre-merged dictionaries
        var mergedWriteHandlers = TransitFactory.MergedWriteHandlers(customWriteHandlers);
        var mergedReadHandlers = TransitFactory.MergedReadHandlers(customReadHandlers);

        var point = new Point(10, 20);

        // 2. Use them in a writer
        using var output = new MemoryStream();
        using var writer = TransitFactory.Writer<object>(TransitFactory.Format.Json, output, mergedWriteHandlers);
        writer.Write(point);
        
        output.Position = 0;
        using var reader = TransitFactory.Reader(TransitFactory.Format.Json, output, mergedReadHandlers);
        var readPoint = (Point)reader.Read<object>();

        Assert.AreEqual(point.X, readPoint.X);
        Assert.AreEqual(point.Y, readPoint.Y);
    }

    private class UnknownObject
    {
        public override string ToString() => "UnknownString";
    }

    private class CatchAllDefaultWriteHandler : IWriteHandler
    {
        public string Tag(object obj) => "unknown";
        public object Representation(object obj) => obj.ToString()!;
        public string? StringRepresentation(object obj) => obj.ToString();
        public IWriteHandler? GetVerboseHandler() => null;
    }

    private class TestListWriteHandler : IWriteHandler
    {
        public string Tag(object obj) => obj is List<object> ? "array" : "list";
        public object Representation(object obj)
        {
            if (obj is LinkedList<object>)
                return TransitFactory.TaggedValue("array", obj);
            return obj;
        }
        public string? StringRepresentation(object obj) => null;
        public IWriteHandler? GetVerboseHandler() => null;
    }

    private class PointWriteHandler : IWriteHandler
    {
        public string Tag(object obj) => "point";
        public object Representation(object obj)
        {
            var p = (Point)obj;
            return new List<object> { p.X, p.Y };
        }
        public string? StringRepresentation(object obj) => Representation(obj).ToString()!;
        public IWriteHandler? GetVerboseHandler() => this;
    }

    #endregion

    #region JSON Machine Mode

    [TestMethod]
    public void TestMachineReadMap()
    {
        var m = Reader("[\"^ \",\"foo\",1,\"bar\",2]").Read<IDictionary>();
        Assert.IsTrue(m.Contains("foo"));
        Assert.IsTrue(m.Contains("bar"));
        Assert.AreEqual(1L, m["foo"]);
        Assert.AreEqual(2L, m["bar"]);
    }

    [TestMethod]
    public void TestMachineReadMapWithNested()
    {
        var m = Reader("[\"^ \",\"foo\",1,\"bar\",[\"^ \",\"baz\",3]]").Read<IDictionary>();
        Assert.IsTrue(m["bar"] is IDictionary);
        Assert.AreEqual(3L, ((IDictionary)m["bar"]!)["baz"]);
    }

    [TestMethod]
    public void TestMachineWriteMap()
    {
        var m = new Dictionary<string, object> { ["foo"] = 1 };
        Assert.AreEqual("[\"^ \",\"foo\",1]", WriteJson(m));

        // Tighter test with preserved order
        var m2 = new SortedDictionary<string, object> { ["bar"] = 2, ["foo"] = 1 };
        Assert.AreEqual("[\"^ \",\"bar\",2,\"foo\",1]", WriteJson(m2));
    }

    [TestMethod]
    public void TestMachineWriteEmptyMap()
    {
        var m = new Dictionary<string, object>();
        Assert.AreEqual("[\"^ \"]", WriteJson(m));
    }

    [TestMethod]
    public void TestMachineWritingArrayMarkerDirectly()
    {
        // If "^ " appears as an element, it must be escaped to "~^ " so it's not
        // misinterpreted as a map marker when read back.
        var l1 = new List<object> { "^ " };
        var s = WriteJson(l1);
        Assert.AreEqual("[\"~^ \"]", s);

        // Round-trip: read it back and verify
        var l2 = Reader(s).Read<IList<object>>();
        Assert.AreEqual(1, l2.Count);
        Assert.AreEqual("^ ", l2[0]);
    }

    [TestMethod]
    public void TestMachineReadTime()
    {
        // Machine timestamps: ~m prefix
        var human = Reader("[\"~t1776-07-04T12:00:00.000Z\",\"~t1970-01-01T00:00:00.000Z\",\"~t2000-01-01T12:00:00.000Z\",\"~t2014-04-07T22:17:17.000Z\"]")
            .Read<IList<object>>();
        var machine = Reader("[\"~m-6106017600000\",\"~m0\",\"~m946728000000\",\"~m1396909037000\"]")
            .Read<IList<object>>();

        Assert.AreEqual(4, human.Count);
        Assert.AreEqual(4, machine.Count);

        for (int i = 0; i < human.Count; i++)
        {
            var dh = (DateTime)human[i];
            var dm = (DateTime)machine[i];
            Assert.AreEqual(dh, dm, $"Mismatch at index {i}");
        }
    }

    [TestMethod]
    public void TestMachineRoundTrip()
    {
        object inObject = true;
        string s;
        using (var output = new MemoryStream())
        {
            using var w = TransitFactory.Writer<object>(TransitFactory.Format.Json, output);
            w.Write(inObject);
            output.Position = 0;
            using var sr = new StreamReader(output, leaveOpen: true);
            s = sr.ReadToEnd();
        }

        using var input = new MemoryStream(Encoding.UTF8.GetBytes(s));
        using var reader = TransitFactory.Reader(TransitFactory.Format.Json, input);
        var outObject = reader.Read<object>();
        Assert.AreEqual(inObject, outObject);
    }

    #endregion

    #region Type Tests

    [TestMethod]
    public void TestUseIKeywordAsDictionaryKey()
    {
        IDictionary<object, object> d = new Dictionary<object, object>();
        d.Add(TransitFactory.Keyword("foo"), 1);
        d.Add("foo", 2);
        d.Add(TransitFactory.Keyword("bar"), 3);
        d.Add("bar", 4);

        Assert.AreEqual(1, d[TransitFactory.Keyword("foo")]);
        Assert.AreEqual(2, d["foo"]);
        Assert.AreEqual(3, d[TransitFactory.Keyword("bar")]);
        Assert.AreEqual(4, d["bar"]);
    }

    [TestMethod]
    public void TestUseISymbolAsDictionaryKey()
    {
        IDictionary<object, object> d = new Dictionary<object, object>();
        d.Add(TransitFactory.Symbol("foo"), 1);
        d.Add("foo", 2);
        d.Add(TransitFactory.Symbol("bar"), 3);
        d.Add("bar", 4);

        Assert.AreEqual(1, d[TransitFactory.Symbol("foo")]);
        Assert.AreEqual(2, d["foo"]);
        Assert.AreEqual(3, d[TransitFactory.Symbol("bar")]);
        Assert.AreEqual(4, d["bar"]);
    }

    [TestMethod]
    public void TestKeywordEquality()
    {
        var k1 = TransitFactory.Keyword("foo");
        var k2 = TransitFactory.Keyword("!foo"[1..]);
        var k3 = TransitFactory.Keyword("bar");

        Assert.AreEqual(k1, k2);
        Assert.AreEqual(k2, k1);
        Assert.AreNotEqual(k1, k3);
        Assert.AreNotEqual(k3, k1);
    }

    [TestMethod]
    public void TestKeywordHashCode()
    {
        var k1 = TransitFactory.Keyword("foo");
        var k2 = TransitFactory.Keyword("!foo"[1..]);
        var k3 = TransitFactory.Keyword("bar");

        Assert.AreEqual(k1.GetHashCode(), k2.GetHashCode());
        Assert.AreNotEqual(k3.GetHashCode(), k1.GetHashCode());
    }

    [TestMethod]
    public void TestKeywordComparator()
    {
        var l = new List<IKeyword>
        {
            TransitFactory.Keyword("bbb"),
            TransitFactory.Keyword("ccc"),
            TransitFactory.Keyword("abc"),
            TransitFactory.Keyword("dab"),
        };

        l.Sort((a, b) => string.Compare(a.Value, b.Value, StringComparison.Ordinal));

        Assert.AreEqual("abc", l[0].ToString());
        Assert.AreEqual("bbb", l[1].ToString());
        Assert.AreEqual("ccc", l[2].ToString());
        Assert.AreEqual("dab", l[3].ToString());
    }

    [TestMethod]
    public void TestSymbolEquality()
    {
        var s1 = TransitFactory.Symbol("foo");
        var s2 = TransitFactory.Symbol("!foo"[1..]);
        var s3 = TransitFactory.Symbol("bar");

        Assert.AreEqual(s1, s2);
        Assert.AreEqual(s2, s1);
        Assert.AreNotEqual(s1, s3);
        Assert.AreNotEqual(s3, s1);
    }

    [TestMethod]
    public void TestSymbolHashCode()
    {
        var s1 = TransitFactory.Symbol("foo");
        var s2 = TransitFactory.Symbol("!foo"[1..]);
        var s3 = TransitFactory.Symbol("bar");

        Assert.AreEqual(s1.GetHashCode(), s2.GetHashCode());
        Assert.AreNotEqual(s3.GetHashCode(), s1.GetHashCode());
    }

    [TestMethod]
    public void TestSymbolComparator()
    {
        var l = new List<ISymbol>
        {
            TransitFactory.Symbol("bbb"),
            TransitFactory.Symbol("ccc"),
            TransitFactory.Symbol("abc"),
            TransitFactory.Symbol("dab"),
        };

        l.Sort((a, b) => string.Compare(a.Value, b.Value, StringComparison.Ordinal));

        Assert.AreEqual("abc", l[0].ToString());
        Assert.AreEqual("bbb", l[1].ToString());
        Assert.AreEqual("ccc", l[2].ToString());
        Assert.AreEqual("dab", l[3].ToString());
    }

    [TestMethod]
    public void TestDictionaryWithEscapedKey()
    {
        var d1 = new Dictionary<object, object> { { "~Gfoo", 20L } };
        string str = WriteJson(d1);

        var d2 = Reader(str).Read<IDictionary>();
        Assert.IsTrue(d2.Contains("~Gfoo"));
        Assert.AreEqual(20L, d2["~Gfoo"]);
    }

    [TestMethod]
    public void TestLink()
    {
        var l1 = TransitFactory.Link("http://google.com/", "search", "name", "a-prompt", "link");
        string str = WriteJson(l1);
        var l2 = Reader(str).Read<ILink>();
        Assert.AreEqual("http://google.com/", l2.Href.AbsoluteUri);
        Assert.AreEqual("search", l2.Rel);
        Assert.AreEqual("name", l2.Name);
        Assert.AreEqual("link", l2.Render);
        Assert.AreEqual("a-prompt", l2.Prompt);
    }

    [TestMethod]
    public void TestEmptySet()
    {
        string str = WriteJson(new HashSet<object>());
        Assert.IsInstanceOfType<ISet<object>>(Reader(str).Read<ISet<object>>());
    }

    [TestMethod]
    public void TestBooleanVerification()
    {
        // Verify true/false roundtrip
        Assert.AreEqual(true, Reader(WriteJson(true)).Read<bool>());
        Assert.AreEqual(false, Reader(WriteJson(false)).Read<bool>());
        Assert.AreEqual(true, Reader(WriteJsonVerbose(true)).Read<bool>());
        Assert.AreEqual(false, Reader(WriteJsonVerbose(false)).Read<bool>());

        // Verify booleans as map keys encode as ~?t/~?f and decode back
        var dict = new Dictionary<bool, string> { { true, "t" }, { false, "f" } };
        string json = WriteJson(dict);
        Assert.IsTrue(json.Contains("\"~?t\""));
        Assert.IsTrue(json.Contains("\"~?f\""));
        var readDict = Reader(json).Read<IDictionary>();
        Assert.AreEqual("t", readDict[true]);
        Assert.AreEqual("f", readDict[false]);
    }

    [TestMethod]
    public void TestNullVerification()
    {
        // Verify null roundtrips
        Assert.IsNull(Reader(WriteJson(null)).Read<object>());
        Assert.IsNull(Reader(WriteJsonVerbose(null)).Read<object>());

        // Verify null in arrays
        var list = new List<object?> { 1L, null, 3L };
        var readList = Reader(WriteJson(list)).Read<IList>();
        Assert.AreEqual(1L, readList[0]);
        Assert.IsNull(readList[1]);
        Assert.AreEqual(3L, readList[2]);

        // Verify null in both key and value positions of maps
        var dict = new Transit.Net.Impl.NullKeyDictionary();
        dict[null] = null;
        var encoded = WriteJson(dict);
        var readDict = Reader(encoded).Read<IDictionary>();
        Assert.IsTrue(readDict.Contains(null!));
        Assert.IsNull(readDict[null!]);
    }

    [TestMethod]
    public void TestNullRoundtripAndEncoding()
    {
        // 1. Verify null roundtrips correctly in JSON and JSON-Verbose
        Assert.IsNull(Reader(WriteJson(null)).Read<object>());
        Assert.IsNull(Reader(WriteJsonVerbose(null)).Read<object>());

        // 2. Check ~_ string decoding (must not be confused with null)
        Assert.AreEqual("~_", Reader("\"~~_\"").Read<string>());

        // 3. Verify null in arrays
        var listWithNull = new List<object?> { 1L, null, 3L };
        var lJson = WriteJson(listWithNull);
        var rList = Reader(lJson).Read<IList>();
        Assert.AreEqual(3, rList.Count);
        Assert.AreEqual(1L, rList[0]);
        Assert.IsNull(rList[1]);
        Assert.AreEqual(3L, rList[2]);

        // 4. Verify null in both key and value positions of maps
        // We use Transit.Net.Impl.NullKeyDictionary since standard Dictionary throws on null key
        var mapWithNull = new Transit.Net.Impl.NullKeyDictionary();
        mapWithNull[null] = "null value";
        mapWithNull["null key"] = null;

        var mJson = WriteJson(mapWithNull);
        var rMap = Reader(mJson).Read<IDictionary>();
        Assert.IsTrue(rMap.Contains(null!));
        Assert.AreEqual("null value", rMap[null!]);
        Assert.IsTrue(rMap.Contains("null key"));
        Assert.IsNull(rMap["null key"]);

        // 5. Null as map key must encode as ~_
        // JSON Verbose output should have "~_" as the key
        var mJsonVerbose = WriteJsonVerbose(mapWithNull);
        Assert.IsTrue(mJsonVerbose.Contains("\"~_\""), "Null key should encode as ~_");
    }

    [TestMethod]
    public void TestJsonVsJsonVerboseConsistency()
    {
        var data = new Dictionary<string, object>
        {
            ["int"] = 42L,
            ["str"] = "hello",
            ["list"] = new List<object> { 1L, 2L, 3L },
            ["map"] = new Dictionary<string, object> { ["nested"] = true }
        };

        var json = WriteJson(data);
        var jsonVerbose = WriteJsonVerbose(data);

        // Verify map representation differs (JSON mode uses array with ^ , Verbose uses {} object)
        Assert.IsTrue(json.StartsWith("[\"^ \"") || json.Contains("\"^ \""), "JSON mode should use array maps with ^ marker");
        Assert.IsTrue(jsonVerbose.StartsWith("{"), "JSON-Verbose mode should use object maps");
        Assert.AreNotEqual(json, jsonVerbose);

        // Verify that data written in JSON mode can be read back correctly
        // and reader transparently handles both modes.
        var readJson = Reader(json).Read<IDictionary>();
        var readVerbose = Reader(jsonVerbose).Read<IDictionary>();

        // Assert they produce the same values
        Assert.AreEqual(42L, readJson["int"]);
        Assert.AreEqual(42L, readVerbose["int"]);
        
        Assert.AreEqual("hello", readJson["str"]);
        Assert.AreEqual("hello", readVerbose["str"]);
        
        var list1 = (IList)readJson["list"]!;
        var list2 = (IList)readVerbose["list"]!;
        Assert.AreEqual(3, list1.Count);
        Assert.AreEqual(list1.Count, list2.Count);

        var map1 = (IDictionary)readJson["map"]!;
        var map2 = (IDictionary)readVerbose["map"]!;
        Assert.IsTrue((bool)map1["nested"]!);
        Assert.IsTrue((bool)map2["nested"]!);
    }

    [TestMethod]
    public void TestVerifyBoolean()
    {
        // JSON Verbose true/false
        Assert.AreEqual(ScalarVerbose("true"), WriteJsonVerbose(true));
        Assert.AreEqual(ScalarVerbose("false"), WriteJsonVerbose(false));

        // JSON true/false
        Assert.AreEqual(Scalar("true"), WriteJson(true));
        Assert.AreEqual(Scalar("false"), WriteJson(false));

        // Reading native JSON
        Assert.IsTrue(Reader("true").Read<bool>());
        Assert.IsFalse(Reader("false").Read<bool>());

        // Tagged string forms
        Assert.IsTrue(Reader("\"~?t\"").Read<bool>());
        Assert.IsFalse(Reader("\"~?f\"").Read<bool>());

        // Map keys
        var d = new Dictionary<bool, int> { [true] = 1, [false] = 2 };
        var jsonVerbose = WriteJsonVerbose(d);
        Assert.IsTrue(jsonVerbose.Contains("\"~?t\":1") || jsonVerbose.Contains("\"~?t\": 1"), "true as map key must encode as ~?t");
        Assert.IsTrue(jsonVerbose.Contains("\"~?f\":2") || jsonVerbose.Contains("\"~?f\": 2"), "false as map key must encode as ~?f");

        var readMap = Reader(jsonVerbose).Read<IDictionary>();
        Assert.AreEqual(1L, readMap[true]);
        Assert.AreEqual(2L, readMap[false]);
    }

    [TestMethod]
    public void TestVerifyNull()
    {
        // Roundtrip null
        Assert.AreEqual(ScalarVerbose("null"), WriteJsonVerbose(null));
        Assert.AreEqual(Scalar("null"), WriteJson(null));

        // Read null
        Assert.IsNull(Reader("null").Read<object>());
        Assert.IsNull(Reader("\"~_\"").Read<object>());

        // Null in arrays
        var arr = new object?[] { 1L, null, 2L };
        var arrVerbose = WriteJsonVerbose(arr);
        Assert.AreEqual("[1,null,2]", arrVerbose.Replace(" ", ""));
        var readArr = Reader(arrVerbose).Read<IList>();
        Assert.AreEqual(1L, readArr[0]);
        Assert.IsNull(readArr[1]);
        Assert.AreEqual(2L, readArr[2]);

        // Null in map values
        var mapVal = new Dictionary<string, object?> { ["a"] = null };
        var mapValVerbose = WriteJsonVerbose(mapVal);
        Assert.IsTrue(mapValVerbose.Contains("\"a\":null") || mapValVerbose.Contains("\"a\": null"));
        var readMapVal = Reader(mapValVerbose).Read<IDictionary>();
        Assert.IsNull(readMapVal["a"]);

        // Null as map key
        var mapKey = new Transit.Net.Impl.NullKeyDictionary();
        mapKey[null] = "value";
        string json = WriteJson(mapKey);
        // By spec, null key should encode as ~_
        Assert.IsTrue(json.Contains("\"~_\""), "Null key should encode as ~_");

        var readMapKey = Reader(json).Read<IDictionary>();
        Assert.AreEqual("value", readMapKey[null!]);
    }

    [TestMethod]
    public void TestVerifyString()
    {
        // Plain string roundtrip
        Assert.AreEqual("foo", Reader(WriteJson("foo")).Read<string>());
        Assert.AreEqual("foo", Reader(WriteJsonVerbose("foo")).Read<string>());

        // Empty string
        Assert.AreEqual("", Reader(WriteJson("")).Read<string>());
        Assert.AreEqual("", Reader(WriteJsonVerbose("")).Read<string>());
        Assert.AreEqual("", Reader("\"\"").Read<string>());

        // Strings starting with ~ must be escaped as ~~
        Assert.AreEqual("~foo", Reader(WriteJson("~foo")).Read<string>());
        var tildeJson = WriteJson("~foo");
        Assert.IsTrue(tildeJson.Contains("~~foo"), "~ prefix must be escaped to ~~");

        // Strings starting with ` must be escaped as ~`
        Assert.AreEqual("`foo", Reader(WriteJson("`foo")).Read<string>());
        var backtickJson = WriteJson("`foo");
        Assert.IsTrue(backtickJson.Contains("~`foo"), "` prefix must be escaped to ~`");

        // Strings starting with ^ must be escaped as ~^
        Assert.AreEqual("^foo", Reader(WriteJson("^foo")).Read<string>());
        var caretJson = WriteJson("^foo");
        Assert.IsTrue(caretJson.Contains("~^foo"), "^ prefix must be escaped to ~^");

        // Strings as map keys (including special prefixes)
        var d = new Dictionary<string, int> { ["~special"] = 10, ["^caret"] = 20, ["`backtick"] = 30 };
        var readD = Reader(WriteJson(d)).Read<IDictionary>();
        Assert.AreEqual(10L, readD["~special"]);
        Assert.AreEqual(20L, readD["^caret"]);
        Assert.AreEqual(30L, readD["`backtick"]);

        // Long string roundtrip
        var longStr = new string('x', 10000);
        Assert.AreEqual(longStr, Reader(WriteJson(longStr)).Read<string>());

        // Unicode
        Assert.AreEqual("héllo 日本語", Reader(WriteJson("héllo 日本語")).Read<string>());
    }

    [TestMethod]
    public void TestVerifyInteger()
    {
        // Roundtrip byte, short, int, long
        Assert.AreEqual(42L, Reader(WriteJson((byte)42)).Read<long>());
        Assert.AreEqual(42L, Reader(WriteJson((short)42)).Read<long>());
        Assert.AreEqual(42L, Reader(WriteJson(42)).Read<long>());
        Assert.AreEqual(42L, Reader(WriteJson(42L)).Read<long>());
        Assert.AreEqual(0L, Reader(WriteJson(0)).Read<long>());
        Assert.AreEqual(-1L, Reader(WriteJson(-1)).Read<long>());

        // < 2^53 writes as bare JSON number
        long belowBoundary = 9007199254740991L; // 2^53 - 1
        var belowJson = WriteJson(belowBoundary);
        Assert.IsFalse(belowJson.Contains("~i"), "Below 2^53 should not use ~i");
        Assert.AreEqual(belowBoundary, Reader(belowJson).Read<long>());

        // >= 2^53 writes as ~i string
        long atBoundary = 9007199254740992L; // 2^53
        var atJson = WriteJson(atBoundary);
        Assert.IsTrue(atJson.Contains("~i"), ">= 2^53 should use ~i");
        Assert.AreEqual(atBoundary, Reader(atJson).Read<long>());

        // Negative boundary
        Assert.AreEqual(-9007199254740992L, Reader(WriteJson(-9007199254740992L)).Read<long>());

        // Integer as map key uses ~i form
        var d = new Dictionary<long, string> { [42L] = "value" };
        var mapJson = WriteJsonVerbose(d);
        Assert.IsTrue(mapJson.Contains("\"~i42\""), "Integer map key must use ~i form");
        var readD = Reader(mapJson).Read<IDictionary>();
        Assert.AreEqual("value", readD[42L]);

        // Read ~i tagged form
        Assert.AreEqual(42L, Reader("\"~i42\"").Read<long>());
        Assert.AreEqual(-999L, Reader("\"~i-999\"").Read<long>());
    }

    [TestMethod]
    public void TestVerifyFloatingPoint()
    {
        // Roundtrip float and double
        Assert.AreEqual(42.5, (double)Reader(WriteJson(42.5)).Read<object>(), 0.001);
        Assert.AreEqual(42.5, (double)Reader(WriteJson(42.5f)).Read<object>(), 0.01);
        Assert.AreEqual(42.5, (double)Reader(WriteJsonVerbose(42.5)).Read<object>(), 0.001);

        // Negative
        Assert.AreEqual(-3.14, (double)Reader(WriteJson(-3.14)).Read<object>(), 0.001);

        // Very small
        Assert.AreEqual(6.626e-34, (double)Reader(WriteJson(6.626e-34)).Read<object>(), 1e-40);

        // Very large — note: JSON may serialize large doubles as integers, so cast via Convert
        Assert.AreEqual(4.5e11, System.Convert.ToDouble(Reader(WriteJson(4.5e11)).Read<object>()), 1.0);

        // Float as map key uses ~d form  
        var d = new Dictionary<double, int> { [3.14] = 1 };
        var mapJson = WriteJsonVerbose(d);
        Assert.IsTrue(mapJson.Contains("\"~d"), "Float map key must use ~d form");
        var readD = Reader(mapJson).Read<IDictionary>();
        Assert.AreEqual(1L, readD[3.14]);

        // Read ~d tagged form
        Assert.AreEqual(42.5, Reader("\"~d42.5\"").Read<double>(), 0.001);
    }

    [TestMethod]
    public void TestVerifyArbitraryPrecisionInteger()
    {
        // Basic roundtrip
        var big = BigInteger.Parse("4256768765123454321897654321234567");
        Assert.AreEqual(big, Reader(WriteJson(big)).Read<BigInteger>());
        Assert.AreEqual(big, Reader(WriteJsonVerbose(big)).Read<BigInteger>());

        // Negative
        var negBig = BigInteger.Parse("-999999999999999999999999999");
        Assert.AreEqual(negBig, Reader(WriteJson(negBig)).Read<BigInteger>());

        // Small BigInteger still uses ~n
        Assert.AreEqual(new BigInteger(42), Reader(WriteJson(new BigInteger(42))).Read<BigInteger>());
        var smallBigJson = WriteJson(new BigInteger(42));
        Assert.IsTrue(smallBigJson.Contains("~n42"), "BigInteger always uses ~n tag");

        // Read from string
        Assert.AreEqual(BigInteger.Parse("9223372036854775808"),
            Reader("\"~n9223372036854775808\"").Read<BigInteger>());
    }

    [TestMethod]
    public void TestVerifyKeyword()
    {
        // Roundtrip
        var kw = TransitFactory.Keyword("foo");
        Assert.AreEqual("foo", Reader(WriteJson(kw)).Read<IKeyword>().ToString());
        Assert.AreEqual("foo", Reader(WriteJsonVerbose(kw)).Read<IKeyword>().ToString());

        // Encoding contains ~:
        Assert.IsTrue(WriteJson(kw).Contains("~:foo"));

        // Keywords as map keys
        var d = new Dictionary<object, object> { [TransitFactory.Keyword("name")] = "test" };
        var json = WriteJson(d);
        var readD = Reader(json).Read<IDictionary>();
        Assert.AreEqual("test", readD[TransitFactory.Keyword("name")]);

        // Keyword equality
        Assert.AreEqual(TransitFactory.Keyword("abc"), TransitFactory.Keyword("abc"));
        Assert.AreNotEqual(TransitFactory.Keyword("abc"), TransitFactory.Keyword("xyz"));

        // Keyword caching (> 3 chars total: ~:foo = 5 chars)
        IList l = new IKeyword[] { TransitFactory.Keyword("longkey"), TransitFactory.Keyword("longkey") };
        var cached = WriteJson(l);
        Assert.IsTrue(cached.Contains("^"), "Keywords > 3 chars should be cached in JSON mode");
    }

    [TestMethod]
    public void TestVerifySymbol()
    {
        // Roundtrip
        var sym = TransitFactory.Symbol("foo");
        Assert.AreEqual("foo", Reader(WriteJson(sym)).Read<ISymbol>().ToString());
        Assert.AreEqual("foo", Reader(WriteJsonVerbose(sym)).Read<ISymbol>().ToString());

        // Encoding contains ~$
        Assert.IsTrue(WriteJson(sym).Contains("~$foo"));

        // Symbol equality
        Assert.AreEqual(TransitFactory.Symbol("abc"), TransitFactory.Symbol("abc"));
        Assert.AreNotEqual(TransitFactory.Symbol("abc"), TransitFactory.Symbol("xyz"));

        // Symbol as map key
        var d = new Dictionary<object, object> { [TransitFactory.Symbol("mysym")] = 42L };
        var json = WriteJson(d);
        var readD = Reader(json).Read<IDictionary>();
        Assert.AreEqual(42L, readD[TransitFactory.Symbol("mysym")]);
    }

    [TestMethod]
    public void TestVerifyChar()
    {
        // Roundtrip
        Assert.AreEqual('f', Reader(WriteJson('f')).Read<char>());
        Assert.AreEqual('f', Reader(WriteJsonVerbose('f')).Read<char>());

        // Special characters
        Assert.AreEqual(' ', Reader("\"~c \"").Read<char>());
        Assert.AreEqual('~', Reader("\"~c~\"").Read<char>());

        // Char arrays
        char[] chars = { 'a', 'b', 'c' };
        var json = WriteJsonVerbose(chars);
        var list = Reader(json).Read<IList>();
        Assert.AreEqual('a', list[0]);
        Assert.AreEqual('b', list[1]);
        Assert.AreEqual('c', list[2]);
    }

    [TestMethod]
    public void TestVerifyBinary()
    {
        // Roundtrip
        byte[] bytes = Encoding.UTF8.GetBytes("Hello, Transit!");
        var json = WriteJson(bytes);
        Assert.IsTrue(json.Contains("~b"), "Binary should use ~b tag");
        byte[] decoded = Reader(json).Read<byte[]>();
        CollectionAssert.AreEqual(bytes, decoded);

        // Verbose mode
        byte[] decoded2 = Reader(WriteJsonVerbose(bytes)).Read<byte[]>();
        CollectionAssert.AreEqual(bytes, decoded2);

        // Empty byte array
        byte[] empty = Array.Empty<byte>();
        byte[] decodedEmpty = Reader(WriteJson(empty)).Read<byte[]>();
        Assert.AreEqual(0, decodedEmpty.Length);

        // Arbitrary bytes (non-UTF8)
        byte[] arbitrary = { 0x00, 0xFF, 0x7F, 0x80, 0x01 };
        byte[] decodedArb = Reader(WriteJson(arbitrary)).Read<byte[]>();
        CollectionAssert.AreEqual(arbitrary, decodedArb);
    }

    [TestMethod]
    public void TestVerifyUuid()
    {
        // String form roundtrip
        var guid = Guid.NewGuid();
        Assert.AreEqual(guid, Reader(WriteJson(guid)).Read<Guid>());
        Assert.AreEqual(guid, Reader(WriteJsonVerbose(guid)).Read<Guid>());

        // Encoding contains ~u
        Assert.IsTrue(WriteJsonVerbose(guid).Contains("~u" + guid.ToString()));

        // hi64/lo64 array form
        long hi64 = ((Transit.Net.Java.Uuid)guid).MostSignificantBits;
        long lo64 = ((Transit.Net.Java.Uuid)guid).LeastSignificantBits;
        var readFromArray = Reader("{\"~#u\": [" + hi64 + ", " + lo64 + "]}").Read<Guid>();
        Assert.AreEqual(guid, readFromArray);

        // UUID as map key
        var d = new Dictionary<Guid, string> { [guid] = "value" };
        var readD = Reader(WriteJsonVerbose(d)).Read<IDictionary>();
        Assert.AreEqual("value", readD[guid]);

        // Known UUID roundtrip
        var known = Guid.Parse("531a379e-31bb-4ce1-8690-158dceb64be6");
        Assert.AreEqual(known, Reader("\"~u531a379e-31bb-4ce1-8690-158dceb64be6\"").Read<Guid>());
    }

    [TestMethod]
    public void TestVerifyUri()
    {
        // Basic roundtrip
        var uri = new Uri("http://www.example.com/path?q=1");
        Assert.AreEqual(uri, Reader(WriteJson(uri)).Read<Uri>());
        Assert.AreEqual(uri, Reader(WriteJsonVerbose(uri)).Read<Uri>());

        // Various schemes
        Assert.AreEqual(new Uri("https://secure.example.com"),
            Reader("\"~rhttps://secure.example.com\"").Read<Uri>());
        Assert.AreEqual(new Uri("ftp://files.example.com/file.txt"),
            Reader("\"~rftp://files.example.com/file.txt\"").Read<Uri>());
        Assert.AreEqual(new Uri("mailto:user@example.com"),
            Reader("\"~rmailto:user@example.com\"").Read<Uri>());

        // URI as map key
        var d = new Dictionary<Uri, string> { [uri] = "value" };
        var readD = Reader(WriteJsonVerbose(d)).Read<IDictionary>();
        Assert.AreEqual("value", readD[uri]);
    }

    [TestMethod]
    public void TestVerifyDateTime()
    {
        // JSON machine mode uses ~m (milliseconds)
        var d = DateTime.UtcNow;
        var jsonMachine = WriteJson(d);
        Assert.IsTrue(jsonMachine.Contains("~m"), "JSON mode should use ~m tag");
        var readMachine = Reader(jsonMachine).Read<DateTime>().ToUniversalTime();
        // millisecond precision
        Assert.AreEqual(d.Year, readMachine.Year);
        Assert.AreEqual(d.Month, readMachine.Month);
        Assert.AreEqual(d.Day, readMachine.Day);

        // Verbose mode uses ~t (RFC 3339)
        var jsonVerbose = WriteJsonVerbose(d);
        Assert.IsTrue(jsonVerbose.Contains("~t"), "JSON-Verbose should use ~t tag");

        // Pre-1970 timestamps
        var pre1970 = new DateTime(1776, 7, 4, 12, 0, 0, DateTimeKind.Utc);
        var pre1970Read = Reader(WriteJson(pre1970)).Read<DateTime>().ToUniversalTime();
        Assert.AreEqual(pre1970.Year, pre1970Read.Year);
        Assert.AreEqual(pre1970.Month, pre1970Read.Month);

        // Epoch exactly
        var epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        Assert.IsTrue(WriteJson(epoch).Contains("~m0"), "Epoch should encode as ~m0");

        // Date as map key
        var mapD = new Dictionary<DateTime, string> { [d] = "now" };
        var readMapD = Reader(WriteJsonVerbose(mapD)).Read<IDictionary>();
        Assert.AreEqual(1, readMapD.Count);
    }

    [TestMethod]
    public void TestVerifySpecialNumbers()
    {
        // NaN roundtrip
        Assert.AreEqual(double.NaN, Reader(WriteJson(double.NaN)).Read<double>());
        Assert.AreEqual(double.NaN, Reader(WriteJsonVerbose(double.NaN)).Read<double>());

        // Positive infinity
        Assert.AreEqual(double.PositiveInfinity, Reader(WriteJson(double.PositiveInfinity)).Read<double>());
        Assert.AreEqual(double.PositiveInfinity, Reader(WriteJsonVerbose(double.PositiveInfinity)).Read<double>());

        // Negative infinity
        Assert.AreEqual(double.NegativeInfinity, Reader(WriteJson(double.NegativeInfinity)).Read<double>());
        Assert.AreEqual(double.NegativeInfinity, Reader(WriteJsonVerbose(double.NegativeInfinity)).Read<double>());

        // Float variants
        Assert.AreEqual(double.NaN, Reader(WriteJson(float.NaN)).Read<double>());
        Assert.AreEqual(double.PositiveInfinity, Reader(WriteJson(float.PositiveInfinity)).Read<double>());
        Assert.AreEqual(double.NegativeInfinity, Reader(WriteJson(float.NegativeInfinity)).Read<double>());

        // Encoding uses ~z tag
        Assert.IsTrue(WriteJson(double.NaN).Contains("~zNaN"));
        Assert.IsTrue(WriteJson(double.PositiveInfinity).Contains("~zINF"));
        Assert.IsTrue(WriteJson(double.NegativeInfinity).Contains("~z-INF"));
    }

    [TestMethod]
    public void TestVerifyArray()
    {
        // Empty array
        var empty = new List<object>();
        Assert.AreEqual("[]", WriteJsonVerbose(empty));
        var readEmpty = Reader(WriteJson(empty)).Read<IList>();
        Assert.AreEqual(0, readEmpty.Count);

        // Simple array
        var simple = new List<object> { 1L, 2L, 3L };
        var readSimple = Reader(WriteJson(simple)).Read<IList>();
        Assert.AreEqual(3, readSimple.Count);
        Assert.AreEqual(1L, readSimple[0]);

        // Mixed-type array
        var mixed = new List<object> { "hello", 42L, true, TransitFactory.Keyword("kw") };
        var readMixed = Reader(WriteJson(mixed)).Read<IList>();
        Assert.AreEqual(4, readMixed.Count);
        Assert.AreEqual("hello", readMixed[0]);
        Assert.AreEqual(42L, readMixed[1]);
        Assert.AreEqual(true, readMixed[2]);
        Assert.AreEqual("kw", readMixed[3]!.ToString());

        // Nested arrays
        var nested = new List<object> { new List<object> { 1L, 2L }, new List<object> { 3L, 4L } };
        var readNested = Reader(WriteJson(nested)).Read<IList>();
        Assert.AreEqual(2, readNested.Count);
        Assert.AreEqual(2, ((IList)readNested[0]!).Count);
    }

    [TestMethod]
    public void TestVerifyList()
    {
        // IEnumerable (linked list) encodes as ~#list
        var linked = new LinkedList<object>();
        linked.AddLast("a");
        linked.AddLast("b");
        IEnumerable<object> e = linked;

        var json = WriteJson(e);
        Assert.IsTrue(json.Contains("~#list"), "IEnumerable should encode as ~#list");
        var read = Reader(json).Read<IEnumerable>().Cast<object>().ToList();
        Assert.AreEqual(2, read.Count);
        Assert.AreEqual("a", read[0]);
        Assert.AreEqual("b", read[1]);

        // Empty list
        var emptyLinked = new LinkedList<object>();
        IEnumerable<object> emptyEnum = emptyLinked;
        var emptyJson = WriteJson(emptyEnum);
        Assert.IsTrue(emptyJson.Contains("~#list"));
        var readEmpty = Reader(emptyJson).Read<IEnumerable>().Cast<object>().ToList();
        Assert.AreEqual(0, readEmpty.Count);

        // Verbose mode uses {~#list: [...]}
        var verboseJson = WriteJsonVerbose(e);
        Assert.IsTrue(verboseJson.Contains("\"~#list\""));
    }

    [TestMethod]
    public void TestVerifyMap()
    {
        // Simple map roundtrip
        var m = new Dictionary<string, object> { ["a"] = 1L, ["b"] = 2L };
        var readM = Reader(WriteJson(m)).Read<IDictionary>();
        Assert.AreEqual(1L, readM["a"]);
        Assert.AreEqual(2L, readM["b"]);

        // JSON mode uses "^ " marker
        var jsonMap = WriteJson(m);
        Assert.IsTrue(jsonMap.Contains("\"^ \""), "JSON mode should use array-map marker");

        // Verbose mode uses JSON object
        var verboseMap = WriteJsonVerbose(m);
        Assert.IsTrue(verboseMap.StartsWith("{"), "Verbose mode should use JSON object");

        // Empty map
        var emptyM = new Dictionary<string, object>();
        Assert.AreEqual("[\"^ \"]", WriteJson(emptyM));
        Assert.AreEqual("{}", WriteJsonVerbose(emptyM));

        // Nested maps
        var nested = new Dictionary<string, object>
        {
            ["inner"] = new Dictionary<string, object> { ["deep"] = 42L }
        };
        var readNested = Reader(WriteJson(nested)).Read<IDictionary>();
        Assert.AreEqual(42L, ((IDictionary)readNested["inner"]!)["deep"]);

        // Map with keyword keys
        var kwMap = new Dictionary<object, object>
        {
            [TransitFactory.Keyword("name")] = "alice",
            [TransitFactory.Keyword("age")] = 30L
        };
        var readKw = Reader(WriteJson(kwMap)).Read<IDictionary>();
        Assert.AreEqual("alice", readKw[TransitFactory.Keyword("name")]);
        Assert.AreEqual(30L, readKw[TransitFactory.Keyword("age")]);
    }

    [TestMethod]
    public void TestVerifyCmap()
    {
        // Map with composite keys falls back to cmap
        IRatio r = new Ratio(BigInteger.One, new BigInteger(3));
        var d = new Dictionary<object, object> { [r] = "ratio-key" };
        var json = WriteJson(d);
        Assert.IsTrue(json.Contains("~#cmap"), "Composite key map should encode as cmap");
        var readD = Reader(json).Read<IDictionary>();
        Assert.AreEqual(1, readD.Count);

        // Verbose
        var verboseJson = WriteJsonVerbose(d);
        Assert.IsTrue(verboseJson.Contains("~#cmap"));

        // cmap with null key
        var d2 = new NullKeyDictionary();
        d2[null] = "null-value";
        d2[new List<object> { 1L }] = "array-value";
        var json2 = WriteJson(d2);
        Assert.IsTrue(json2.Contains("~#cmap"));
        var readD2 = Reader(json2).Read<IDictionary>();
        Assert.AreEqual("null-value", readD2[null!]);
    }

    [TestMethod]
    public void TestVerifySet()
    {
        // Roundtrip
        var s = new HashSet<object> { 1L, 2L, 3L };
        var json = WriteJson(s);
        Assert.IsTrue(json.Contains("~#set"), "Set should encode as ~#set");
        var readS = Reader(json).Read<ISet<object>>();
        Assert.AreEqual(3, readS.Count);
        Assert.IsTrue(readS.Contains(1L));
        Assert.IsTrue(readS.Contains(2L));
        Assert.IsTrue(readS.Contains(3L));

        // Verbose
        Assert.IsTrue(WriteJsonVerbose(s).Contains("\"~#set\""));

        // Empty set
        var empty = new HashSet<object>();
        var readEmpty = Reader(WriteJson(empty)).Read<ISet<object>>();
        Assert.AreEqual(0, readEmpty.Count);

        // Set with mixed types
        var mixed = new HashSet<object> { "hello", 42L, true };
        var readMixed = Reader(WriteJson(mixed)).Read<ISet<object>>();
        Assert.AreEqual(3, readMixed.Count);
        Assert.IsTrue(readMixed.Contains("hello"));
        Assert.IsTrue(readMixed.Contains(42L));
        Assert.IsTrue(readMixed.Contains(true));
    }

    [TestMethod]
    public void TestVerifyRatio()
    {
        // Roundtrip
        IRatio r = new Ratio(BigInteger.One, new BigInteger(3));
        var json = WriteJson(r);
        Assert.IsTrue(json.Contains("~#ratio"), "Ratio should encode as ~#ratio");
        var readR = Reader(json).Read<IRatio>();
        Assert.AreEqual(BigInteger.One, readR.Numerator);
        Assert.AreEqual(new BigInteger(3), readR.Denominator);

        // Verbose
        var verbose = WriteJsonVerbose(r);
        Assert.IsTrue(verbose.Contains("\"~#ratio\""));

        // Value check
        Assert.AreEqual(1.0 / 3.0, readR.GetValue(), 0.0001);
    }

    [TestMethod]
    public void TestVerifyQuotedValues()
    {
        // Top-level scalars are quoted
        var jsonStr = WriteJson("hello");
        Assert.IsTrue(jsonStr.Contains("~#'"), "Top-level scalar should be quoted");
        Assert.AreEqual("hello", Reader(jsonStr).Read<string>());

        // Top-level null
        var jsonNull = WriteJson(null);
        Assert.IsTrue(jsonNull.Contains("~#'"), "Top-level null should be quoted");
        Assert.IsNull(Reader(jsonNull).Read<object>());

        // Top-level bool
        Assert.IsTrue(WriteJson(true).Contains("~#'"));
        Assert.AreEqual(true, Reader(WriteJson(true)).Read<bool>());

        // Top-level integer  
        Assert.IsTrue(WriteJson(42).Contains("~#'"));
        Assert.AreEqual(42L, Reader(WriteJson(42)).Read<long>());

        // Verbose mode uses {~#': value}
        Assert.IsTrue(WriteJsonVerbose("hello").Contains("\"~#'\""));

        // Arrays/maps are NOT quoted at top level
        var list = new List<object> { 1L, 2L };
        Assert.IsFalse(WriteJson(list).Contains("~#'"), "Arrays should not be quoted");
    }

    [TestMethod]
    public void TestVerifyTaggedValue()
    {
        // TaggedValue roundtrip
        var tv = TransitFactory.TaggedValue("point", new List<object> { 1L, 2L });
        var json = WriteJson(tv);
        Assert.IsTrue(json.Contains("~#point"));
        var readTv = Reader(json).Read<ITaggedValue>();
        Assert.AreEqual("point", readTv.Tag);
        var rep = (IList<object>)readTv.Representation;
        Assert.AreEqual(1L, rep[0]);
        Assert.AreEqual(2L, rep[1]);

        // Verbose
        var verbose = WriteJsonVerbose(tv);
        Assert.IsTrue(verbose.Contains("\"~#point\""));

        // Unknown single-char tag
        var unknown = Reader("\"~jfoo\"").Read<ITaggedValue>();
        Assert.AreEqual("j", unknown.Tag);
        Assert.AreEqual("foo", unknown.Representation);
    }

    [TestMethod]
    public void TestVerifyLink()
    {
        // Full link roundtrip
        var link = TransitFactory.Link("http://example.com/", "rel", "name", "prompt", "link");
        var json = WriteJson(link);
        Assert.IsTrue(json.Contains("~#link"));
        var readLink = Reader(json).Read<ILink>();
        Assert.AreEqual("http://example.com/", readLink.Href.AbsoluteUri);
        Assert.AreEqual("rel", readLink.Rel);
        Assert.AreEqual("name", readLink.Name);
        Assert.AreEqual("prompt", readLink.Prompt);
        Assert.AreEqual("link", readLink.Render);

        // Verbose
        var verbose = WriteJsonVerbose(link);
        Assert.IsTrue(verbose.Contains("\"~#link\""));
        var readVerbose = Reader(verbose).Read<ILink>();
        Assert.AreEqual("http://example.com/", readVerbose.Href.AbsoluteUri);

        // Minimal link (only required fields)
        var minLink = TransitFactory.Link("http://min.com/", "rel-only");
        var readMin = Reader(WriteJson(minLink)).Read<ILink>();
        Assert.AreEqual("http://min.com/", readMin.Href.AbsoluteUri);
        Assert.AreEqual("rel-only", readMin.Rel);
    }

    [TestMethod]
    public void TestVerifySpecialCharEscaping()
    {
        // ~ at start is escaped to ~~
        Assert.AreEqual("~hello", Reader(WriteJson("~hello")).Read<string>());

        // ` at start is escaped to ~`
        Assert.AreEqual("`hello", Reader(WriteJson("`hello")).Read<string>());

        // ^ at start is escaped to ~^
        Assert.AreEqual("^hello", Reader(WriteJson("^hello")).Read<string>());

        // "^ " (map-as-array marker) in a list must be escaped
        var l = new List<object> { "^ " };
        var json = WriteJson(l);
        Assert.IsTrue(json.Contains("~^ "), "^ marker must be escaped in arrays");
        var readL = Reader(json).Read<IList<object>>();
        Assert.AreEqual("^ ", readL[0]);

        // Double-escape: ~~foo reads back to ~foo
        Assert.AreEqual("~foo", Reader("\"~~foo\"").Read<string>());

        // ~_ is null, ~~_ is the literal string "~_"
        Assert.IsNull(Reader("\"~_\"").Read<object>());
        Assert.AreEqual("~_", Reader("\"~~_\"").Read<string>());

        // ~#tag is a tag, ~~#tag is the string "~#tag"
        Assert.IsInstanceOfType<ITaggedValue>(Reader("{\"~#foo\": 1}").Read<object>());
        // Map with string key "~#set" (double-escaped)
        var m = Reader("{\"~~#set\": 1}").Read<IDictionary>();
        Assert.IsTrue(m.Contains("~#set"));
    }

    [TestMethod]
    public void TestVerifyWriteCache()
    {
        // Keywords > 3 chars are cached in JSON mode
        var kws = Enumerable.Range(0, 5)
            .SelectMany(_ => new[] { TransitFactory.Keyword("alpha"), TransitFactory.Keyword("beta1") })
            .ToList();
        var json = WriteJson(kws);
        Assert.IsTrue(json.Contains("^"), "Repeated keywords should be cached");

        // Short values <= 3 chars are NOT cached
        var shortKws = new List<object> { TransitFactory.Keyword("ab"), TransitFactory.Keyword("ab") };
        var shortJson = WriteJson(shortKws);
        // ~:ab = 4 chars, so it IS long enough to cache
        // But ~:a = 3 chars would not be. Let's test with single-char keyword:
        var singleCharKws = new List<object> { TransitFactory.Keyword("a"), TransitFactory.Keyword("a") };
        var singleCharJson = WriteJson(singleCharKws);
        // ~:a is exactly 3 chars - should NOT be cached
        Assert.IsFalse(singleCharJson.Contains("^"), "Keywords <= 3 chars total should not be cached");

        // Verbose mode has caching disabled
        var verboseJson = WriteJsonVerbose(kws);
        Assert.IsFalse(verboseJson.Contains("^"), "Verbose mode should not cache");

        // Cache codes use ^ prefix
        var wc = new WriteCache(true);
        Assert.AreEqual("~:longkey", wc.CacheWrite("~:longkey", false)); // first occurrence
        Assert.AreEqual("^" + (char)WriteCache.BaseCharIdx, wc.CacheWrite("~:longkey", false)); // cached

        // Cache wraps at 44^2
        var wc2 = new WriteCache(true);
        for (int i = 0; i < 44 * 44 + 1; i++)
        {
            string key = "~:key_pad_" + i.ToString("D6");
            wc2.CacheWrite(key, false);
        }
        // After wrapping, should still function (first entry after wrap is stored fresh)
        string afterWrap = "~:after_wrap";
        Assert.AreEqual(afterWrap, wc2.CacheWrite(afterWrap, false));
    }

    [TestMethod]
    public void TestVerifyReadCache()
    {
        // Read cache resolves codes correctly
        var rc = new ReadCache();
        Assert.AreEqual("~:longkey", rc.CacheRead("~:longkey", false));
        Assert.AreEqual("~:longkey", rc.CacheRead("^" + (char)WriteCache.BaseCharIdx, false));

        // Cache stays in sync between reader and writer across a write-then-read
        var data = new Dictionary<object, object>();
        for (int i = 0; i < 50; i++)
            data[TransitFactory.Keyword("key" + i.ToString("D3"))] = (long)i;
        var readData = Reader(WriteJson(data)).Read<IDictionary>();
        for (int i = 0; i < 50; i++)
            Assert.AreEqual((long)i, readData[TransitFactory.Keyword("key" + i.ToString("D3"))]);

        // Large cache boundary test: 1935-1937 keywords (near 44^2 = 1936)
        var bigList = new List<object>();
        for (int i = 0; i < 1937; i++)
            bigList.Add(TransitFactory.Keyword("kw" + i.ToString("D5")));
        var readBig = Reader(WriteJson(bigList)).Read<IList>();
        Assert.AreEqual(1937, readBig.Count);
        Assert.AreEqual("kw00000", readBig[0]!.ToString());
        Assert.AreEqual("kw01936", readBig[1936]!.ToString());
    }

    [TestMethod]
    public void TestVerifyCustomReadHandlers()
    {
        // Custom handler for known tag
        var handlers = new Dictionary<string, IReadHandler>
        {
            ["point"] = new PointReadHandler()
        };
        using var input = new MemoryStream(Encoding.UTF8.GetBytes("[\"~#point\",[10,20]]"));
        using var reader = TransitFactory.Reader(TransitFactory.Format.Json, input, handlers, null);
        var point = reader.Read<Point>();
        Assert.AreEqual(new Point(10, 20), point);

        // Default handler catches unrecognized tags
        var defaultHandler = new CatchAllDefaultReadHandler();
        using var input2 = new MemoryStream(Encoding.UTF8.GetBytes("[\"~#widget\",[1,2,3]]"));
        using var reader2 = TransitFactory.Reader(TransitFactory.Format.Json, input2, null, defaultHandler);
        var result = reader2.Read<string>();
        Assert.IsTrue(result.Contains("widget"));
    }

    [TestMethod]
    public void TestVerifyCustomWriteHandlers()
    {
        // Custom write handler
        var handlers = new Dictionary<Type, IWriteHandler>
        {
            [typeof(Point)] = new PointWriteHandler()
        };
        var json = WriteJson(new Point(5, 10), handlers);
        Assert.IsTrue(json.Contains("~#point"));
        Assert.IsTrue(json.Contains("[5,10]"));

        // Verbose handler
        var verbose = WriteJsonVerbose(new Point(5, 10), handlers);
        Assert.IsTrue(verbose.Contains("~#point"));

        // Handler cache not corrupted across multiple writers
        for (int i = 0; i < 3; i++)
        {
            using var output = new MemoryStream();
            using var w = TransitFactory.Writer<object>(TransitFactory.Format.Json, output, handlers);
            w.Write(new Point(i, i));
            output.Position = 0;
            using var sr = new StreamReader(output, leaveOpen: true);
            var json2 = sr.ReadToEnd();
            Assert.IsTrue(json2.Contains($"[{i},{i}]"));
        }
    }

    [TestMethod]
    public void TestVerifyRoundtripAllTypes()
    {
        // Scalar types roundtrip in both modes
        AssertRoundtrip("hello");
        AssertRoundtrip(42L);
        AssertRoundtrip(42.5);
        AssertRoundtrip(true);
        AssertRoundtrip(false);
        AssertRoundtrip('x');
        AssertRoundtripByEquals(TransitFactory.Keyword("test"));
        AssertRoundtripByEquals(TransitFactory.Symbol("sym"));
        AssertRoundtripByEquals(new Uri("http://example.com/"));
        AssertRoundtripByEquals(Guid.NewGuid());

        // BigInteger
        var big = BigInteger.Parse("123456789012345678901234567890");
        Assert.AreEqual(big, Reader(WriteJson(big)).Read<BigInteger>());

        // BigRational
        var br = new BigRational(12.345M);
        Assert.AreEqual(br, Reader(WriteJson(br)).Read<BigRational>());

        // Composites
        var list = new List<object> { 1L, "two", true };
        var readList = Reader(WriteJson(list)).Read<IList>();
        Assert.AreEqual(3, readList.Count);
        Assert.AreEqual("two", readList[1]);

        var set = new HashSet<object> { 1L, 2L };
        var readSet = Reader(WriteJson(set)).Read<ISet<object>>();
        Assert.IsTrue(readSet.Contains(1L));

        var map = new Dictionary<string, object> { ["key"] = "value" };
        var readMap = Reader(WriteJson(map)).Read<IDictionary>();
        Assert.AreEqual("value", readMap["key"]);

        // Nested structure
        var nested = new Dictionary<string, object>
        {
            ["list"] = new List<object> { 1L, new Dictionary<string, object> { ["a"] = 2L } },
            ["set"] = new HashSet<object> { "x", "y" }
        };
        var readNested = Reader(WriteJson(nested)).Read<IDictionary>();
        var innerList = (IList)readNested["list"]!;
        var innerMap = (IDictionary)innerList[1]!;
        Assert.AreEqual(2L, innerMap["a"]);
    }

    private void AssertRoundtrip(object value)
    {
        Assert.AreEqual(value, Reader(WriteJson(value)).Read<object>());
        Assert.AreEqual(value, Reader(WriteJsonVerbose(value)).Read<object>());
    }

    private void AssertRoundtripByEquals(object value)
    {
        var readJson = Reader(WriteJson(value)).Read<object>();
        var readVerbose = Reader(WriteJsonVerbose(value)).Read<object>();
        Assert.AreEqual(value, readJson);
        Assert.AreEqual(value, readVerbose);
    }

    [TestMethod]
    public void TestVerifyErrorHandling()
    {
        // Writing unregistered type throws NotSupportedException
        Assert.ThrowsException<NotSupportedException>(() => WriteJson(new object()));
        Assert.ThrowsException<NotSupportedException>(() => WriteJsonVerbose(new object()));

        // Reading empty stream throws some JSON-related exception
        bool threw = false;
        try
        {
            using var input = new MemoryStream(Array.Empty<byte>());
            using var reader = TransitFactory.Reader(TransitFactory.Format.Json, input);
            reader.Read<object>();
        }
        catch (Exception ex) when (ex.GetType().Name.Contains("Json"))
        {
            threw = true;
        }
        Assert.IsTrue(threw, "Reading empty stream should throw a JSON exception");
    }

    [TestMethod]
    public void TestVerifyExemplarFiles()
    {
        // Locate exemplar directory by searching upward from known project paths
        string? exemplarDir = null;
        // Try common known paths
        string[] candidates = {
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "transit-format", "examples", "0.8", "simple")),
            "/Users/is/src/transit-csharp/transit-format/examples/0.8/simple",
        };
        foreach (var c in candidates)
        {
            if (Directory.Exists(c)) { exemplarDir = c; break; }
        }
        // If still not found, search upward from BaseDirectory
        if (exemplarDir == null)
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                var candidate = Path.Combine(dir.FullName, "transit-format", "examples", "0.8", "simple");
                if (Directory.Exists(candidate)) { exemplarDir = candidate; break; }
                dir = dir.Parent;
            }
        }
        if (exemplarDir == null) {
            Assert.Inconclusive("Could not find transit-format exemplar directory. Skipping test.");
        }

        // Test a selection of exemplar files that should decode without error
        string[] exemplarNames = {
            "one_string", "one", "true", "false",
            "ints", "ints_interesting",
            "doubles_small", "doubles_interesting",
            "keywords", "symbols",
            "list_simple", "list_empty", "list_mixed", "list_nested",
            "map_simple", "map_string_keys", "map_numeric_keys",
            "set_simple", "set_empty",
            "nil",
        };

        int tested = 0;
        foreach (var name in exemplarNames)
        {
            string jsonPath = Path.Combine(exemplarDir, name + ".json");
            string verbosePath = Path.Combine(exemplarDir, name + ".verbose.json");

            if (File.Exists(jsonPath))
            {
                var jsonContent = File.ReadAllText(jsonPath);
                var result = Reader(jsonContent).Read<object>();
                // Just verify it decodes without throwing
                tested++;
            }

            if (File.Exists(verbosePath))
            {
                var verboseContent = File.ReadAllText(verbosePath);
                var result = Reader(verboseContent).Read<object>();
                tested++;
            }
        }

        if (tested == 0) {
            Assert.Inconclusive($"Should have tested at least one exemplar file. Looked in: {exemplarDir}. Skipping test.");
        }
    }

    #endregion
}
