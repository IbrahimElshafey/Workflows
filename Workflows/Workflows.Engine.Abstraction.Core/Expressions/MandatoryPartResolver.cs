
using System;
using System.Linq;
using Workflows.Handler.Abstraction.Serialization;

using System.Collections.Concurrent;
using System.Collections.Generic;
namespace Workflows.Handler.Expressions
{
    // -------------------------------------------------------
    // Call-time resolution — replaces executing CallMandatoryPartPaths
    // -------------------------------------------------------

    internal class MandatoryPartResolver
    {
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

        /// <summary>
        /// Builds the MandatoryPart match value from an incoming call.
        /// Replaces: Deserialize(CallMandatoryPartPaths).Compile()(input, output)
        /// Result:   ["42","Paid|Urgent","12"]  — matches Waits.MandatoryPart exactly
        /// </summary>
        public static string BuildFromCall(object input, object output, List<string> paths)
        {
            if (_objectNavigator == null)
                throw new InvalidOperationException("Object navigator not configured. Call SetObjectNavigator first.");
            if (_jsonSerializer == null)
                throw new InvalidOperationException("JSON serializer not configured. Call SetJsonSerializer first.");

            var values = paths.Select(path =>
            {
                var dot = path.IndexOf('.');
                var prefix = path[..dot];       // "input" or "output"
                var token = path[(dot + 1)..]; // "ProjectId" or "Order.RegionId"

                var targetObj = prefix == "output" ? output : input;
                return _objectNavigator.GetValue(targetObj, token);
            });

            return _jsonSerializer.Serialize(values);
        }
    }
}