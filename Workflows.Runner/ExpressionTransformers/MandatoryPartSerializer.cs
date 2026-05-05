using FastExpressionCompiler;
using System.Linq;
using System.Linq.Expressions;

namespace Workflows.Runner.ExpressionTransformers
{
    // -------------------------------------------------------
    // Registration side — serializes instance constants to JSON
    // -------------------------------------------------------

    internal static class MandatoryPartSerializer
    {
        /// <summary>
        /// Executes InstanceExactMatchExpression and serializes to JSON array.
        /// Called once at wait registration — result stored in Waits.MandatoryPart.
        /// Never recomputed for the lifetime of that wait row.
        /// </summary>
        public static string Serialize(
            LambdaExpression instanceExpression,
            object workflowInstance,
            object closure)
        {
            var values = (object[])instanceExpression
                .CompileFast()
                .DynamicInvoke(workflowInstance, closure)!;

            return Newtonsoft.Json.JsonConvert.SerializeObject(
                values.Select(v => v?.ToString()).ToList());
            // ["42","Paid|Urgent","12"] — handles |, #, quotes, any character
        }
    }
}