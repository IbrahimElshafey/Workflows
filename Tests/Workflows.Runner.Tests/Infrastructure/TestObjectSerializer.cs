using System;
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
            WriteIndented = false
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
    }
}
