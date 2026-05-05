using System;

namespace Workflows.Shared.Serialization
{
    public class JsonObjectSerializer : IObjectSerializer
    {
        public T Deserialize<T>(object serializedObj, SerializationScope scope = SerializationScope.Standard)
        {
            throw new NotImplementedException();
        }

        public object Deserialize(object serializedObj, Type type, SerializationScope scope = SerializationScope.Standard)
        {
            throw new NotImplementedException();
        }

        public object Serialize(object obj, SerializationScope scope = SerializationScope.Standard)
        {
            throw new NotImplementedException();
        }
    }
}
