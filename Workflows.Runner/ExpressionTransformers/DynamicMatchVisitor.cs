using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text.Json;
using Workflows.Shared;

namespace Workflows.Runner.ExpressionTransformers
{
    internal class DynamicMatchVisitor : ExpressionVisitor
    {
        private readonly LambdaExpression _originalLambda;
        private bool _isUnsupportedNodeFound;
        private readonly HashSet<Expression> _partialNodes = new();
        private static readonly ConstantExpression UnknownNode = Expression.Constant("__UNKNOWN__");

        public LambdaExpression TypedResult { get; private set; }

        public Expression<Func<JsonElement, JsonElement, JsonElement, bool>> Result { get; private set; }

        public bool IsFullMatch => !_isUnsupportedNodeFound && Result != null;

        public List<(string SignalPath, Expression OtherSide, Type PropertyType)> PotentialExactMatchPairs { get; private set; } = new();

        public bool IsExactMatchFullMatch { get; private set; } = true;

        public DynamicMatchVisitor(LambdaExpression originalLambda)
        { _originalLambda = originalLambda ?? throw new ArgumentNullException(nameof(originalLambda)); }

        private void MarkPartial(Expression node)
        {
            if(node != null)
                _partialNodes.Add(node);
        }

        private bool IsPartial(Expression node) => node != null && _partialNodes.Contains(node);

        public void Build()
        {
            // PASS 1: Walk the original typed tree and safely prune unsupported logic
            var safeTypedBody = Visit(_originalLambda.Body);

            // CRITICAL: Return null if the tree couldn't be saved or collapsed safely
            if(safeTypedBody == UnknownNode || safeTypedBody == null || _isUnsupportedNodeFound)
            {
                Result = null;
                TypedResult = null;
                IsExactMatchFullMatch = false;
                return;
            }

            TypedResult = Expression.Lambda(safeTypedBody, _originalLambda.Parameters);

            // PASS 2: Translate the perfectly safe POCO tree into a JsonElement execution tree
            var jsonTranslator = new JsonTranslationVisitor(_originalLambda.Parameters);
            var jsonBody = jsonTranslator.Visit(safeTypedBody);

            Result = Expression.Lambda<Func<JsonElement, JsonElement, JsonElement, bool>>(
                jsonBody,
                jsonTranslator.JsonSignal,
                jsonTranslator.JsonState,
                jsonTranslator.JsonInstance);

            // PASS 3: Collect exact match pairs from the safe typed body
            var collector = new ExactMatchCollector(_originalLambda.Parameters[0]);
            collector.Visit(safeTypedBody);
            PotentialExactMatchPairs = collector.Pairs;
            IsExactMatchFullMatch = collector.IsFullMatch;
        }

        // --- Pass 1: Pruning Logic (Operates on strongly-typed POCO tree) ---

        protected override Expression VisitBinary(BinaryExpression node)
        {
            var left = Visit(node.Left);
            var right = Visit(node.Right);

            if(left == UnknownNode && right == UnknownNode)
                return UnknownNode;

            if(left == UnknownNode || right == UnknownNode)
            {
                if(node.NodeType == ExpressionType.AndAlso)
                {
                    var kept = left == UnknownNode ? right : left;
                    MarkPartial(kept);
                    return kept;
                }
                _isUnsupportedNodeFound = true;
                return UnknownNode;
            }

            var updated = node.Update(left, node.Conversion, right);
            if(IsPartial(left) || IsPartial(right))
                MarkPartial(updated);
            return updated;
        }

        protected override Expression VisitUnary(UnaryExpression node)
        {
            var operand = Visit(node.Operand);
            if(operand == UnknownNode)
                return UnknownNode;

            // Negation on a tainted partial node risks False Negatives. Abort.
            if(node.NodeType == ExpressionType.Not && IsPartial(operand))
            {
                _isUnsupportedNodeFound = true;
                return UnknownNode;
            }

            var updated = node.Update(operand);
            if(IsPartial(operand))
                MarkPartial(updated);
            return updated;
        }

        protected override Expression VisitMethodCall(MethodCallExpression node)
        {
            // Only .Equals() is native. Everything else (like DbCheck()) triggers Unknown.
            if(node.Method.Name == "Equals" && node.Arguments.Count == 1 && node.Object != null)
            {
                var obj = Visit(node.Object);
                var arg = Visit(node.Arguments[0]);

                if(obj == UnknownNode || arg == UnknownNode)
                    return UnknownNode;

                var updated = node.Update(obj, new[] { arg });
                if(IsPartial(obj) || IsPartial(arg))
                    MarkPartial(updated);
                return updated;
            }

            if(node.Method.DeclaringType == typeof(string) &&
                node.Method.Name == "Equals" &&
                node.Method.IsStatic &&
                node.Arguments.Count == 2)
            {
                var arg0 = Visit(node.Arguments[0]);
                var arg1 = Visit(node.Arguments[1]);

                if(arg0 == UnknownNode || arg1 == UnknownNode)
                    return UnknownNode;

                var updated = node.Update(null, new[] { arg0, arg1 });
                if(IsPartial(arg0) || IsPartial(arg1))
                    MarkPartial(updated);
                return updated;
            }

            _isUnsupportedNodeFound = true;
            return UnknownNode;
        }

