using System;
using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using Workflows.Abstraction.DTOs;
using Workflows.Abstraction.DTOs.Waits;
using Workflows.Abstraction.Helpers;

namespace Workflows.Orchestrator
{
    public class SignalPreFilter : ISignalPreFilter
    {
        private readonly IExpressionSerializer _expressionSerializer;

        private static readonly ConcurrentDictionary<string, Func<JsonElement, JsonElement, JsonElement, bool>> _compiledGenericExpressionsCache = new();
        private static readonly JsonSerializerOptions _jsonSerializerOptions = new JsonSerializerOptions
        {
            Converters = { new JsonStringEnumConverter() }
        };

        public SignalPreFilter(IExpressionSerializer expressionSerializer)
        {
            _expressionSerializer = expressionSerializer ?? throw new ArgumentNullException(nameof(expressionSerializer));
        }

        public bool IsMatch(SignalWaitDto signalWait, SignalDto signalDto, WorkflowStateDto state)
        {
            if (signalWait == null) throw new ArgumentNullException(nameof(signalWait));
            if (signalDto == null) throw new ArgumentNullException(nameof(signalDto));
            if (state == null) throw new ArgumentNullException(nameof(state));

            if (string.IsNullOrEmpty(signalWait.GenericMatchExpression))
            {
                return true;
            }

            try
            {
                var compiled = _compiledGenericExpressionsCache.GetOrAdd(signalWait.GenericMatchExpression, exprStr =>
                {
                    var lambda = _expressionSerializer.Deserialize(exprStr);
                    return (Func<JsonElement, JsonElement, JsonElement, bool>)lambda.Compile();
                });

                var signalElement = ToJsonElement(signalDto.Data);

                object? explicitState = null;
                if (state.StateObject?.WaitStatesObjects != null)
                {
                    if (!state.StateObject.WaitStatesObjects.TryGetValue(signalWait.StateKey, out explicitState))
                    {
                        state.StateObject.WaitStatesObjects.TryGetValue(signalWait.Id, out explicitState);
                    }
                }
                var stateElement = ToJsonElement(explicitState);
                var instanceElement = ToJsonElement(state.StateObject?.Instance);

                return compiled(signalElement, stateElement, instanceElement);
            }
            catch
            {
                // Defensive fallback: proceed to runner if evaluation fails
                return true;
            }
        }

        private static JsonElement ToJsonElement(object? obj)
        {
            if (obj == null) return default;
            if (obj is JsonElement je) return je;
            if (obj is JsonDocument jd) return jd.RootElement;
            return JsonSerializer.SerializeToElement(obj, _jsonSerializerOptions);
        }
    }
}
