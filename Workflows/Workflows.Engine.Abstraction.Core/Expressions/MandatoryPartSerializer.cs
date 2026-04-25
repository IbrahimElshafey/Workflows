using System.Linq.Expressions;
using Workflows.Handler.Abstraction.Serialization;
using System;
using System.Linq;
namespace Workflows.Handler.Expressions
{
    // -------------------------------------------------------
    // Registration side — serializes instance constants to JSON
    // -------------------------------------------------------

    internal static class MandatoryPartSerializer
    {
        private static IJsonSerializer _jsonSerializer;

        /// <summary>
        /// Sets the JSON serializer implementation to use
        /// </summary>
        public static void SetJsonSerializer(IJsonSerializer jsonSerializer)
        {
            _jsonSerializer = jsonSerializer ?? throw new ArgumentNullException(nameof(jsonSerializer));
        }

        /// <summary>
        /// Executes InstanceMandatoryPartExpression and serializes to JSON array.
        /// Called once at wait registration — result stored in Waits.MandatoryPart.
        /// Never recomputed for the lifetime of that wait row.
        /// </summary>
        public static string Serialize(
            LambdaExpression instanceExpression,
            object workflowInstance,
            object closure)
        {
            var values = (object[])instanceExpression
                .Compile()
                .DynamicInvoke(workflowInstance, closure)!;

            if (_jsonSerializer == null)
                throw new InvalidOperationException("JSON serializer not configured. Call SetJsonSerializer first.");

            return _jsonSerializer.Serialize(
                values.Select(v => v?.ToString()).ToList());
            // ["42","Paid|Urgent","12"] — handles |, #, quotes, any character
        }
    }
}