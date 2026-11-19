using System.Text.Json;
using Workflows.Abstraction.Enums;
using Workflows.Abstraction.Helpers;

namespace Workflows.Runner.Tests.Infrastructure
{
    /// <summary>
    /// Simple JSON-based object serializer for testing
    /// </summary>
    internal class TestObjectSerializer : IObjectSerializer
    {
        private readonly JsonSerializerOptions _options = new()
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = false,
            Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
        };

        public object Serialize(object obj, SerializationScope scope = SerializationScope.Standard)
        {
            if (obj == null) return null;
            return JsonSerializer.Serialize(obj, _options);
        }

        public object Deserialize(object serializedObj, Type type, SerializationScope scope = SerializationScope.Standard)
        {
            if (serializedObj == null) return null;
            var serialized = serializedObj as string ?? serializedObj.ToString();
            if (string.IsNullOrEmpty(serialized)) return null;
            return JsonSerializer.Deserialize(serialized, type, _options);
        }

        public TResult Deserialize<TResult>(object serializedObj, SerializationScope scope = SerializationScope.Standard)
        {
            if (serializedObj == null) return default;
            var serialized = serializedObj as string ?? serializedObj.ToString();
            if (string.IsNullOrEmpty(serialized)) return default;
            return JsonSerializer.Deserialize<TResult>(serialized, _options);
        }

        public void Serialize(object obj, System.IO.Stream stream, SerializationScope scope = SerializationScope.Standard)
        {
            if (obj == null) return;
            JsonSerializer.Serialize(stream, obj, _options);
        }

        public TResult Deserialize<TResult>(System.IO.Stream stream, SerializationScope scope = SerializationScope.Standard)
        {
            if (stream == null) return default;
            return JsonSerializer.Deserialize<TResult>(stream, _options);
        }

        public object Deserialize(System.IO.Stream stream, Type type, SerializationScope scope = SerializationScope.Standard)
        {
            if (stream == null) return null;
            return JsonSerializer.Deserialize(stream, type, _options);
        }
    }
}