        protected override Expression VisitMember(MemberExpression node)
        {
            var root = GetRoot(node);
            if (root is ParameterExpression p && _originalLambda.Parameters.Contains(p))
            {
                if (!IsValidPath(node))
                {
                    _isUnsupportedNodeFound = true;
                    return UnknownNode;
                }
            }
            return base.VisitMember(node);
        }

        private static Expression UnwrapConvert(Expression node)
        {
            while (node is UnaryExpression ue &&
                (ue.NodeType == ExpressionType.Convert || ue.NodeType == ExpressionType.ConvertChecked))
            {
                node = ue.Operand;
            }
            return node;
        }

        private static Expression GetRoot(Expression node)
        {
            node = UnwrapConvert(node);
            while (node is MemberExpression me)
            {
                node = UnwrapConvert(me.Expression!);
            }
            return node;
        }

        private static bool IsValidPath(MemberExpression node)
        {
            Expression? current = node.Expression;
            while (current != null)
            {
                current = UnwrapConvert(current);
                if (IsRepresentableInJson(current.Type))
                {
                    return false;
                }
                if (current is MemberExpression me)
                {
                    current = me.Expression;
                }
                else
                {
                    break;
                }
            }
            return true;
        }

        private static bool IsRepresentableInJson(Type type)
        {
            var underlyingType = Nullable.GetUnderlyingType(type) ?? type;
            var types = new[]
            {
                typeof(bool),
                typeof(byte),
                typeof(sbyte),
                typeof(char),
                typeof(decimal),
                typeof(double),
                typeof(float),
                typeof(int),
                typeof(uint),
                typeof(nint),
                typeof(nuint),
                typeof(short),
                typeof(ushort),
                typeof(long),
                typeof(ulong),
                typeof(string),
                typeof(Guid),
                typeof(DateTime),
                typeof(TimeSpan)
            };
            return types.Contains(underlyingType) || underlyingType.IsEnum;
        }

        private static bool IsRepresentableInSql(Type type)
        {
            var underlyingType = Nullable.GetUnderlyingType(type) ?? type;
            var types = new[]
            {
                typeof(bool),
                typeof(byte),
                typeof(sbyte),
                typeof(char),
                typeof(decimal),
                typeof(double),
                typeof(float),
                typeof(int),
                typeof(uint),
                typeof(nint),
                typeof(nuint),
                typeof(short),
                typeof(ushort),
                typeof(long),
                typeof(ulong),
                typeof(string),
                typeof(Guid)
            };
            return types.Contains(underlyingType) || underlyingType.IsEnum;
        }

        // --- Pass 2: The Internal JSON Mapper ---
        private class JsonTranslationVisitor : ExpressionVisitor
        {
            public ParameterExpression JsonSignal { get; } = Expression.Parameter(typeof(JsonElement), "signalData");

            public ParameterExpression JsonState { get; } = Expression.Parameter(typeof(JsonElement), "stateData");

            public ParameterExpression JsonInstance
            {
                get;
            } = Expression.Parameter(typeof(JsonElement), "workflowInstance");

            private readonly IReadOnlyList<ParameterExpression> _originalParams;

            public JsonTranslationVisitor(IReadOnlyList<ParameterExpression> originalParams)
            { _originalParams = originalParams; }

            protected override Expression VisitMember(MemberExpression node)
            {
                var root = GetRoot(node);
                if(root is ParameterExpression p)
                {
                    ParameterExpression target = null;
                    if(_originalParams.Count > 0 && p == _originalParams[0])
                        target = JsonSignal;
                    else if(_originalParams.Count > 1 && p == _originalParams[1])
                        target = JsonState;
                    else if(_originalParams.Count > 2 && p == _originalParams[2])
                        target = JsonInstance;

                    if(target != null)
                    {
                        var path = GetPath(node);
                        var method = typeof(JsonElementExtensions).GetMethods()
                            .First(x => x.Name == "Get" && x.IsGenericMethod)
                            .MakeGenericMethod(node.Type);

                        return Expression.Call(method, target, Expression.Constant(path));
                    }
                }
                return base.VisitMember(node);
            }

            protected override Expression VisitMethodCall(MethodCallExpression node)
            {
                // JsonElement doesn't have .Equals(), so map it mathematically to ==
                if(node.Method.Name == "Equals" && node.Arguments.Count == 1 && node.Object != null)
                {
                    var left = Visit(node.Object);
                    var right = Visit(node.Arguments[0]);
                    return Expression.Equal(left, right);
                }
                if(node.Method.DeclaringType == typeof(string) &&
                    node.Method.Name == "Equals" &&
                    node.Method.IsStatic &&
                    node.Arguments.Count == 2)
                {
                    var left = Visit(node.Arguments[0]);
                    var right = Visit(node.Arguments[1]);
                    return Expression.Equal(left, right);
                }
                return base.VisitMethodCall(node);
            }

