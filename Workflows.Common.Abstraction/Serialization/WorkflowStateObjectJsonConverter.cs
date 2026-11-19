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
            var stateIndexToken = jsonObject.GetValue("StateIndex", StringComparison.OrdinalIgnoreCase);
            if (stateIndexToken != null && stateIndexToken.Type != JTokenType.Null)
            {
                stateObj.StateIndex = stateIndexToken.Value<int>();
            }

            var workflowTypeToken = jsonObject.GetValue("WorkflowType", StringComparison.OrdinalIgnoreCase);
            if (workflowTypeToken != null && workflowTypeToken.Type != JTokenType.Null)
            {
                stateObj.WorkflowType = workflowTypeToken.Value<string>();
            }

            var subWorkflowMethodToken = jsonObject.GetValue("SubWorkflowMethod", StringComparison.OrdinalIgnoreCase);
            if (subWorkflowMethodToken != null && subWorkflowMethodToken.Type != JTokenType.Null)
            {
                stateObj.SubWorkflowMethod = subWorkflowMethodToken.Value<string>();
            }

            // 2. Deserialize Instance if present
            Type? containerType = null;
            var instanceToken = jsonObject.GetValue("Instance", StringComparison.OrdinalIgnoreCase);
            if (instanceToken != null && instanceToken.Type != JTokenType.Null)
            {
                if (!string.IsNullOrEmpty(stateObj.WorkflowType))
                {
                    if (global::Workflows.Definition.Registration.WorkflowDefinitionRegistry.Workflows.TryGetValue(stateObj.WorkflowType, out var tuple))
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

            // 3. Deserialize Locals
            var localsToken = jsonObject.GetValue("Locals", StringComparison.OrdinalIgnoreCase);
            if (localsToken != null && localsToken.Type != JTokenType.Null)
            {
                var dict = new Dictionary<string, object>();
                var localsJson = (JObject)localsToken;

                object? statePoco = null;
                var stateToken = localsJson.GetValue("state", StringComparison.OrdinalIgnoreCase);
                if (stateToken != null && stateToken.Type != JTokenType.Null)
                {
                    Type? stateType = null;
                    if (!string.IsNullOrEmpty(stateObj.WorkflowType))
                    {
                        if (global::Workflows.Definition.Registration.WorkflowDefinitionRegistry.Workflows.TryGetValue(stateObj.WorkflowType, out var tuple))
                        {
                            if (string.IsNullOrEmpty(stateObj.SubWorkflowMethod))
                            {
                                stateType = tuple.StateType;
                            }
                            else
                            {
                                var subMethod = tuple.WorkflowContainer.GetMethod(
                                    stateObj.SubWorkflowMethod,
                                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                                if (subMethod == null)
                                {
                                    throw new InvalidOperationException($"Sub-workflow method '{stateObj.SubWorkflowMethod}' not found on '{tuple.WorkflowContainer.FullName}'.");
                                }
                                stateType = subMethod.GetParameters()[0].ParameterType;
                            }
                        }
                    }

                    if (stateType != null && stateType != typeof(object))
                    {
                        using (var subReader = stateToken.CreateReader())
                        {
                            statePoco = ContractBypassingSerializer(serializer).Deserialize(subReader, stateType);
                        }
                    }
                    else
                    {
                        statePoco = stateToken.ToObject<object>(serializer);
                    }
                    dict["state"] = statePoco;
                }

                foreach (var prop in localsJson.Properties())
                {
                    if (prop.Name == "state") continue;

                    if (prop.Value.Type == JTokenType.Null)
                    {
                        dict[prop.Name] = null;
                        continue;
                    }

                    if (prop.Name == "root")
                    {
                        Type? stateMachineType = null;
                        if (!string.IsNullOrEmpty(stateObj.WorkflowType))
                        {
                            if (string.IsNullOrEmpty(stateObj.SubWorkflowMethod))
                            {
                                if (global::Workflows.Definition.Registration.WorkflowDefinitionRegistry.Workflows.TryGetValue(stateObj.WorkflowType, out var tuple))
                                {
                                    stateMachineType = tuple.WorkflowStateMachine;
                                }
                            }
                            else
                            {
                                // Resolve sub-workflow method
                                MethodInfo? methodInfo = null;
                                if (global::Workflows.Definition.Registration.WorkflowDefinitionRegistry.Workflows.TryGetValue(stateObj.WorkflowType, out var tuple))
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
                            var stVal = propJson.GetValue("$state", StringComparison.OrdinalIgnoreCase);
                            if (stVal != null && stVal.Type != JTokenType.Null)
                            {
                                stateMachineObj.StateIndex = stVal.Value<int>();
                            }
                            var instVal = propJson.GetValue("Instance", StringComparison.OrdinalIgnoreCase);
                            if (instVal != null && instVal.Type != JTokenType.Null)
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
                        }

                        dict[prop.Name] = stateMachineObj;
                    }
                    else if (prop.Value is JObject propObj && 
                             (propObj.GetValue("WorkflowType", StringComparison.OrdinalIgnoreCase) != null || 
                              propObj.GetValue("Locals", StringComparison.OrdinalIgnoreCase) != null || 
                              propObj.GetValue("SubWorkflowMethod", StringComparison.OrdinalIgnoreCase) != null))
                    {
                        using (var subReader = prop.Value.CreateReader())
                        {
                            dict[prop.Name] = serializer.Deserialize<WorkflowStateObject>(subReader);
                        }
                    }
                    else
                    {
                        using (var subReader = prop.Value.CreateReader())
                        {
                            dict[prop.Name] = ContractBypassingSerializer(serializer).Deserialize(subReader, typeof(object));
                        }
                    }
                }
                stateObj.Locals = dict;
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
