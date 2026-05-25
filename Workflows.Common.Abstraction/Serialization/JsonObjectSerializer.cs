using System;
using Newtonsoft.Json;
using Workflows.Abstraction.Enums;
using Workflows.Abstraction.Helpers;

namespace Workflows.Shared.Serialization
{
    public class JsonObjectSerializer : IObjectSerializer
    {
        private static readonly JsonSerializerSettings StandardSettings = new JsonSerializerSettings
        {
            TypeNameHandling = TypeNameHandling.None,
            NullValueHandling = NullValueHandling.Ignore,
            Formatting = Formatting.None,
            ContractResolver = new PrivateSetterContractResolver(),
            Converters = { new Newtonsoft.Json.Converters.StringEnumConverter() }
        };

        private static readonly JsonSerializerSettings StateSettings = new JsonSerializerSettings
        {
            TypeNameHandling = TypeNameHandling.All,
            NullValueHandling = NullValueHandling.Ignore,
            Formatting = Formatting.None,
            ConstructorHandling = ConstructorHandling.AllowNonPublicDefaultConstructor,
            ObjectCreationHandling = ObjectCreationHandling.Replace,
            ContractResolver = new PrivateSetterContractResolver(),
            PreserveReferencesHandling = PreserveReferencesHandling.Objects,
            Converters = { new Newtonsoft.Json.Converters.StringEnumConverter() }
        };

        public class PrivateSetterContractResolver : Newtonsoft.Json.Serialization.DefaultContractResolver
        {
            protected override Newtonsoft.Json.Serialization.JsonProperty CreateProperty(
                System.Reflection.MemberInfo member, 
                Newtonsoft.Json.MemberSerialization memberSerialization)
            {
                var prop = base.CreateProperty(member, memberSerialization);
                if (!prop.Writable)
                {
                    var property = member as System.Reflection.PropertyInfo;
                    if (property != null)
                    {
                        var hasPrivateSetter = property.GetSetMethod(true) != null;
                        prop.Writable = hasPrivateSetter;
                    }
                }
                return prop;
            }
        }

        private static JsonSerializerSettings GetSettings(SerializationScope scope)
        {
            return scope == SerializationScope.Standard ? StandardSettings : StateSettings;
        }

        public T Deserialize<T>(object serializedObj, SerializationScope scope = SerializationScope.Standard)
        {
            if (serializedObj == null) return default;
            var serialized = serializedObj as string ?? serializedObj.ToString();
            if (string.IsNullOrEmpty(serialized)) return default;

            return JsonConvert.DeserializeObject<T>(serialized, GetSettings(scope));
        }

        public object Deserialize(object serializedObj, Type type, SerializationScope scope = SerializationScope.Standard)
        {
            if (serializedObj == null) return null;
            var serialized = serializedObj as string ?? serializedObj.ToString();
            if (string.IsNullOrEmpty(serialized)) return null;

            return JsonConvert.DeserializeObject(serialized, type, GetSettings(scope));
        }

        public object Serialize(object obj, SerializationScope scope = SerializationScope.Standard)
        {
            if (obj == null) return null;
            return JsonConvert.SerializeObject(obj, GetSettings(scope));
        }
    }
}
