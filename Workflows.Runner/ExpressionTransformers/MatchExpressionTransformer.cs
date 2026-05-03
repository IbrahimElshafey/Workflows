using System;
using ResumableFunctions.Handler.Expressions;
using Workflows.Definition;

namespace Workflows.Runner.ExpressionTransformers
{
    internal class MatchExpressionTransformer
    {
        internal MatchTransformationResult Transform(ISignalWait signalWait)
        {
            if (signalWait == null)
                throw new ArgumentNullException(nameof(signalWait));

            var matchWriter = new MatchExpressionWriter(
                signalWait.MatchExpression,
                signalWait.WorkflowContainer);

            var result = matchWriter.MatchTransformationResult;
            if (result?.MatchExpression == null)
                return result;

            var dynamicMatchVisitor = new DynamicMatchVisitor(result.MatchExpression);
            result.GenericMatchExpression = dynamicMatchVisitor.Result;
            result.IsGenericMatchFullMatch = result.GenericMatchExpression != null;

            return result;
        }
    }
}
