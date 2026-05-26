using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
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
            Converters = { new Newtonsoft.Json.Converters.StringEnumConverter(), new ObjectIntConverter() }
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
            Converters = { new Newtonsoft.Json.Converters.StringEnumConverter(), new ObjectIntConverter() }
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

    public class ObjectIntConverter : JsonConverter
    {
        public override bool CanConvert(Type objectType)
        {
            return objectType == typeof(object);
        }

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.Integer)
            {
                var value = reader.Value;
                if (value is long l && l >= int.MinValue && l <= int.MaxValue)
                {
                    return (int)l;
                }
                return value;
            }

            var token = JToken.Load(reader);
            return ConvertToken(token, serializer);
        }

        private object ConvertToken(JToken token, JsonSerializer serializer)
        {
            if (token == null) return null;

            switch (token.Type)
            {
                case JTokenType.Integer:
                    var val = ((JValue)token).Value;
                    if (val is long l && l >= int.MinValue && l <= int.MaxValue)
                    {
                        return (int)l;
                    }
                    return val;

                case JTokenType.Float:
                case JTokenType.String:
                case JTokenType.Boolean:
                case JTokenType.Null:
                case JTokenType.Date:
                case JTokenType.Bytes:
                case JTokenType.Guid:
                case JTokenType.Uri:
                case JTokenType.TimeSpan:
                    return ((JValue)token).Value;

                case JTokenType.Array:
                    var list = new List<object>();
                    foreach (var child in token.Children())
                    {
                        list.Add(ConvertToken(child, serializer));
                    }
                    return list;

                case Newtonsoft.Json.Linq.JTokenType.Object:
                    var jobj = (JObject)token;
                    if (jobj.Property("$type") != null)
                    {
                        using (var subReader = jobj.CreateReader())
                        {
                            return serializer.Deserialize(subReader);
                        }
                    }
                    var dict = new Dictionary<string, object>();
                    foreach (var prop in jobj.Properties())
                    {
                        dict[prop.Name] = ConvertToken(prop.Value, serializer);
                    }
                    return dict;

                default:
                    return token.ToObject<object>(serializer);
            }
        }

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            throw new NotImplementedException();
        }

        public override bool CanWrite => false;
    }
}
