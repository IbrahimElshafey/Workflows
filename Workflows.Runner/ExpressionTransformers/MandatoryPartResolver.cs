using Newtonsoft.Json.Linq;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace Workflows.Runner.ExpressionTransformers
{
    // -------------------------------------------------------
    // Call-time resolution — replaces executing SignalExactMatchPaths
    // -------------------------------------------------------

    internal class MandatoryPartResolver
    {
        private static readonly ConcurrentDictionary<string, Func<JObject, JObject, string?>> _cache = new();

        /// <summary>
        /// Builds the MandatoryPart match value from an incoming call.
        /// Replaces: Deserialize(SignalExactMatchPaths).Compile()(signalData)
        /// Result:   ["42","Paid|Urgent","12"]  — matches Waits.MandatoryPart exactly
        /// </summary>
        public static string BuildFromCall(object signalData, List<string> paths)
        {
            var signalDataObj = JObject.FromObject(signalData);

            var values = paths.Select(path =>
                _cache.GetOrAdd(path, BuildAccessor)(signalDataObj, signalDataObj));

            return Newtonsoft.Json.JsonConvert.SerializeObject(values);
        }

        private static Func<JObject, JObject, string?> BuildAccessor(string path)
        {
            var dot = path.IndexOf('.');
            var token = path[(dot + 1)..];  // "ProjectId" or "Order.RegionId"

            return (signalDataObj, _) =>
                signalDataObj
                    .SelectToken(token)        // handles nested paths free of charge
                    ?.ToString();
        }
    }
}