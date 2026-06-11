using System;
using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using Workflows.Abstraction.DTOs;
using Workflows.Abstraction.DTOs.Waits;
using Workflows.Abstraction.Helpers;
using Workflows.Abstraction.Persistence;

namespace Workflows.Orchestrator
{
    public class SignalPreFilter : ISignalPreFilter
    {
        private readonly IExpressionSerializer _expressionSerializer;
        private readonly ITemplateRepository? _templateRepository;

        private static readonly ConcurrentDictionary<string, Func<JsonElement, JsonElement, JsonElement, bool>> _compiledGenericExpressionsCache = new();
        private static readonly JsonSerializerOptions _jsonSerializerOptions = new JsonSerializerOptions
        {
            Converters = { new JsonStringEnumConverter() }
        };

        public SignalPreFilter(IExpressionSerializer expressionSerializer, ITemplateRepository? templateRepository = null)
        {
            _expressionSerializer = expressionSerializer ?? throw new ArgumentNullException(nameof(expressionSerializer));
            _templateRepository = templateRepository;
        }

        public bool IsMatch(SignalWaitDto signalWait, SignalDto signalDto, WorkflowStateDto state)
        {
            if (signalWait == null) throw new ArgumentNullException(nameof(signalWait));
            if (signalDto == null) throw new ArgumentNullException(nameof(signalDto));
            if (state == null) throw new ArgumentNullException(nameof(state));

            // GenericMatchExpression now lives in the template cache — look it up by TemplateHashKey
            string? genericMatchExpression = null;
            if (!string.IsNullOrEmpty(signalWait.TemplateHashKey))
            {
                var template = _templateRepository?.GetTemplate(signalWait.TemplateHashKey);
                genericMatchExpression = template?.GenericMatchExpressionJson;
            }

            if (string.IsNullOrEmpty(genericMatchExpression))
            {
                return true;
            }

            try
            {
                var compiled = _compiledGenericExpressionsCache.GetOrAdd(genericMatchExpression, exprStr =>
                {
                    var lambda = _expressionSerializer.Deserialize(exprStr);
                    return (Func<JsonElement, JsonElement, JsonElement, bool>)lambda.Compile();
                });

                var signalElement = ToJsonElement(signalDto.Data);

                object? explicitState = null;
                if (state.StateObject?.Locals != null)
                {
                    if (!state.StateObject.Locals.TryGetValue(signalWait.StateKey.ToString(), out explicitState))
                    {
                        state.StateObject.Locals.TryGetValue(signalWait.Id.ToString(), out explicitState);
                    }
                }
                var stateElement = ToJsonElement(explicitState);
                var instanceElement = ToJsonElement(state.StateObject?.Instance);

                var matchResult = compiled(signalElement, stateElement, instanceElement);
                return matchResult;
            }
            catch (Exception)
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
            if (obj is Newtonsoft.Json.Linq.JToken jToken)
            {
                var jsonStr = jToken.ToString(Newtonsoft.Json.Formatting.None);
                return JsonSerializer.Deserialize<JsonElement>(jsonStr, _jsonSerializerOptions);
            }
            return JsonSerializer.SerializeToElement(obj, _jsonSerializerOptions);
        }
    }
}
