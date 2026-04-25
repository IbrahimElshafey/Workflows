using System.Collections.Concurrent;
using System.Linq.Expressions;
using Workflows.Handler.Abstraction.Serialization;
using System.Linq;

using System;
using System.Collections.Generic;
namespace Workflows.Handler.InOuts
{
    public class MatchExpressionParts
    {
        public LambdaExpression MatchExpression { get; internal set; }
        public List<string> CallMandatoryPartPaths { get; internal set; }
        public LambdaExpression InstanceMandatoryPartExpression { get; internal set; }
        public bool IsMandatoryPartFullMatch { get; internal set; }
        public object Closure { get; internal set; }

        private static IJsonSerializer _jsonSerializer;
        private static IObjectNavigator _objectNavigator;

        /// <summary>
        /// Sets the JSON serializer implementation to use
        /// </summary>
        public static void SetJsonSerializer(IJsonSerializer jsonSerializer)
        {
            _jsonSerializer = jsonSerializer ?? throw new ArgumentNullException(nameof(jsonSerializer));
        }

        /// <summary>
        /// Sets the object navigator implementation to use
        /// </summary>
        public static void SetObjectNavigator(IObjectNavigator objectNavigator)
        {
            _objectNavigator = objectNavigator ?? throw new ArgumentNullException(nameof(objectNavigator));
        }

        public string GetInstanceMandatoryPart(object currentWorkflow)
        {
            if (InstanceMandatoryPartExpression == null) return null;

            var parts = (object[])InstanceMandatoryPartExpression
                .Compile()
                .DynamicInvoke(currentWorkflow, Closure);

            if (parts?.Any() != true) return null;

            if (_jsonSerializer == null)
                throw new InvalidOperationException("JSON serializer not configured. Call SetJsonSerializer first.");

            return _jsonSerializer.Serialize(
                parts.Select(v => v?.ToString()).ToList());
            // ["42","Paid|Urgent","12"] — handles any character safely
        }

        public string GetSignalMandatoryPart(object input, object output)
        {
            if (CallMandatoryPartPaths?.Any() != true) return null;

            if (_objectNavigator == null)
                throw new InvalidOperationException("Object navigator not configured. Call SetObjectNavigator first.");
            if (_jsonSerializer == null)
                throw new InvalidOperationException("JSON serializer not configured. Call SetJsonSerializer first.");

            var values = CallMandatoryPartPaths.Select(path =>
            {
                var dot = path.IndexOf('.');
                var prefix = path[..dot];       // "input" or "output"
                var token = path[(dot + 1)..]; // "ProjectId" or "Order.RegionId"

                var targetObj = prefix == "output" ? output : input;
                return _objectNavigator.GetValue(targetObj, token);
            });

            return _jsonSerializer.Serialize(values);
            // Same JSON array format as GetInstanceMandatoryPart → equality match works
        }
    }
}