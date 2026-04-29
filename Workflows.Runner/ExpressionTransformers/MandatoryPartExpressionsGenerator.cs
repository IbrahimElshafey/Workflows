using FastExpressionCompiler;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using static ResumableFunctions.Handler.Expressions.MatchExpressionWriter;
using static System.Linq.Expressions.Expression;

namespace Workflows.Runner.ExpressionTransformers
{
    internal class MandatoryPartExpressionsGenerator : ExpressionVisitor
    {
        private readonly List<ExpressionPart> _mandatoryParts;
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
            List<ExpressionPart> expressionParts)
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
                    if (node.Type.Name.StartsWith(Abstraction.Helpers.Constants.CompilerClosurePrefix))
                        return _matchExpression.Parameters[2];
                    return base.VisitConstant(node);
                });
                return changeClosureVarsVisitor.Visit(valuePart);
            }
        }
    }

    // -------------------------------------------------------
    // Call-time resolution — replaces executing SignalExactMatchPaths
    // -------------------------------------------------------

    internal class MandatoryPartResolver
    {
        private static readonly ConcurrentDictionary<string, Func<JObject, JObject, string?>> _cache = new();

        /// <summary>
        /// Builds the MandatoryPart match value from an incoming call.
        /// Replaces: Deserialize(SignalExactMatchPaths).Compile()(signalData)
        /// Result:   ["42","Paid|Urgent","12"]  — matches Waits.MandatoryPart exactly
        /// </summary>
        public static string BuildFromCall(object signalData, List<string> paths)
        {
            var signalDataObj = JObject.FromObject(signalData);

            var values = paths.Select(path =>
                _cache.GetOrAdd(path, BuildAccessor)(signalDataObj, signalDataObj));

            return Newtonsoft.Json.JsonConvert.SerializeObject(values);
        }

        private static Func<JObject, JObject, string?> BuildAccessor(string path)
        {
            var dot = path.IndexOf('.');
            var token = path[(dot + 1)..];  // "ProjectId" or "Order.RegionId"

            return (signalDataObj, _) =>
                signalDataObj
                    .SelectToken(token)        // handles nested paths free of charge
                    ?.ToString();
        }
    }

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