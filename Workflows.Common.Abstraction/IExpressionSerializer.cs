using System.Linq.Expressions;

namespace Workflows.Common.Abstraction
{
    /// <summary>
    /// Responsible for serializing C# Expression Trees used in workflow wait conditions.
    /// This allows the Orchestrator to store and later evaluate match logic.
    /// </summary>
    public interface IExpressionSerializer
    {
        /// <summary>
        /// Serializes a LINQ expression into a format that can be stored in the database.
        /// </summary>
        /// <param name="expression">The lambda expression to serialize (e.g., a match or data-mapping expression).</param>
        /// <returns>A serialized representation of the expression tree.</returns>
        string Serialize(LambdaExpression expression);

        /// <summary>
        /// Deserializes a string back into a functional LINQ Expression Tree.
        /// </summary>
        /// <param name="serializedExpression">The string representation of the expression.</param>
        /// <returns>A reconstructed LambdaExpression.</returns>
        LambdaExpression Deserialize(string serializedExpression);
    }
}