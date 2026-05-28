using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Workflows.Abstraction.DTOs;
using Workflows.Abstraction.Runner;

namespace Workflows.Shared.Serialization
{
    public class WorkflowStateObjectJsonConverter : JsonConverter
    {
        public override bool CanConvert(Type objectType)
        {
            return objectType == typeof(WorkflowStateObject);
        }

        public override object? ReadJson(JsonReader reader, Type objectType, object? existingValue, JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.Null) return null;

            var jsonObject = JObject.Load(reader);

            var stateObj = new WorkflowStateObject();

            // 1. Read simple properties
            if (jsonObject.TryGetValue("StateIndex", out var stateIndexToken))
            {
                stateObj.StateIndex = stateIndexToken.Value<int>();
            }

            if (jsonObject.TryGetValue("WorkflowType", out var workflowTypeToken))
            {
                stateObj.WorkflowType = workflowTypeToken.Value<string>();
            }

            if (jsonObject.TryGetValue("SubWorkflowMethod", out var subWorkflowMethodToken))
            {
                stateObj.SubWorkflowMethod = subWorkflowMethodToken.Value<string>();
            }

            // 2. Deserialize Instance if present
            Type? containerType = null;
            if (jsonObject.TryGetValue("Instance", out var instanceToken) && instanceToken.Type != JTokenType.Null)
            {
                if (!string.IsNullOrEmpty(stateObj.WorkflowType))
                {
                    var registry = WorkflowRegistryLocator.Current;
                    if (registry != null && registry.Workflows.TryGetValue(stateObj.WorkflowType, out var tuple))
                    {
                        containerType = tuple.WorkflowContainer;
                    }
                }

                if (containerType != null)
                {
                    using (var subReader = instanceToken.CreateReader())
                    {
                        stateObj.Instance = ContractBypassingSerializer(serializer).Deserialize(subReader, containerType);
                    }
                }
                else
                {
                    stateObj.Instance = instanceToken.ToObject<object>(serializer);
                }
            }

