using FastExpressionCompiler;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using static Workflows.Runner.ExpressionTransformers.MatchExpressionWriter;
using static System.Linq.Expressions.Expression;
using Workflows.Runner.Helpers;

namespace Workflows.Runner.ExpressionTransformers
{
    internal class MandatoryPartExpressionsGenerator : ExpressionVisitor
    {
        private readonly List<DataObjects.ExpressionPart> _mandatoryParts;
        private readonly LambdaExpression _matchExpression;

        /// <summary>
        /// Replaces the old SignalExactMatchPaths (heavy serialized lambda).
        /// Stored in WaitTemplates as plain JSON array: ["signalData.ProjectId","signalData.RegionId"]
        /// Order matches InstanceExactMatchExpression output order (both sorted by InputOutputPart.ToString())
        /// </summary>
        public List<string> SignalExactMatchPaths { get; }

        /// <summary>
        /// Unchanged — evaluates instance constants at wait registration time.
        /// Caller must serialize result as JSON array → stored in Waits.MandatoryPart.
        /// e.g. ["42","Paid|Urgent","12"]
        /// </summary>
        public LambdaExpression InstanceExactMatchExpression { get; }

        public MandatoryPartExpressionsGenerator(
            LambdaExpression matchExpression,
            List<DataObjects.ExpressionPart> expressionParts)
        {
            _matchExpression = matchExpression;
            _mandatoryParts = expressionParts
                .Where(x => x.IsMandatory)
                .OrderBy(x => x.InputOutputPart.ToString()) // same order as before
                .ToList();

            SignalExactMatchPaths = ExtractCallPaths();
            InstanceExactMatchExpression = GetInstanceMandatoryPartExpression();
        }

        /// <summary>
        /// Walks each InputOutputPart MemberExpression chain to produce a dot-separated path.
        /// signalData.ProjectId           → "signalData.ProjectId"
        /// signalData.Order.RegionId      → "signalData.Order.RegionId"
        /// signalData.IsActive (bool)     → "signalData.IsActive"
        /// </summary>
        private List<string> ExtractCallPaths() =>
            _mandatoryParts
                .Select(x => BuildPath(x.InputOutputPart))
                .ToList();

        private string BuildPath(Expression expression)
        {
            var parts = new Stack<string>();
            var current = expression;

            // Walk MemberExpression chain: output.Order.ProjectId
            while (current is MemberExpression member)
            {
                parts.Push(member.Member.Name);
                current = member.Expression;
            }

            // Root must be signalData parameter
            if (current is ParameterExpression param)
            {
                var prefix = param == _matchExpression.Parameters[0] ? "signalData" : "signalData";
                parts.Push(prefix);
            }

            return string.Join(".", parts);
        }

        private LambdaExpression GetInstanceMandatoryPartExpression()
        {
            return Lambda(
                NewArrayInit(
                    typeof(object),
                    _mandatoryParts.Select(x => Convert(ValuePart(x.ValuePart), typeof(object)))),
                _matchExpression.Parameters[1],
                _matchExpression.Parameters[2]);

            Expression ValuePart(Expression valuePart)
            {
                var changeClosureVarsVisitor = new GenericVisitor();
                changeClosureVarsVisitor.OnVisitConstant(node =>
                {
                    if (node.Type.Name.StartsWith(CompilerConstants.ClosurePrefix))
                        return _matchExpression.Parameters[2];
                    return base.VisitConstant(node);
                });
                return changeClosureVarsVisitor.Visit(valuePart);
            }
        }
    }
}