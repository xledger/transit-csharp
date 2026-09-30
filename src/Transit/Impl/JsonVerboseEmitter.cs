using System.Collections;
using System.Collections.Frozen;
using System.Text.Json;

namespace Transit.Net.Impl;

/// <summary>
/// JSON verbose emitter — writes maps as JSON objects and tags as map entries.
/// </summary>
internal class JsonVerboseEmitter : JsonEmitter
{
    public JsonVerboseEmitter(Utf8JsonWriter jsonWriter, FrozenDictionary<Type, IWriteHandler> handlers, IWriteHandler? defaultWriteHandler = null, Func<object, object>? transform = null)
        : base(jsonWriter, handlers, defaultWriteHandler, transform)
    {
    }

    protected override void WriteText(ReadOnlySpan<char> text, bool asDictionaryKey)
    {
        if (asDictionaryKey)
            JsonWriter.WritePropertyName(text);
        else
            JsonWriter.WriteStringValue(text);
    }

    // Verbose writes a tagged value as a one-entry map rather than a two-element list
    protected override void EmitTaggedStart(string t, WriteCache cache)
    {
        EmitDictionaryStart(1L);
        EmitString(Constants.EscTag, t, "", true, cache);
    }

    protected override void EmitTaggedEnd() => EmitDictionaryEnd();

    protected override void EmitDictionary(IDictionary dict, bool ignored, WriteCache cache)
    {
        EmitDictionaryStart(0);
        MarshalEntries(dict, cache);
        EmitDictionaryEnd();
    }
}
