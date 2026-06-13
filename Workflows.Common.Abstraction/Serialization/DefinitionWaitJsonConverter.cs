using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using System;
using System.Collections.Generic;
using System.Linq;
using Workflows.Definition;
using Workflows.Primitives;

namespace Workflows.Shared.Serialization
{
    public class DefinitionWaitJsonConverter : JsonConverter
    {
        [ThreadStatic]
        private static HashSet<Type>? _activeTypes;

        private static HashSet<Type> GetActiveTypes()
        {
            return _activeTypes ??= new HashSet<Type>();
        }

        public override bool CanConvert(Type objectType)
        {
            var active = GetActiveTypes();
            bool isBypassed = active.Contains(objectType);
            bool assignable = typeof(Wait).IsAssignableFrom(objectType);
            // Console.WriteLine($"[CanConvert] Type: {objectType.FullName}, IsBypassed: {isBypassed}, Assignable: {assignable}, Active: [{string.Join(", ", active.Select(t => t.Name))}]");
            if (isBypassed)
            {
                return false;
            }
            return assignable;
        }

        public override object? ReadJson(JsonReader reader, Type objectType, object? existingValue, JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.Null) return null;

            var jsonObject = JObject.Load(reader);
            
            WaitType waitType = WaitType.SignalWait;
            if (jsonObject.TryGetValue("WaitType", out var waitTypeToken))
            {
                if (waitTypeToken.Type == JTokenType.Integer)
                {
                    waitType = (WaitType)waitTypeToken.Value<int>();
                }
                else if (waitTypeToken.Type == JTokenType.String)
                {
                    Enum.TryParse<WaitType>(waitTypeToken.Value<string>(), true, out waitType);
                }
            }

            Type targetType;
            if (objectType != typeof(Wait) && !objectType.IsAbstract)
            {
                targetType = objectType;
            }
            else
            {
                switch (waitType)
                {
                    case WaitType.Command:
                    case WaitType.Compensation:
                        targetType = typeof(CommandWait<object, object>);
                        break;
                    case WaitType.SubWorkflowWait:
                        targetType = typeof(SubWorkflowWait);
                        break;
                    case WaitType.GroupWaitAll:
                    case WaitType.GroupWaitFirst:
                    case WaitType.GroupWaitWithExpression:
                        targetType = typeof(GroupWait);
                        break;
                    case WaitType.SignalWait:
                    default:
                        targetType = typeof(SignalWait<object>);
                        break;
                }
            }

            var activeTypes = GetActiveTypes();
            bool added = activeTypes.Add(targetType);
            // Console.WriteLine($"[ReadJson] Start.ObjectType: {objectType.FullName}, TargetType: {targetType.FullName}, Added to active: {added}");
            try
            {
                using (var subReader = jsonObject.CreateReader())
                {
                    var res = ContractBypassingSerializer(serializer).Deserialize(subReader, targetType);
                    // Console.WriteLine($"[ReadJson] End. ObjectType: {objectType.FullName}, ResultType: {res?.GetType().FullName}");
                    return res;
                }
            }
            finally
            {
                if (added)
                {
                    activeTypes.Remove(targetType);
                }
            }
        }

        public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer)
        {
            if (value == null)
            {
                writer.WriteNull();
                return;
            }

            var type = value.GetType();
            var activeTypes = GetActiveTypes();
            bool added = activeTypes.Add(type);
            // Console.WriteLine($"[WriteJson] Start. ValueType: {type.FullName}, Added to active: {added}");
            try
            {
                ContractBypassingSerializer(serializer).Serialize(writer, value);
                // Console.WriteLine($"[WriteJson] End. ValueType: {type.FullName}");
            }
            finally
            {
                if (added)
                {
                    activeTypes.Remove(type);
                }
            }
        }

        private JsonSerializer? _bypassSerializer;
        private readonly object _lock = new object();

        private JsonSerializer ContractBypassingSerializer(JsonSerializer serializer)
        {
            lock (_lock)
            {
                if (_bypassSerializer == null)
                {
                    var settings = new JsonSerializerSettings
                    {
                        TypeNameHandling = TypeNameHandling.None,
                        NullValueHandling = serializer.NullValueHandling,
                        Formatting = serializer.Formatting,
                        ConstructorHandling = serializer.ConstructorHandling,
                        ObjectCreationHandling = serializer.ObjectCreationHandling,
                        ContractResolver = new WaitContractResolver(serializer.ContractResolver),
                        PreserveReferencesHandling = serializer.PreserveReferencesHandling,
                    };
                    foreach (var converter in serializer.Converters)
                    {
                        settings.Converters.Add(converter);
                    }
                    _bypassSerializer = JsonSerializer.Create(settings);
                }
                return _bypassSerializer;
            }
        }

        public class WaitContractResolver : DefaultContractResolver
        {
            private readonly IContractResolver? _parentResolver;

            public WaitContractResolver(IContractResolver? parentResolver)
            {
                _parentResolver = parentResolver;
            }

            protected override IList<JsonProperty> CreateProperties(Type type, MemberSerialization memberSerialization)
            {
                var props = base.CreateProperties(type, memberSerialization);

                if (typeof(Wait).IsAssignableFrom(type))
                {
                    props = props.Where(p =>
                    {
                        if (p.PropertyType != null && typeof(Delegate).IsAssignableFrom(p.PropertyType))
                        {
                            return false;
                        }
                        if (p.PropertyName == "ExplicitState" || p.PropertyName == "WorkflowContainer" || p.PropertyName == "CancelAction" || p.PropertyName == "AfterMatchAction")
                        {
                            return false;
                        }
                        return true;
                    }).ToList();

                    var nonPublicProps = type.GetProperties(System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    foreach (var propInfo in nonPublicProps)
                    {
                        if (typeof(Delegate).IsAssignableFrom(propInfo.PropertyType))
                        {
                            continue;
                        }
                        if (propInfo.Name == "ExplicitState" || propInfo.Name == "WorkflowContainer" || propInfo.Name == "CancelAction" || propInfo.Name == "AfterMatchAction" || propInfo.Name.Contains("."))
                        {
                            continue;
                        }
                        
                        var jsonProp = CreateProperty(propInfo, memberSerialization);
                        var hasSetter = propInfo.GetSetMethod(true) != null;
                        jsonProp.Writable = hasSetter;
                        jsonProp.Readable = true;
                        
                        if (!props.Any(p => p.PropertyName == jsonProp.PropertyName))
                        {
                            props.Add(jsonProp);
                        }
                    }
                }
                return props;
            }

            protected override JsonProperty CreateProperty(System.Reflection.MemberInfo member, MemberSerialization memberSerialization)
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
    }
}
