using System;
using System.Linq.Expressions;

namespace Workflows.Engine.Abstraction.Core.Abstraction.Serialization
{
    /// <summary>
    /// Abstract base class for expression serialization.
    /// Implementations should provide concrete serialization strategy (e.g., using Nuqleon or other libraries).
    /// </summary>
    public abstract class ExpressionSerializer
    {
        /// <summary>
        /// Serializes a LambdaExpression to a string representation
        /// </summary>
        public abstract string Serialize(LambdaExpression expression);

        /// <summary>
        /// Deserializes a string to a LambdaExpression
        /// </summary>
        public abstract LambdaExpression Deserialize(string serialized);
    }
}