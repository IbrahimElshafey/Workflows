using System;
using System.Linq.Expressions;
using System.Threading.Tasks;

namespace Workflows.Runner.ExpressionTransformers
{
    internal class ExpressionCompiler
    {
        /// <summary>
        /// This method compiles the given rewritten match expression like `(workflowInstance, signalData, closure) => bool`
        /// into a delegate that can be invoked at runtime.
        /// </summary>
        /// <param name="matchExpresssion">Result of rewrite original match expression to include clouser and workflowInstance</param>
        /// <exception cref="NotImplementedException"></exception>
        internal Func<object, object, object, bool> CompiledMatchExpression(LambdaExpression matchExpresssion)
        {
            throw new NotImplementedException();
        }
        /// <summary>
        /// This method changes the given after match action like `Action(SignalData signalDtaa)>` to `(workflowInstance, signalData, closure) => void`.
        /// which can be invoked at runtime after a successful match. 
        /// This allows the after match action to have access to the workflow instance and closure if needed.
        /// </summary>
        internal Action<object, object, object> AfterMatchAction<SignalData>(Action<SignalData> afterMatchAction)
        {
            throw new NotImplementedException();
        }
        /// <summary>
        /// Creates a cancellation action delegate that can be invoked with state parameters.
        /// </summary>
        internal Func<object, object, ValueTask> CancelAction(Func<ValueTask> cancelAction)
        {
            throw new NotImplementedException();
        }
        /// <summary>
        /// Creates a compiled delegate that evaluates the specified instance exact match expression and returns the
        /// resulting object array.
        /// (workflowInstance, closure) => string
        /// </summary>
        internal Func<object, object, string> CompiledInstanceExactMatchExpression(LambdaExpression instanceExactMatchExpression)
        {
            throw new NotImplementedException();
        }
        /// <summary>
        /// Creates a filter function that determines whether two objects belong to the same group based on the
        /// specified group match condition.
        /// (workflowInstance, closure) => bool
        /// </summary>
        public Func<object, object, bool> GroupMatchFilter(Func<bool> groupMatchFilter)
        {
            throw new NotImplementedException();
        }
    }
}
