using System;
using System.Linq.Expressions;
using System.Threading.Tasks;
using FastExpressionCompiler;

namespace Workflows.Runner.ExpressionTransformers
{
    internal class ExpressionCompiler
    {
        /// <summary>
        /// This method compiles the given rewritten match expression like `(workflowInstance, signalData, closure) => bool`
        /// into a delegate that can be invoked at runtime.
        /// </summary>
        /// <param name="matchExpresssion">Result of rewrite original match expression to include closure and workflowInstance</param>
        internal Func<object, object, object, bool> CompiledMatchExpression(LambdaExpression matchExpresssion)
        {
            if (matchExpresssion == null) throw new ArgumentNullException(nameof(matchExpresssion));
            return (Func<object, object, object, bool>)matchExpresssion.CompileFast();
        }

        /// <summary>
        /// This method changes the given after match action like `Action(SignalData signalData)` to `(workflowInstance, signalData, closure) => void`.
        /// which can be invoked at runtime after a successful match. 
        /// This allows the after match action to have access to the workflow instance and closure if needed.
        /// </summary>
        internal Action<object, object, object> AfterMatchAction<SignalData>(Action<SignalData> afterMatchAction)
        {
            if (afterMatchAction == null) return null;
            return (instance, signal, state) => afterMatchAction((SignalData)signal);
        }

        /// <summary>
        /// Creates a cancellation action delegate that can be invoked with state parameters.
        /// </summary>
        internal Func<object, object, ValueTask> CancelAction(Func<ValueTask> cancelAction)
        {
            if (cancelAction == null) return null;
            return (instance, state) => cancelAction();
        }

        /// <summary>
        /// Creates a compiled delegate that evaluates the specified instance exact match expression and returns the
        /// resulting object array.
        /// (workflowInstance, closure) => string[]
        /// </summary>
        internal Func<object, object, string[]> CompiledInstanceExactMatchExpression(LambdaExpression instanceExactMatchExpression)
        {
            if (instanceExactMatchExpression == null) throw new ArgumentNullException(nameof(instanceExactMatchExpression));
            return (Func<object, object, string[]>)instanceExactMatchExpression.CompileFast();
        }

        /// <summary>
        /// Creates a filter function that determines whether two objects belong to the same group based on the
        /// specified group match condition.
        /// (workflowInstance, closure) => bool
        /// </summary>
        public Func<object, object, bool> GroupMatchFilter(Func<bool> groupMatchFilter)
        {
            if (groupMatchFilter == null) return null;
            return (instance, state) => groupMatchFilter();
        }
    }
}

