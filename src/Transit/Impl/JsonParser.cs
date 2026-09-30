using System.Buffers;
using System.Collections.Frozen;
using System.Text.Json;

namespace Transit.Net.Impl;

/// <summary>
/// JSON parser using System.Text.Json Utf8JsonReader for streaming, zero-allocation parsing.
/// The Utf8JsonReader is a ref struct, so it is passed by ref through all parsing methods.
/// </summary>
internal sealed class JsonParser : AbstractParser
{
    private readonly ReadOnlySequence<byte> _data;

    public JsonParser(
        ReadOnlySequence<byte> data,
        FrozenDictionary<string, IReadHandler> handlers,
        IDefaultReadHandler<object>? defaultHandler,
        IDictionaryReader dictionaryBuilder,
        IListReader listBuilder)
        : base(handlers, defaultHandler, dictionaryBuilder, listBuilder)
    {
        _data = data;
    }

    /// <summary>
    /// Formats a DateTime for verbose transit encoding.
    /// </summary>
    public new static string FormatDateTime(DateTime value) => AbstractParser.FormatDateTime(value);

    public override object? Parse(ReadCache cache)
    {
        var reader = new Utf8JsonReader(_data, new JsonReaderOptions
        {
            AllowTrailingCommas = true
        });
        reader.Read(); // Advance to first token
        return ParseValue(ref reader, false, cache);
    }

    public override object? ParseVal(bool asDictionaryKey, ReadCache cache)
    {
        var reader = new Utf8JsonReader(_data, new JsonReaderOptions
        {
            AllowTrailingCommas = true
        });
        reader.Read();
        return ParseValue(ref reader, asDictionaryKey, cache);
    }

    private object? ParseValue(ref Utf8JsonReader reader, bool asDictionaryKey, ReadCache cache)
    {
        return reader.TokenType switch
        {
            JsonTokenType.Number => ParseNumber(ref reader),
            JsonTokenType.String => ReadString(ref reader, asDictionaryKey, cache),
            JsonTokenType.True => true,
            JsonTokenType.False => false,
            JsonTokenType.Null => null,
            JsonTokenType.StartArray => ParseArray(ref reader, asDictionaryKey, cache, null),
            JsonTokenType.StartObject => ParseObject(ref reader, false, cache, null),
            _ => null,
        };
    }

    /// <summary>
    /// A substitution code is resolved from the reader's raw UTF-8 where possible, since
    /// materialising the two or three characters only to look them up is pure waste.
    /// </summary>
    private object? ReadString(ref Utf8JsonReader reader, bool asDictionaryKey, ReadCache cache)
    {
        if (!reader.HasValueSequence && !reader.ValueIsEscaped
            && cache.TryReadCode(reader.ValueSpan, out var cached))
            return cached;

        return cache.CacheRead(reader.GetString()!, asDictionaryKey, this);
    }

    private static object ParseNumber(ref Utf8JsonReader reader)
    {
        if (reader.TryGetInt64(out var l))
            return l;
        if (reader.TryGetDouble(out var d))
            return d;
        return reader.GetDecimal();
    }

    public override object? ParseDictionary(bool ignored, ReadCache cache, IDictionaryReadHandler? handler)
    {
        var reader = new Utf8JsonReader(_data, new JsonReaderOptions
        {
            AllowTrailingCommas = true
        });
        reader.Read();
        return ParseObject(ref reader, ignored, cache, handler);
    }

    private object? ParseObject(ref Utf8JsonReader reader, bool ignored, ReadCache cache, IDictionaryReadHandler? handler)
    {
        // reader.TokenType should be StartObject
        var dr = handler?.DictionaryReader() ?? DictionaryBuilder;
        var d = dr.Init();

        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            // TokenType should be PropertyName
            object key;
            if (!reader.HasValueSequence && !reader.ValueIsEscaped
                && cache.TryReadCode(reader.ValueSpan, out var cachedKey))
            {
                reader.Read(); // Advance to value
                key = cachedKey;
            }
            else
            {
                var propName = reader.GetString()!;
                reader.Read(); // Advance to value
                key = cache.CacheRead(propName, true, this);
            }
            if (key is Tag tag)
            {
                var tagStr = tag.GetValue();
                object? result;
                if (TryGetHandler(tagStr, out var valHandler) && valHandler != null)
                {
                    if (reader.TokenType == JsonTokenType.StartObject && valHandler is IDictionaryReadHandler dictHandler)
                        result = ParseObject(ref reader, false, cache, dictHandler);
                    else if (reader.TokenType == JsonTokenType.StartArray && valHandler is IListReadHandler listHandler)
                        result = ParseArray(ref reader, false, cache, listHandler);
                    else
                        result = valHandler.FromRepresentation(ParseValue(ref reader, false, cache)!);
                }
                else
                {
                    result = Decode(tagStr, ParseValue(ref reader, false, cache)!);
                }
                // Consume the EndObject of this tagged-value object
                SkipToEndObject(ref reader);
                return result;
            }
            else
            {
                d = dr.Add(d, key, ParseValue(ref reader, false, cache)!);
            }
        }

