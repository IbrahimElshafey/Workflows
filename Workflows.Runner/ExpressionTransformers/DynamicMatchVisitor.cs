using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text.Json;
using Workflows.Shared;

namespace Workflows.Runner.ExpressionTransformers
{
    internal class DynamicMatchVisitor : ExpressionVisitor
    {
        private readonly LambdaExpression _typedLambda;
        private bool _isUnsupportedNodeFound;
        private bool _hasOrOperator;
        private readonly HashSet<Expression> _partialNodes = new();
        private static readonly ConstantExpression UnknownNode = Expression.Constant("__UNKNOWN__");

        private ParameterExpression _signalParam;
        private ParameterExpression _stateParam;
        private ParameterExpression _instanceParam;

        private ParameterExpression _jsonSignalParam;
        private ParameterExpression _jsonStateParam;
        private ParameterExpression _jsonInstanceParam;

        public LambdaExpression TypedResult => _typedLambda;

        public Expression<Func<JsonElement, JsonElement, JsonElement, bool>> Result { get; private set; }

        public bool IsFullMatch => !_isUnsupportedNodeFound && Result != null;

        public List<(string SignalPath, Expression OtherSide, Type PropertyType)> PotentialExactMatchPairs { get; private set; } = new();

        public bool IsExactMatchFullMatch { get; private set; } = true;

        public DynamicMatchVisitor(LambdaExpression typedLambda)
        {
            _typedLambda = typedLambda ?? throw new ArgumentNullException(nameof(typedLambda));
        }

        public void Build()
        {
            // Lock onto the exact generic parameters from the upstream normalization phase
            _signalParam = _typedLambda.Parameters.Count > 0 ? _typedLambda.Parameters[0] : null;
            _stateParam = _typedLambda.Parameters.Count > 1 ? _typedLambda.Parameters[1] : null;
            _instanceParam = _typedLambda.Parameters.Count > 2 ? _typedLambda.Parameters[2] : null;

            // Orchestrator Tier 1.5 JSON Target signatures
            _jsonSignalParam = Expression.Parameter(typeof(JsonElement), "signalData");
            _jsonStateParam = Expression.Parameter(typeof(JsonElement), "stateData");
            _jsonInstanceParam = Expression.Parameter(typeof(JsonElement), "instanceData");

            var visitedBody = Visit(_typedLambda.Body);

            if (_isUnsupportedNodeFound || visitedBody == UnknownNode)
            {
                Result = null;
                IsExactMatchFullMatch = false;
                PotentialExactMatchPairs.Clear();
                return;
            }

            if (_hasOrOperator || PotentialExactMatchPairs.Count == 0)
            {
                PotentialExactMatchPairs.Clear();
                IsExactMatchFullMatch = false;
            }

            Result = Expression.Lambda<Func<JsonElement, JsonElement, JsonElement, bool>>(
                visitedBody,
                _jsonSignalParam,
                _jsonStateParam,
                _jsonInstanceParam
            );
        }

        protected override Expression VisitBinary(BinaryExpression node)
        {
            if (node.NodeType == ExpressionType.Equal)
            {
                var leftIsSignal = IsRootedInParameter(node.Left, _signalParam);
                var rightIsSignal = IsRootedInParameter(node.Right, _signalParam);

                if (leftIsSignal && !ContainsParameter(node.Right, _signalParam))
                {
                    var signalExpr = UnwrapConvert(node.Left) as MemberExpression;
                    if (signalExpr != null && IsSupportedExactMatchType(signalExpr.Type))
                    {
                        PotentialExactMatchPairs.Add((GetPath(signalExpr), UnwrapConvert(node.Right), signalExpr.Type));
                    }
                    else
                    {
                        IsExactMatchFullMatch = false;
                    }
                }
                else if (rightIsSignal && !ContainsParameter(node.Left, _signalParam))
                {
                    var signalExpr = UnwrapConvert(node.Right) as MemberExpression;
                    if (signalExpr != null && IsSupportedExactMatchType(signalExpr.Type))
                    {
                        PotentialExactMatchPairs.Add((GetPath(signalExpr), UnwrapConvert(node.Left), signalExpr.Type));
                    }
                    else
                    {
                        IsExactMatchFullMatch = false;
                    }
                }
                else
                {
                    IsExactMatchFullMatch = false;
                }
            }
            else if (node.NodeType == ExpressionType.OrElse)
            {
                IsExactMatchFullMatch = false;
                _hasOrOperator = true;
            }
            else if (node.NodeType != ExpressionType.AndAlso)
            {
                IsExactMatchFullMatch = false;
            }

            var visitedLeft = Visit(node.Left);
            var visitedRight = Visit(node.Right);

            if (visitedLeft == UnknownNode || visitedRight == UnknownNode)
            {
                _isUnsupportedNodeFound = true;
                return UnknownNode;
            }

            if (node.NodeType == ExpressionType.Equal)
            {
                var method = typeof(object).GetMethod(nameof(object.Equals), new[] { typeof(object), typeof(object) });
                var convertedLeft = Expression.Convert(visitedLeft, typeof(object));
                var convertedRight = Expression.Convert(visitedRight, typeof(object));
                return Expression.Call(method, convertedLeft, convertedRight);
            }

            return Expression.MakeBinary(node.NodeType, visitedLeft, visitedRight);
        }

