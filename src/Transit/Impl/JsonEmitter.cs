using System.Collections;
using System.Collections.Frozen;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace Transit.Net.Impl;

/// <summary>
/// JSON emitter using System.Text.Json Utf8JsonWriter for high-performance
/// binary-to-UTF8 output with no intermediate string allocations.
/// </summary>
internal class JsonEmitter : AbstractEmitter
{
    private static readonly long JsonIntMax = (long)Math.Pow(2, 53) - 1;
    private static readonly long JsonIntMin = -JsonIntMax;

    // Emitted once per map and too short to be cacheable, so it is pre-encoded here instead.
    private static readonly JsonEncodedText DirectoryAsList = JsonEncodedText.Encode(Constants.DirectoryAsList);

    internal readonly Utf8JsonWriter JsonWriter;

    public JsonEmitter(Utf8JsonWriter jsonWriter, FrozenDictionary<Type, IWriteHandler> handlers, IWriteHandler? defaultWriteHandler = null, Func<object, object>? transform = null)
        : base(handlers, defaultWriteHandler, transform)
    {
        JsonWriter = jsonWriter;
    }

    public override void Emit(object obj, bool asDictionaryKey, WriteCache cache)
        => MarshalTop(obj, cache);

    public override void EmitNull(bool asDictionaryKey, WriteCache cache)
    {
        if (asDictionaryKey)
            EmitString(Constants.EscStr, "_", "", asDictionaryKey, cache);
        else
            JsonWriter.WriteNullValue();
    }

    /// <summary>Longest prefix+tag+value composed on the stack rather than the heap.</summary>
    private const int MaxComposedLength = 256;

    public override void EmitString(string? prefix, string? tag, string s, bool asDictionaryKey, WriteCache cache)
    {
        // A value already in the cache needs neither composing nor allocating.
        if (cache.TryGetCode(prefix, tag, s, asDictionaryKey, out var code))
        {
            WriteText(code, asDictionaryKey);
            return;
        }

        var plen = prefix?.Length ?? 0;
        var tlen = tag?.Length ?? 0;
        if (plen + tlen == 0)
        {
            WriteText(s, asDictionaryKey);
            return;
        }

        WriteComposed(prefix, tag, s, asDictionaryKey);
    }

    // Kept out of line so the stackalloc does not stop EmitString being inlined.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private void WriteComposed(string? prefix, string? tag, string s, bool asDictionaryKey)
    {
        var plen = prefix!.Length;
        var tlen = tag?.Length ?? 0;
        var len = plen + tlen + s.Length;
        if (len > MaxComposedLength)
        {
            WriteText(Util.MaybePrefix(prefix, tag, s), asDictionaryKey);
            return;
        }

        Span<char> span = stackalloc char[len];
        prefix.AsSpan().CopyTo(span);
        tag.AsSpan().CopyTo(span[plen..]);
        s.AsSpan().CopyTo(span[(plen + tlen)..]);
        WriteText(span, asDictionaryKey);
    }

    protected virtual void WriteText(ReadOnlySpan<char> text, bool asDictionaryKey)
        => JsonWriter.WriteStringValue(text);

    public override void EmitBoolean(bool b, bool asDictionaryKey, WriteCache cache)
    {
        if (asDictionaryKey)
            EmitString(Constants.EscStr, "?", b ? "t" : "f", asDictionaryKey, cache);
        else
            JsonWriter.WriteBooleanValue(b);
    }

    public override void EmitInteger(object i, bool asDictionaryKey, WriteCache cache)
        => EmitInteger(Util.NumberToPrimitiveLong(i), asDictionaryKey, cache);

    public override void EmitInteger(long i, bool asDictionaryKey, WriteCache cache)
    {
        if (asDictionaryKey || i > JsonIntMax || i < JsonIntMin)
            EmitString(Constants.EscStr, "i", i.ToString(CultureInfo.InvariantCulture), asDictionaryKey, cache);
        else
            JsonWriter.WriteNumberValue(i);
    }

    public override void EmitDouble(object d, bool asDictionaryKey, WriteCache cache)
    {
        if (d is double dbl) EmitDouble(dbl, asDictionaryKey, cache);
        else if (d is float flt) EmitDouble(flt, asDictionaryKey, cache);
        else throw new TransitException("Unknown double type: " + d.GetType());
    }

    public override void EmitDouble(float d, bool asDictionaryKey, WriteCache cache)
    {
        if (asDictionaryKey)
            EmitString(Constants.EscStr, "d", d.ToString(CultureInfo.InvariantCulture), asDictionaryKey, cache);
        else
            JsonWriter.WriteNumberValue(d);
    }

    public override void EmitDouble(double d, bool asDictionaryKey, WriteCache cache)
    {
        if (asDictionaryKey)
            EmitString(Constants.EscStr, "d", d.ToString(CultureInfo.InvariantCulture), asDictionaryKey, cache);
        else
            JsonWriter.WriteNumberValue(d);
    }

    public override void EmitBinary(object b, bool asDictionaryKey, WriteCache cache)
        => EmitString(Constants.EscStr, "b", Convert.ToBase64String((byte[])b), asDictionaryKey, cache);

    public override void EmitListStart(long size) => JsonWriter.WriteStartArray();
    public override void EmitListEnd() => JsonWriter.WriteEndArray();
    public override void EmitDictionaryStart(long size) => JsonWriter.WriteStartObject();
    public override void EmitDictionaryEnd() => JsonWriter.WriteEndObject();

    public override void FlushWriter() => JsonWriter.Flush();
    public override bool PrefersStrings() => true;

    protected override void EmitDictionary(IDictionary dict, bool ignored, WriteCache cache)
    {
        EmitListStart(0);
        JsonWriter.WriteStringValue(DirectoryAsList);

        MarshalEntries(dict, cache);

        EmitListEnd();
    }
}