        return dr.Complete(d);
    }

    private static void SkipToEndObject(ref Utf8JsonReader reader)
    {
        // The reader should be positioned just after the value inside a single-property object.
        // Consume tokens until we hit the matching EndObject.
        int depth = 1;
        while (depth > 0 && reader.Read())
        {
            if (reader.TokenType == JsonTokenType.StartObject)
                depth++;
            else if (reader.TokenType == JsonTokenType.EndObject)
                depth--;
        }
    }

    public override object? ParseList(bool asDictionaryKey, ReadCache cache, IListReadHandler? handler)
    {
        var reader = new Utf8JsonReader(_data, new JsonReaderOptions
        {
            AllowTrailingCommas = true
        });
        reader.Read();
        return ParseArray(ref reader, asDictionaryKey, cache, handler);
    }

    private object? ParseArray(ref Utf8JsonReader reader, bool asDictionaryKey, ReadCache cache, IListReadHandler? handler)
    {
        // reader.TokenType should be StartArray
        if (!reader.Read() || reader.TokenType == JsonTokenType.EndArray)
        {
            // Empty array
            var lr2 = handler?.ListReader() ?? ListBuilder;
            return lr2.Complete(lr2.Init());
        }

        // Check the first element for the map-as-array marker
        if (reader.TokenType == JsonTokenType.String
            && reader.GetString() == Constants.DirectoryAsList)
        {
            return ParseArrayAsDict(ref reader, cache, null);
        }

        var firstVal = ParseValue(ref reader, false, cache);

        if (firstVal is Tag firstTag)
        {
            if (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
            {
                var tagStr = firstTag.GetValue();
                if (TryGetHandler(tagStr, out var valHandler) && valHandler != null)
                {
                    object? val;
                    if (reader.TokenType == JsonTokenType.StartObject && valHandler is IDictionaryReadHandler dictHandler)
                        val = ParseObject(ref reader, false, cache, dictHandler);
                    else if (reader.TokenType == JsonTokenType.StartArray && valHandler is IDictionaryReadHandler dictHandler2)
                    {
                        // Map-as-array encoding (["^ ", ...]) fed to a dict handler
                        var parsed = ParseArray(ref reader, false, cache, null);
                        if (parsed is System.Collections.IDictionary parsedDict)
                        {
                            var dr = dictHandler2.DictionaryReader();
                            var result = dr.Init();
                            foreach (System.Collections.DictionaryEntry entry in parsedDict)
                                result = dr.Add(result, entry.Key, entry.Value!);
                            val = dr.Complete(result);
                        }
                        else
                        {
                            val = valHandler.FromRepresentation(parsed!);
                        }
                    }
                    else if (reader.TokenType == JsonTokenType.StartArray && valHandler is IListReadHandler listHandler)
                        val = ParseArray(ref reader, false, cache, listHandler);
                    else
                        val = valHandler.FromRepresentation(ParseValue(ref reader, false, cache)!);

                    // Skip remaining elements in the array (should just be EndArray)
                    SkipToEndArray(ref reader);
                    return val;
                }

                var decoded = Decode(tagStr, ParseValue(ref reader, false, cache)!);
                SkipToEndArray(ref reader);
                return decoded;
            }
        }

        // Normal list
        var lr = handler?.ListReader() ?? ListBuilder;
        var l = lr.Init();
        l = lr.Add(l, firstVal!);

        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            l = lr.Add(l, ParseValue(ref reader, false, cache)!);
        }
        return lr.Complete(l);
    }

    private object? ParseArrayAsDict(ref Utf8JsonReader reader, ReadCache cache, IDictionaryReadHandler? handler)
    {
        // We've already read the "^ " marker string. Now read key-value pairs.
        var dr = handler?.DictionaryReader() ?? DictionaryBuilder;
        var d = dr.Init();

        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            var key = ParseValue(ref reader, true, cache);
            if (key is Tag tag)
            {
                if (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                {
                    var tagStr = tag.GetValue();
                    if (TryGetHandler(tagStr, out var valHandler) && valHandler != null)
                    {
                        object? val;
                        if (reader.TokenType == JsonTokenType.StartObject && valHandler is IDictionaryReadHandler dictHandler)
                            val = ParseObject(ref reader, false, cache, dictHandler);
                        else if (reader.TokenType == JsonTokenType.StartArray && valHandler is IListReadHandler listHandler)
                            val = ParseArray(ref reader, false, cache, listHandler);
                        else
                            val = valHandler.FromRepresentation(ParseValue(ref reader, false, cache)!);

                        // Skip to end of surrounding array
                        SkipToEndArray(ref reader);
                        return val;
                    }
                    return Decode(tagStr, ParseValue(ref reader, false, cache)!);
                }
            }
            else
            {
                if (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                {
                    d = dr.Add(d, key!, ParseValue(ref reader, false, cache)!);
                }
            }
        }

        return dr.Complete(d);
    }

    private static void SkipToEndArray(ref Utf8JsonReader reader)
    {
        int depth = 1;
        while (depth > 0 && reader.Read())
        {
            if (reader.TokenType == JsonTokenType.StartArray || reader.TokenType == JsonTokenType.StartObject)
                depth++;
            else if (reader.TokenType == JsonTokenType.EndArray || reader.TokenType == JsonTokenType.EndObject)
                depth--;
        }
    }
}