        protected override Expression VisitMethodCall(MethodCallExpression node)
        {
            if (node.Method.Name == "Equals" && node.Object != null && node.Arguments.Count == 1)
            {
                var leftIsSignal = IsRootedInParameter(node.Object, _signalParam);
                var rightIsSignal = IsRootedInParameter(node.Arguments[0], _signalParam);

                if (leftIsSignal && !ContainsParameter(node.Arguments[0], _signalParam))
                {
                    var signalExpr = UnwrapConvert(node.Object) as MemberExpression;
                    if (signalExpr != null && IsSupportedExactMatchType(signalExpr.Type))
                    {
                        PotentialExactMatchPairs.Add((GetPath(signalExpr), UnwrapConvert(node.Arguments[0]), signalExpr.Type));
                    }
                    else
                    {
                        IsExactMatchFullMatch = false;
                    }
                }
                else if (rightIsSignal && !ContainsParameter(node.Object, _signalParam))
                {
                    var signalExpr = UnwrapConvert(node.Arguments[0]) as MemberExpression;
                    if (signalExpr != null && IsSupportedExactMatchType(signalExpr.Type))
                    {
                        PotentialExactMatchPairs.Add((GetPath(signalExpr), UnwrapConvert(node.Object), signalExpr.Type));
                    }
                    else
                    {
                        IsExactMatchFullMatch = false;
                    }
                }
                else
                {
                    IsExactMatchFullMatch = false;
                }

                var visitedObject = Visit(node.Object);
                var visitedArg = Visit(node.Arguments[0]);

                if (visitedObject == UnknownNode || visitedArg == UnknownNode)
                {
                    _isUnsupportedNodeFound = true;
                    return UnknownNode;
                }

                var method = typeof(object).GetMethod(nameof(object.Equals), new[] { typeof(object), typeof(object) });
                return Expression.Call(method, Expression.Convert(visitedObject, typeof(object)), Expression.Convert(visitedArg, typeof(object)));
            }
            else if (node.Method.Name == "Equals" && node.Method.IsStatic && node.Arguments.Count == 2 && node.Method.DeclaringType == typeof(string))
            {
                var firstArg = node.Arguments[0];
                var secondArg = node.Arguments[1];

                var leftIsSignal = IsRootedInParameter(firstArg, _signalParam);
                var rightIsSignal = IsRootedInParameter(secondArg, _signalParam);

                if (leftIsSignal && !ContainsParameter(secondArg, _signalParam))
                {
                    var signalExpr = UnwrapConvert(firstArg) as MemberExpression;
                    if (signalExpr != null && IsSupportedExactMatchType(signalExpr.Type))
                    {
                        PotentialExactMatchPairs.Add((GetPath(signalExpr), UnwrapConvert(secondArg), signalExpr.Type));
                    }
                    else
                    {
                        IsExactMatchFullMatch = false;
                    }
                }
                else if (rightIsSignal && !ContainsParameter(firstArg, _signalParam))
                {
                    var signalExpr = UnwrapConvert(secondArg) as MemberExpression;
                    if (signalExpr != null && IsSupportedExactMatchType(signalExpr.Type))
                    {
                        PotentialExactMatchPairs.Add((GetPath(signalExpr), UnwrapConvert(firstArg), signalExpr.Type));
                    }
                    else
                    {
                        IsExactMatchFullMatch = false;
                    }
                }
                else
                {
                    IsExactMatchFullMatch = false;
                }

                var visitedLeft = Visit(firstArg);
                var visitedRight = Visit(secondArg);

                if (visitedLeft == UnknownNode || visitedRight == UnknownNode)
                {
                    _isUnsupportedNodeFound = true;
                    return UnknownNode;
                }

                var method = typeof(object).GetMethod(nameof(object.Equals), new[] { typeof(object), typeof(object) });
                return Expression.Call(method, Expression.Convert(visitedLeft, typeof(object)), Expression.Convert(visitedRight, typeof(object)));
            }

            _isUnsupportedNodeFound = true;
            IsExactMatchFullMatch = false;
            return UnknownNode;
        }

