using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using Workflows.Definition;
using Workflows.Primitives;

namespace Workflows.Shared.Serialization
{
    public class DefinitionWaitJsonConverter : JsonConverter
    {
        public override bool CanConvert(Type objectType)
        {
            return objectType == typeof(Wait);
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

            using (var subReader = jsonObject.CreateReader())
            {
                return serializer.Deserialize(subReader, targetType);
            }
        }

        public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer)
        {
            serializer.Serialize(writer, value);
        }
    }
}
