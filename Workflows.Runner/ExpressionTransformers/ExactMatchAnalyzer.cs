using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;

namespace Workflows.Runner.ExpressionTransformers
{
    internal sealed class ExactMatchAnalyzer
    {
        // ── Public result ────────────────────────────────────────────────────────
        public bool IsFullMatch { get; }
        public IReadOnlyList<string> SignalPaths { get; }
        public Expression<Func<object, object, string[]>>? InstanceMatchExpression { get; }

        // ── Factory ──────────────────────────────────────────────────────────────
        public static ExactMatchAnalyzer Analyze(
            LambdaExpression? typedExpression,
            bool dynamicVisitorIsFullMatch)
        {
            // Fast-path: upstream already failed
            if (!dynamicVisitorIsFullMatch || typedExpression is null)
                return new ExactMatchAnalyzer(isFullMatch: false);

            var visitor = new CollectingVisitor(typedExpression);
            visitor.Visit(typedExpression.Body);

            // Sort so SQL index key order is deterministic
            var sorted = visitor.Pairs
                .OrderBy(p => p.SignalPath)
                .ToList();

            var paths = sorted.Select(p => p.SignalPath).ToList();
            var matchExpr = BuildInstanceMatchExpression(
                typedExpression,
                sorted.Select(p => p.OtherSide).ToList());

            return new ExactMatchAnalyzer(
                isFullMatch: visitor.IsFullMatch,
                signalPaths: paths,
                instanceMatchExpression: matchExpr);
        }

        // ── Private constructor (results are immutable after construction) ────────
        private ExactMatchAnalyzer(
            bool isFullMatch,
            IReadOnlyList<string>? signalPaths = null,
            Expression<Func<object, object, string[]>>? instanceMatchExpression = null)
        {
            IsFullMatch = isFullMatch;
            SignalPaths = signalPaths ?? [];
            InstanceMatchExpression = instanceMatchExpression;
        }

        // ── Expression builder (pure, no side effects) ───────────────────────────
        private static Expression<Func<object, object, string[]>>? BuildInstanceMatchExpression(
            LambdaExpression source,
            List<Expression> otherSides)
        {
            if (otherSides.Count == 0) return null;

            var instanceParam = Expression.Parameter(typeof(object), "workflowInstance");
            var stateParam = Expression.Parameter(typeof(object), "state");

            var paramMap = BuildParamMap(source.Parameters, instanceParam, stateParam);
            var replacer = new ParameterReplacer(paramMap);

            var stringElements = otherSides.Select(expr =>
                ToStringExpression(replacer.Visit(expr)));

            return Expression.Lambda<Func<object, object, string[]>>(
                Expression.NewArrayInit(typeof(string), stringElements),
                instanceParam, stateParam);
        }

        // Maps the original typed parameters → the new object parameters
        private static Dictionary<ParameterExpression, Expression> BuildParamMap(
            IReadOnlyList<ParameterExpression> original,
            Expression instanceParam,
            Expression stateParam)
        {
            var map = new Dictionary<ParameterExpression, Expression>();
            // Parameters[0] = signal (not remapped — never appears on the "other side")
            if (original.Count > 1) map[original[1]] = Expression.Convert(stateParam, original[1].Type);
            if (original.Count > 2) map[original[2]] = Expression.Convert(instanceParam, original[2].Type);
            return map;
        }

        private static Expression ToStringExpression(Expression expr) =>
            Expression.Call(
                typeof(Convert),
                nameof(Convert.ToString),
                typeArguments: null,
                Expression.Convert(expr, typeof(object)));

        // ── Inner visitor: only responsible for collecting pairs ─────────────────
        private sealed class CollectingVisitor(LambdaExpression source) : ExpressionVisitor
        {
            private readonly ParameterExpression _signalParam = source.Parameters[0];

            public List<(string SignalPath, Expression OtherSide)> Pairs { get; } = [];
            public bool IsFullMatch { get; private set; } = true;

            protected override Expression VisitBinary(BinaryExpression node)
            {
                if (node.NodeType == ExpressionType.AndAlso)
                    return base.VisitBinary(node); // recurse into both sides

                if (node.NodeType == ExpressionType.Equal && TryAddPair(node.Left, node.Right))
                    return node;

                IsFullMatch = false;
                return node;
            }

            protected override Expression VisitMethodCall(MethodCallExpression node)
            {
                if (node.Method.Name == nameof(object.Equals)
                    && node.Arguments.Count == 1
                    && node.Object is not null
                    && TryAddPair(node.Object, node.Arguments[0]))
                    return node;

                IsFullMatch = false;
                return node;
            }

            // Returns true and records the pair when left is a signal member
            // and right doesn't reference the signal parameter.
            private bool TryAddPair(Expression left, Expression right)
            {
                if (left is MemberExpression member
                    && IsSignalMember(member)
                    && !ReferencesSignal(right))
                {
                    Pairs.Add((GetPath(member), right));
                    return true;
                }

                // Try the flipped order too
                if (right is MemberExpression member2
                    && IsSignalMember(member2)
                    && !ReferencesSignal(left))
                {
                    Pairs.Add((GetPath(member2), left));
                    return true;
                }

                return false;
            }

            private bool IsSignalMember(MemberExpression node) =>
                GetRoot(node) == _signalParam;

            private bool ReferencesSignal(Expression node)
            {
                var checker = new ParameterReferenceChecker(_signalParam);
                checker.Visit(node);
                return checker.Found;
            }
        }

        // ── Shared helpers ────────────────────────────────────────────────────────
        private static Expression GetRoot(Expression node)
        {
            while (node is MemberExpression me) node = me.Expression!;
            return node;
        }

        private static string GetPath(MemberExpression node)
        {
            var parts = new List<string>();
            for (MemberExpression? cur = node; cur is not null; cur = cur.Expression as MemberExpression)
                parts.Add(cur.Member.Name);
            parts.Reverse();
            return string.Join('.', parts);
        }

        private sealed class ParameterReplacer(Dictionary<ParameterExpression, Expression> map)
            : ExpressionVisitor
        {
            protected override Expression VisitParameter(ParameterExpression node) =>
                map.GetValueOrDefault(node) ?? base.VisitParameter(node);
        }

        private sealed class ParameterReferenceChecker(ParameterExpression target)
            : ExpressionVisitor
        {
            public bool Found { get; private set; }

            protected override Expression VisitParameter(ParameterExpression node)
            {
                if (node == target) Found = true;
                return base.VisitParameter(node);
            }
        }
    }
}