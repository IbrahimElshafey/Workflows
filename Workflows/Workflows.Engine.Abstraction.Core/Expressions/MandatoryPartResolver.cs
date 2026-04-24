
using System;

using System.Collections.Concurrent;
using System.Collections.Generic;
namespace Workflows.Handler.Expressions
{
    // -------------------------------------------------------
    // Call-time resolution — replaces executing CallMandatoryPartPaths
    // -------------------------------------------------------

    internal class MandatoryPartResolver
    {
        private static readonly ConcurrentDictionary<string, Func<JObject, JObject, string?>> _cache = new();

        /// <summary>
        /// Builds the MandatoryPart match value from an incoming call.
        /// Replaces: Deserialize(CallMandatoryPartPaths).Compile()(input, output)
        /// Result:   ["42","Paid|Urgent","12"]  — matches Waits.MandatoryPart exactly
        /// </summary>
        public static string BuildFromCall(object input, object output, List<string> paths)
        {
            var inputObj = JObject.FromObject(input);
            var outputObj = JObject.FromObject(output);

            var values = paths.Select(path =>
                _cache.GetOrAdd(path, BuildAccessor)(inputObj, outputObj));

            return JsonSerializer.Serialize(values);
        }

        private static Func<JObject, JObject, string?> BuildAccessor(string path)
        {
            var dot = path.IndexOf('.');
            var prefix = path[..dot];        // "input" or "output"
            var token = path[(dot + 1)..];  // "ProjectId" or "Order.RegionId"

            return (inputObj, outputObj) =>
                (prefix == "output" ? outputObj : inputObj)
                    .SelectToken(token)        // handles nested paths free of charge
                    ?.ToString();
        }
    }
}