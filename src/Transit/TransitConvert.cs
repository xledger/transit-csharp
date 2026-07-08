using System.Text;
using Transit.Net.Serialization;

namespace Transit.Net;

/// <summary>
/// Provides methods for converting between .NET types and Transit format.
/// </summary>
public static class TransitConvert
{
    /// <summary>
    /// Serializes the specified object to a Transit-encoded string.
    /// </summary>
    /// <typeparam name="T">The type of the object to serialize.</typeparam>
    /// <param name="value">The object to serialize.</param>
    /// <param name="format">The Transit format to use.</param>
    /// <param name="settings">The serialization settings.</param>
    /// <returns>A Transit-encoded string.</returns>
    public static string SerializeObject<T>(T value, TransitFactory.Format format, TransitSerializerSettings? settings = null)
    {
        using var ms = new MemoryStream();
        SerializeObject(value, ms, format, settings);
        return Encoding.UTF8.GetString(new ReadOnlySpan<byte>(ms.GetBuffer(), 0, (int)ms.Length));
    }

    /// <summary>
    /// Serializes the specified object to a stream in Transit format.
    /// </summary>
    public static void SerializeObject<T>(T value, Stream output, TransitFactory.Format format, TransitSerializerSettings? settings = null)
    {
        var useKeywords = settings?.UseKeywordKeys ?? false;
        var defaultHandler = settings?.DefaultWriteHandler ?? new ObjectSerializationWriteHandler(useKeywords);
        
        using var writer = TransitFactory.Writer<T>(format, output, 
            settings?.WriteHandlers, 
            defaultHandler, 
            ownsStream: false);
        
        writer.Write(value);
    }

    /// <summary>
    /// Deserializes the Transit-encoded string to the specified .NET type.
    /// </summary>
    public static T DeserializeObject<T>(string value, TransitFactory.Format format, TransitSerializerSettings? settings = null)
    {
        using var ms = new MemoryStream(Encoding.UTF8.GetBytes(value));
        return DeserializeObject<T>(ms, format, settings);
    }

    /// <summary>
    /// Deserializes the Transit-encoded stream to the specified .NET type.
    /// </summary>
    public static T DeserializeObject<T>(Stream stream, TransitFactory.Format format, TransitSerializerSettings? settings = null)
    {
        using var reader = TransitFactory.Reader(format, stream, 
            settings?.ReadHandlers, 
            settings?.DefaultReadHandler, 
            ownsStream: false);
        
        var result = reader.Read<object>();
        
        if (result == null) return default!;
        if (result is T typedResult) return typedResult;

        return (T)ObjectDeserializer.MapValue(result, typeof(T))!;
    }

    private class ObjectSerializationWriteHandler : IWriteHandler
    {
        private readonly bool _useKeywords;
        public ObjectSerializationWriteHandler(bool useKeywords) => _useKeywords = useKeywords;

        public string Tag(object obj)
        {
            if (obj == null) return "_";
            return ObjectSerializer.GetHandler(obj.GetType(), _useKeywords).Tag(obj);
        }

        public object Representation(object obj)
            => ObjectSerializer.GetHandler(obj.GetType(), _useKeywords).Representation(obj);

        public string? StringRepresentation(object obj) => null;

        public IWriteHandler? GetVerboseHandler() => null;
    }
}