        protected override Expression VisitMember(MemberExpression node)
        {
            var parentType = node.Expression?.Type;
            if (parentType != null)
            {
                var underlyingType = Nullable.GetUnderlyingType(parentType) ?? parentType;
                if (underlyingType.IsPrimitive || 
                    underlyingType == typeof(string) || 
                    underlyingType == typeof(decimal) || 
                    underlyingType == typeof(DateTime) || 
                    underlyingType == typeof(TimeSpan) || 
                    underlyingType.IsEnum)
                {
                    _isUnsupportedNodeFound = true;
                    return UnknownNode;
                }
            }

            var root = GetRoot(node);

            if (root == _signalParam) return BuildJsonGet(_jsonSignalParam, GetPath(node), node.Type);
            if (root == _stateParam) return BuildJsonGet(_jsonStateParam, GetPath(node), node.Type);
            if (root == _instanceParam) return BuildJsonGet(_jsonInstanceParam, GetPath(node), node.Type);

            return base.VisitMember(node);
        }

        private Expression BuildJsonGet(ParameterExpression jsonParam, string path, Type targetType)
        {
            var method = typeof(Workflows.Shared.JsonElementExtensions)
                .GetMethod(nameof(Workflows.Shared.JsonElementExtensions.Get))
                .MakeGenericMethod(targetType);

            return Expression.Call(method, jsonParam, Expression.Constant(path));
        }

        private static ParameterExpression GetRoot(Expression node)
        {
            while (node is MemberExpression memberExpr)
            {
                node = memberExpr.Expression;
                node = UnwrapConvert(node);
            }
            return node as ParameterExpression;
        }

        private static string GetPath(MemberExpression node)
        {
            var parts = new List<string>();
            Expression current = node;

            while (current is MemberExpression me)
            {
                parts.Add(me.Member.Name);
                current = me.Expression;
                current = UnwrapConvert(current);
            }

            parts.Reverse();
            return string.Join(".", parts);
        }

        private static bool IsRootedInParameter(Expression node, ParameterExpression target)
        {
            if (target == null || node == null) return false;
            node = UnwrapConvert(node);
            return node is MemberExpression me && GetRoot(me) == target;
        }

        private static Expression UnwrapConvert(Expression expr)
        {
            while (expr is UnaryExpression unary && (expr.NodeType == ExpressionType.Convert || expr.NodeType == ExpressionType.ConvertChecked))
            {
                expr = unary.Operand;
            }
            return expr;
        }

        private bool ContainsParameter(Expression node, ParameterExpression target)
        {
            if (target == null) return false;
            var checker = new ParameterReferenceChecker(target);
            checker.Visit(node);
            return checker.Found;
        }

        private static bool IsSupportedExactMatchType(Type type)
        {
            if (type == null) return false;
            type = Nullable.GetUnderlyingType(type) ?? type;
            return type.IsPrimitive || 
                   type == typeof(string) || 
                   type == typeof(decimal) || 
                   type.IsEnum;
        }

        private sealed class ParameterReferenceChecker : ExpressionVisitor
        {
            private readonly ParameterExpression _target;
            public bool Found { get; private set; }

            public ParameterReferenceChecker(ParameterExpression target)
            {
                _target = target;
            }

            protected override Expression VisitParameter(ParameterExpression node)
            {
                if (node == _target) Found = true;
                return base.VisitParameter(node);
            }
        }
    }
}