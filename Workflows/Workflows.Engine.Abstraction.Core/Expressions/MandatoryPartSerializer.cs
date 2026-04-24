using System.Linq.Expressions;
namespace Workflows.Handler.Expressions
{
    // -------------------------------------------------------
    // Registration side — serializes instance constants to JSON
    // -------------------------------------------------------

    internal static class MandatoryPartSerializer
    {
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
                .CompileFast()
                .DynamicInvoke(workflowInstance, closure)!;

            return JsonSerializer.Serialize(
                values.Select(v => v?.ToString()).ToList());
            // ["42","Paid|Urgent","12"] — handles |, #, quotes, any character
        }
    }
}