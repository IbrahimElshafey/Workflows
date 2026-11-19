using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using Workflows.Abstraction.DTOs.Waits;
using Workflows.Primitives;

namespace Workflows.Shared.Serialization
{
    public class WaitDtoJsonConverter : JsonConverter
    {
        public override bool CanConvert(Type objectType)
        {
            return objectType == typeof(WaitInfrastructureDto);
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
                    targetType = typeof(CommandWaitDto);
                    break;
                case WaitType.SubWorkflowWait:
                    targetType = typeof(SubWorkflowWaitDto);
                    break;
                case WaitType.GroupWaitAll:
                case WaitType.GroupWaitFirst:
                case WaitType.GroupWaitWithExpression:
                    targetType = typeof(GroupWaitDto);
                    break;
                case WaitType.WaitMany:
                case WaitType.WaitAny:
                    targetType = typeof(ExternalGroupWaitDto);
                    break;
                case WaitType.Placeholder:
                    targetType = typeof(PlaceholderWaitDto);
                    break;
                case WaitType.PlaceholderSubWorkflow:
                    targetType = typeof(PlaceholderSubWorkflowWaitDto);
                    break;
                case WaitType.SignalWait:
                default:
                    if (jsonObject.ContainsKey("UniqueMatchId") || jsonObject.ContainsKey("ExecutionTime"))
                    {
                        targetType = typeof(TimeWaitDto);
                    }
                    else
                    {
                        targetType = typeof(SignalWaitDto);
                    }
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
