using System.Buffers;
using System.Collections;
using System.Collections.Frozen;
using System.Numerics;
using System.Text.Json;
using Transit.Net.Impl.WriteHandlers;
using Transit.Net.Numerics;
using Transit.Net.Spi;

namespace Transit.Net.Impl;

/// <summary>
/// Constructs transit writers. Handler dictionaries are built once as FrozenDictionary.
/// </summary>
internal static class WriterFactory
{
    private static readonly FrozenDictionary<Type, IWriteHandler> DefaultHandlersInstance = BuildDefaultHandlers();

    private static FrozenDictionary<Type, IWriteHandler> BuildDefaultHandlers()
    {
        var integerHandler = new IntegerWriteHandler("i");
        var listHandler = new ListWriteHandler();
        var keywordHandler = new ToStringWriteHandler(":");
        var symbolHandler = new ToStringWriteHandler("$");
        var dict = new Dictionary<Type, IWriteHandler>
        {
            [typeof(bool)] = new BooleanWriteHandler(),
            [typeof(NullType)] = new NullWriteHandler(),
            [typeof(string)] = new ToStringWriteHandler("s"),
            [typeof(int)] = integerHandler,
            [typeof(long)] = integerHandler,
            [typeof(short)] = integerHandler,
            [typeof(byte)] = integerHandler,
            [typeof(BigInteger)] = new ToStringWriteHandler("n"),
            [typeof(decimal)] = new ToStringWriteHandler("f"),
            [typeof(BigRational)] = new ToStringWriteHandler("f"),
            [typeof(float)] = new FloatWriteHandler(),
            [typeof(double)] = new DoubleWriteHandler(),
            [typeof(char)] = new ToStringWriteHandler("c"),
            [typeof(IKeyword)] = keywordHandler,
            [typeof(Keyword)] = keywordHandler,
            [typeof(ISymbol)] = symbolHandler,
            [typeof(Symbol)] = symbolHandler,
            [typeof(byte[])] = new BinaryWriteHandler(),
            [typeof(Guid)] = new GuidWriteHandler(),
            [typeof(Uri)] = new ToStringWriteHandler("r"),
            [typeof(DateTime)] = new DateTimeWriteHandler(),
            [typeof(IRatio)] = new RatioWriteHandler(),
            [typeof(ILink)] = new LinkWriteHandler(),
            [typeof(Quote)] = new QuoteWriteHandler(),
            [typeof(ITaggedValue)] = new TaggedValueWriteHandler(),
            [typeof(ISet<>)] = new SetWriteHandler(),
            [typeof(IEnumerable)] = new EnumerableWriteHandler(),
            [typeof(ListWrapper)] = new EnumerableWriteHandler(),
            [typeof(IList<>)] = listHandler,
            [typeof(IDictionary<,>)] = new DictionaryWriteHandler(),
            [typeof(NullKeyDictionary)] = new DictionaryWriteHandler(),
            [typeof(int[])] = listHandler,
            [typeof(long[])] = listHandler,
            [typeof(float[])] = listHandler,
            [typeof(double[])] = listHandler,
            [typeof(short[])] = listHandler,
            [typeof(bool[])] = listHandler,
            [typeof(char[])] = listHandler,
            [typeof(object[])] = listHandler,
            [typeof(TimeSpan)] = new TimeSpanWriteHandler(),
            [typeof(DateTimeOffset)] = new DateTimeOffsetWriteHandler(),
            [typeof(Enum)] = new EnumWriteHandler(),
            [typeof(System.Runtime.CompilerServices.ITuple)] = new TupleWriteHandler(),
        };
        return dict.ToFrozenDictionary();
    }

    public static FrozenDictionary<Type, IWriteHandler> DefaultHandlers() => DefaultHandlersInstance;

    // Keywords and symbols are registered by interface and by concrete struct, and the exact
    // type wins during resolution. The structs are internal, so a caller can only override the
    // interface -- without mirroring, the struct entry would silently shadow that override.
    private static readonly (Type Interface, Type Concrete)[] KeywordLikeTypes =
    {
        (typeof(IKeyword), typeof(Keyword)),
        (typeof(ISymbol), typeof(Symbol)),
    };

