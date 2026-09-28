using System.Collections.Concurrent;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Transit.Net.Tests;

[TestClass]
public class ConcurrentWriterTests
{
    private static object MapPayload() => new Dictionary<object, object>
    {
        {
            TransitFactory.Keyword("items"),
            new Dictionary<object, object>
            {
                { 1L, TransitFactory.Keyword("first") },
                { 2L, TransitFactory.Keyword("second") },
            }
        },
        { TransitFactory.Keyword("session"), "abc" },
    };

    private static object CmapPayload() => new Dictionary<object, object>
    {
        {
            TransitFactory.Keyword("items"),
            new Dictionary<object, object>
            {
                { new List<object> { "1", "2" }, "an array key forces cmap" },
                { "plain", 42L },
            }
        },
        { TransitFactory.Keyword("session"), "abc" },
    };

    // NullKeyDictionary is the only registered type whose handler is emitter-aware, so it is the
    // only one reaching the exact-type lookup. It needs both a map and a cmap payload: a handler
    // that only ever sees one of the two never diverges, and so cannot detect a shared instance.
    private static object NullKeyMapPayload()
    {
        var dict = new Transit.Net.Impl.NullKeyDictionary();
        dict[null] = "a null key";
        dict[TransitFactory.Keyword("items")] = new Dictionary<object, object>
        {
            { 1L, TransitFactory.Keyword("first") },
            { 2L, TransitFactory.Keyword("second") },
        };
        return dict;
    }

    private static object NullKeyCmapPayload()
    {
        var dict = new Transit.Net.Impl.NullKeyDictionary();
        dict[null] = "a null key";
        dict[new List<object> { "1", "2" }] = "an array key forces cmap";
        dict[TransitFactory.Keyword("session")] = "abc";
        return dict;
    }

    private static string Write(object obj, TransitFactory.Format format)
    {
        using var output = new MemoryStream();
        using var w = TransitFactory.Writer<object>(format, output);
        w.Write(obj);

        output.Position = 0;
        using var sr = new StreamReader(output, leaveOpen: true);
        return sr.ReadToEnd();
    }

    [TestMethod]
    public void TestConcurrentJsonWritersDoNotInterfere()
        => AssertConcurrentWritesMatchSingleThreaded(TransitFactory.Format.Json);

    [TestMethod]
    public void TestConcurrentJsonVerboseWritersDoNotInterfere()
        => AssertConcurrentWritesMatchSingleThreaded(TransitFactory.Format.JsonVerbose);

    private static void AssertConcurrentWritesMatchSingleThreaded(TransitFactory.Format format)
    {
        var payloads = new Func<object>[] { MapPayload, CmapPayload, NullKeyMapPayload, NullKeyCmapPayload };
        var expected = payloads.Select(payload => Write(payload(), format)).ToArray();

        const int iterations = 500;
        var threadCount = Math.Max(4, Environment.ProcessorCount);
        var failures = new ConcurrentQueue<string>();
        var start = new Barrier(threadCount);

        var threads = Enumerable.Range(0, threadCount)
            .Select(t => new Thread(() =>
            {
                start.SignalAndWait();

                for (var i = 0; i < iterations; i++)
                {
                    var index = (t + i) % payloads.Length;
                    try
                    {
                        var actual = Write(payloads[index](), format);
                        if (actual != expected[index])
                            failures.Enqueue($"payload {index}: expected {expected[index]}, got {actual}");
                    }
                    catch (Exception ex)
                    {
                        failures.Enqueue($"payload {index}: {ex.GetType().Name}: {ex.Message}");
                    }
                }
            }))
            .ToArray();

        foreach (var thread in threads)
            thread.Start();
        foreach (var thread in threads)
            thread.Join();

        Assert.AreEqual(0, failures.Count, string.Join(Environment.NewLine, failures.Take(5)));
    }
}
