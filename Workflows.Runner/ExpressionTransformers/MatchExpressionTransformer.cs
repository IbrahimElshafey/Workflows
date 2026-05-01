using System;
using ResumableFunctions.Handler.Expressions;

namespace Workflows.Runner.ExpressionTransformers
{
    internal class MatchExpressionTransformer
    {
        internal MatchTransformationResult Transform(Definition.ISignalWait signalWait)
        {
            if (signalWait == null)
                throw new ArgumentNullException(nameof(signalWait));

            var matchWriter = new MatchExpressionWriter(
                signalWait.MatchExpression,
                signalWait.CurrentWorkflow);

            var result = matchWriter.MatchExpressionParts;
            if (result?.MatchExpression == null)
                return result;

            var dynamicMatchVisitor = new DynamicMatchVisitor(result.MatchExpression);
            result.GenericMatchExpression = dynamicMatchVisitor.Result;
            result.IsGenericMatchFullMatch = result.GenericMatchExpression != null;

            return result;
        }
    }
}