    private static bool NeedsKeywordMirroring(IReadOnlyDictionary<Type, IWriteHandler> handlers)
    {
        foreach (var (iface, concrete) in KeywordLikeTypes)
        {
            if (handlers.TryGetValue(iface, out var overridden)
                && handlers.TryGetValue(concrete, out var current)
                && !ReferenceEquals(overridden, current))
                return true;
        }
        return false;
    }

    private static void MirrorKeywordOverrides(Dictionary<Type, IWriteHandler> handlers)
    {
        foreach (var (iface, concrete) in KeywordLikeTypes)
        {
            if (handlers.TryGetValue(iface, out var overridden))
                handlers[concrete] = overridden;
        }
    }

    public static FrozenDictionary<Type, IWriteHandler> MergedHandlers(IDictionary<Type, IWriteHandler>? customHandlers)
    {
        // A frozen table is the caller's own, taken verbatim unless an interface override in it
        // would be shadowed -- which happens when the table was built from DefaultHandlers().
        if (customHandlers is FrozenDictionary<Type, IWriteHandler> frozen)
        {
            if (!NeedsKeywordMirroring(frozen))
                return frozen;

            var mirrored = frozen.ToDictionary();
            MirrorKeywordOverrides(mirrored);
            return mirrored.ToFrozenDictionary();
        }

        if (customHandlers == null || customHandlers.Count == 0)
            return DefaultHandlersInstance;

        var dict = DefaultHandlersInstance.ToDictionary();
        foreach (var kvp in customHandlers)
            dict[kvp.Key] = kvp.Value;
        if (NeedsKeywordMirroring(dict))
            MirrorKeywordOverrides(dict);
        return dict.ToFrozenDictionary();
    }

    private static FrozenDictionary<Type, IWriteHandler> GetVerboseHandlers(FrozenDictionary<Type, IWriteHandler> handlers)
    {
        var dict = new Dictionary<Type, IWriteHandler>(handlers.Count);
        foreach (var item in handlers)
            dict[item.Key] = item.Value.GetVerboseHandler() ?? item.Value;
        return dict.ToFrozenDictionary();
    }

    public static IWriter<T> GetJsonInstance<T>(Stream output, IDictionary<Type, IWriteHandler>? customHandlers, bool verboseMode, bool ownsStream = true, IWriteHandler? defaultWriteHandler = null, Func<object, object>? transform = null)
    {
        var handlers = MergedHandlers(customHandlers);
        var bufferWriter = new ArrayBufferWriter<byte>();
        var jsonWriter = new Utf8JsonWriter(bufferWriter, new JsonWriterOptions
        {
            SkipValidation = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        });

        JsonEmitter emitter;
        if (verboseMode)
        {
            var verboseHandlers = GetVerboseHandlers(handlers);
            emitter = new JsonVerboseEmitter(jsonWriter, verboseHandlers, defaultWriteHandler, transform);
        }
        else
        {
            emitter = new JsonEmitter(jsonWriter, handlers, defaultWriteHandler, transform);
        }

        var wc = new WriteCache(!verboseMode);
        return new Writer<T>(output, emitter, wc, bufferWriter, ownsStream);
    }

    public static IWriter<T> GetMsgPackInstance<T>(Stream output, IDictionary<Type, IWriteHandler>? customHandlers, bool ownsStream = true, IWriteHandler? defaultWriteHandler = null, Func<object, object>? transform = null)
        => throw new NotImplementedException("MessagePack is not yet implemented.");

    private sealed class Writer<T> : IWriter<T>
    {
        private readonly Stream _output;
        private readonly JsonEmitter _emitter;
        private readonly WriteCache _wc;
        private readonly ArrayBufferWriter<byte> _buf;
        private readonly bool _ownsStream;
        private bool _disposed;

        public Writer(Stream output, JsonEmitter emitter, WriteCache wc, ArrayBufferWriter<byte> buf, bool ownsStream)
        {
            _output = output;
            _emitter = emitter;
            _wc = wc;
            _buf = buf;
            _ownsStream = ownsStream;
        }

        public void Write(T value)
        {
            _emitter.Emit(value!, false, _wc.Init());
            _emitter.JsonWriter.Flush();
            _output.Write(_buf.WrittenSpan);
            _buf.ResetWrittenCount();
            _output.Flush();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _emitter.JsonWriter.Dispose();
            if (_ownsStream)
                _output.Dispose();
        }
    }
}
