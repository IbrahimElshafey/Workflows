using System;
using Workflows.Common.Abstraction;

namespace Workflows.Common
{
    public class JsonSerializer : IObjectSerializer
    {
        public T Deserialize<T>(string serializedObj, SerializationScope scope = SerializationScope.Standard)
        {
            throw new NotImplementedException();
        }

        public object Deserialize(string serializedObj, Type type, SerializationScope scope = SerializationScope.Standard)
        {
            throw new NotImplementedException();
        }

        public string Serialize(object obj, SerializationScope scope = SerializationScope.Standard)
        {
            throw new NotImplementedException();
        }
    }
}