            // 3. Deserialize StateMachinesObjects
            if (jsonObject.TryGetValue("StateMachinesObjects", out var stateMachinesToken) && stateMachinesToken.Type != JTokenType.Null)
            {
                var dict = new Dictionary<string, object>();
                var stateMachinesJson = (JObject)stateMachinesToken;
                foreach (var prop in stateMachinesJson.Properties())
                {
                    if (prop.Name == "$id" || prop.Name == "$ref" || prop.Name == "$type")
                    {
                        continue;
                    }

                    if (prop.Value.Type == JTokenType.Null)
                    {
                        dict[prop.Name] = null;
                        continue;
                    }

                    if (prop.Name == "root")
                    {
                        Type? stateMachineType = null;
                        var registry = WorkflowRegistryLocator.Current;
                        if (!string.IsNullOrEmpty(stateObj.WorkflowType))
                        {
                            if (string.IsNullOrEmpty(stateObj.SubWorkflowMethod))
                            {
                                if (registry != null && registry.Workflows.TryGetValue(stateObj.WorkflowType, out var tuple))
                                {
                                    stateMachineType = tuple.WorkflowStateMachine;
                                }
                            }
                            else
                            {
                                // Resolve sub-workflow method
                                MethodInfo? methodInfo = null;
                                if (registry != null && registry.Workflows.TryGetValue(stateObj.WorkflowType, out var tuple))
                                {
                                    methodInfo = tuple.WorkflowContainer.GetMethod(
                                        stateObj.SubWorkflowMethod,
                                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                                }

                                if (methodInfo == null)
                                {
                                    // Fallback: search assemblies
                                    methodInfo = ResolveMethod($"{stateObj.WorkflowType}.{stateObj.SubWorkflowMethod}");
                                }

                                if (methodInfo != null)
                                {
                                    var stateMachineAttribute = methodInfo.GetCustomAttribute<System.Runtime.CompilerServices.AsyncStateMachineAttribute>();
                                    if (stateMachineAttribute != null)
                                    {
                                        stateMachineType = stateMachineAttribute.StateMachineType;
                                    }
                                    else
                                    {
                                        var asyncIteratorAttribute = methodInfo.GetCustomAttribute<System.Runtime.CompilerServices.AsyncIteratorStateMachineAttribute>();
                                        if (asyncIteratorAttribute != null)
                                        {
                                            stateMachineType = asyncIteratorAttribute.StateMachineType;
                                        }
                                    }
                                }
                            }
                        }

                        var stateMachineObj = new StateMachineObject();
                        if (prop.Value is JObject propJson)
                        {
                            if (propJson.TryGetValue("$state", out var stVal))
                            {
                                stateMachineObj.StateIndex = stVal.Value<int>();
                            }
                            if (propJson.TryGetValue("Instance", out var instVal))
                            {
                                if (containerType != null)
                                {
                                    using (var subReader = instVal.CreateReader())
                                    {
                                        stateMachineObj.Instance = ContractBypassingSerializer(serializer).Deserialize(subReader, containerType);
                                    }
                                }
                                else
                                {
                                    stateMachineObj.Instance = instVal.ToObject<object>(serializer);
                                }
                            }

                            var fields = stateMachineType?.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                            foreach (var fieldProp in propJson.Properties())
                            {
                                if (fieldProp.Name == "$state" || fieldProp.Name == "Instance" || fieldProp.Name == "$this" ||
                                    fieldProp.Name == "$id" || fieldProp.Name == "$ref" || fieldProp.Name == "$type")
                                {
                                    continue;
                                }

                                var field = fields?.FirstOrDefault(f => GetCleanFieldName(f.Name) == fieldProp.Name);
                                if (field != null)
                                {
                                    using (var subReader = fieldProp.Value.CreateReader())
                                    {
                                        stateMachineObj[fieldProp.Name] = ContractBypassingSerializer(serializer).Deserialize(subReader, field.FieldType);
                                    }
                                }
                                else
                                {
                                    stateMachineObj[fieldProp.Name] = fieldProp.Value.ToObject<object>(serializer);
                                }
                            }
                        }

                        dict[prop.Name] = stateMachineObj;
                    }
                    else
                    {
                        using (var subReader = prop.Value.CreateReader())
                        {
                            dict[prop.Name] = serializer.Deserialize<WorkflowStateObject>(subReader);
                        }
                    }
                }
                stateObj.StateMachinesObjects = dict;
            }

            // 4. Deserialize WaitStatesObjects
            if (jsonObject.TryGetValue("WaitStatesObjects", out var waitStatesToken) && waitStatesToken.Type != JTokenType.Null)
            {
                var dict = new Dictionary<Guid, object>();
                var waitStatesJson = (JObject)waitStatesToken;
                foreach (var prop in waitStatesJson.Properties())
                {
                    if (Guid.TryParse(prop.Name, out var guid))
                    {
                        dict[guid] = prop.Value;
                    }
                }
                stateObj.WaitStatesObjects = dict;
            }

            return stateObj;
        }

        public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer)
        {
            ContractBypassingSerializer(serializer).Serialize(writer, value);
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
                        ContractResolver = serializer.ContractResolver,
                        PreserveReferencesHandling = serializer.PreserveReferencesHandling,
                    };
                    foreach (var converter in serializer.Converters)
                    {
                        if (converter != this)
                        {
                            settings.Converters.Add(converter);
                        }
                    }
                    _bypassSerializer = JsonSerializer.Create(settings);
                }
                return _bypassSerializer;
            }
        }

        private static string GetCleanFieldName(string fieldName)
        {
            if (fieldName.StartsWith('<') && fieldName.Contains(">5__"))
            {
                int endIdx = fieldName.IndexOf('>');
                if (endIdx > 1)
                {
                    return fieldName.Substring(1, endIdx - 1);
                }
            }
            return fieldName;
        }

        private static MethodInfo? ResolveMethod(string methodFullPath)
        {
            if (string.IsNullOrWhiteSpace(methodFullPath))
                return null;

            int lastDot = methodFullPath.LastIndexOf('.');
            if (lastDot == -1)
                return null;

            string typeName = methodFullPath.Substring(0, lastDot);
            string methodName = methodFullPath.Substring(lastDot + 1);

            Type? type = Type.GetType(typeName);
            if (type == null)
            {
                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    type = assembly.GetType(typeName);
                    if (type != null)
                        break;
                }
            }

            if (type == null)
                return null;

            return type.GetMethod(methodName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance);
        }
    }
}