            private static Expression GetRoot(Expression node)
            {
                while(node is MemberExpression me)
                    node = me.Expression;
                return node;
            }

            private static string GetPath(MemberExpression node)
            {
                var path = new List<string>();
                while(node != null)
                {
                    path.Add(node.Member.Name);
                    node = node.Expression as MemberExpression;
                }
                path.Reverse();
                return string.Join(".", path);
            }
        }

        private sealed class ExactMatchCollector : ExpressionVisitor
        {
            private readonly ParameterExpression _signalParam;

            public List<(string SignalPath, Expression OtherSide, Type PropertyType)> Pairs { get; } = new();

            public bool IsFullMatch { get; private set; } = true;

            public ExactMatchCollector(ParameterExpression signalParam) { _signalParam = signalParam; }

            public override Expression Visit(Expression node)
            {
                if(node == null)
                    return null;

                if(node is BinaryExpression bin)
                {
                    if(bin.NodeType != ExpressionType.AndAlso && bin.NodeType != ExpressionType.Equal)
                    {
                        IsFullMatch = false;
                    }
                } else if(node is UnaryExpression unary)
                {
                    if(unary.NodeType != ExpressionType.Not &&
                        unary.NodeType != ExpressionType.Convert &&
                        unary.NodeType != ExpressionType.ConvertChecked)
                    {
                        IsFullMatch = false;
                    }
                } else if(node is MethodCallExpression)
                {
                    // Will be handled in VisitMethodCall. If not valid equals, it sets IsFullMatch = false.
                } else if(node is LambdaExpression)
                {
                    // Lambda is allowed container
                } else
                {
                    IsFullMatch = false;
                }

                return base.Visit(node);
            }

            protected override Expression VisitBinary(BinaryExpression node)
            {
                if(node.NodeType == ExpressionType.Equal)
                {
                    if(TryExtractEqualityPair(node.Left, node.Right))
                    {
                        return node;
                    }
                }

                if(node.NodeType != ExpressionType.AndAlso)
                {
                    IsFullMatch = false;
                    return node;
                }

                return base.VisitBinary(node);
            }

            protected override Expression VisitMethodCall(MethodCallExpression node)
            {
                bool isHandled = false;

                if(node.Method.DeclaringType == typeof(string) &&
                    node.Method.Name == nameof(string.Equals) &&
                    node.Method.IsStatic &&
                    node.Arguments.Count == 2)
                {
                    isHandled = TryExtractEqualityPair(node.Arguments[0], node.Arguments[1]);
                } else if(node.Method.Name == nameof(string.Equals) && node.Object != null && node.Arguments.Count == 1)
                {
                    isHandled = TryExtractEqualityPair(node.Object, node.Arguments[0]);
                }

                if(isHandled)
                {
                    return node;
                }

                IsFullMatch = false;
                return base.VisitMethodCall(node);
            }

            private bool TryExtractEqualityPair(Expression exprA, Expression exprB)
            {
                var unwrappedA = UnwrapConvert(exprA);
                var unwrappedB = UnwrapConvert(exprB);

                if(unwrappedA is MemberExpression memberA &&
                    IsValidPath(memberA) &&
                    IsRootedInParameter(memberA, _signalParam) &&
                    !ContainsParameter(unwrappedB))
                {
                    if(IsRepresentableInSql(memberA.Type) && IsRepresentableInSql(unwrappedB.Type))
                    {
                        Pairs.Add((GetPath(memberA), exprB, memberA.Type));
                        return true;
                    }
                }
                if(unwrappedB is MemberExpression memberB &&
                    IsValidPath(memberB) &&
                    IsRootedInParameter(memberB, _signalParam) &&
                    !ContainsParameter(unwrappedA))
                {
                    if(IsRepresentableInSql(memberB.Type) && IsRepresentableInSql(unwrappedA.Type))
                    {
                        Pairs.Add((GetPath(memberB), exprA, memberB.Type));
                        return true;
                    }
                }

                return false;
            }

            private static bool IsRootedInParameter(MemberExpression node, ParameterExpression target)
            { return GetRoot(node) == target; }

            private bool ContainsParameter(Expression node)
            {
                var checker = new ParameterReferenceChecker(_signalParam);
                checker.Visit(node);
                return checker.Found;
            }

            private static string GetPath(MemberExpression node)
            {
                var parts = new List<string>();
                Expression? current = node;

                while(current is MemberExpression me)
                {
                    parts.Add(me.Member.Name);
                    current = UnwrapConvert(me.Expression!);
                }

                parts.Reverse();
                return string.Join('.', parts);
            }

            private sealed class ParameterReferenceChecker(ParameterExpression target) : ExpressionVisitor
            {
                public bool Found { get; private set; }

                protected override Expression VisitParameter(ParameterExpression node)
                {
                    if(node == target)
                        Found = true;
                    return base.VisitParameter(node);
                }
            }
        }
    }
}