using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;

namespace Workflows.Runner.ExpressionTransformers
{
    internal sealed class ExactMatchAnalyzer
    {
        public bool IsFullMatch { get; }
        public IReadOnlyList<string> SignalPaths { get; }

        // Output factory executable for Tier 1 path exact evaluations
        public Expression<Func<object, object, string[]>> InstanceMatchExpression { get; }

        public static ExactMatchAnalyzer Create(
            LambdaExpression typedExpression,
            bool isFullMatch,
            List<(string SignalPath, Expression OtherSide, Type PropertyType)> pairs)
        {
            if (typedExpression is null)
                return new ExactMatchAnalyzer(isFullMatch: false);

            var sorted = pairs.OrderBy(p => p.SignalPath).ToList();
            var paths = sorted.Select(p => p.SignalPath).ToList();
            var matchExpr = BuildInstanceMatchExpression(typedExpression, sorted);

            return new ExactMatchAnalyzer(isFullMatch, paths, matchExpr);
        }

        private ExactMatchAnalyzer(bool isFullMatch, List<string> signalPaths = null, Expression<Func<object, object, string[]>> instanceMatchExpression = null)
        {
            IsFullMatch = isFullMatch;
            SignalPaths = signalPaths ?? new List<string>();
            InstanceMatchExpression = instanceMatchExpression;
        }

        private static Expression<Func<object, object, string[]>> BuildInstanceMatchExpression(
            LambdaExpression typedExpression,
            List<(string SignalPath, Expression OtherSide, Type PropertyType)> sortedPairs)
        {
            if (sortedPairs.Count == 0)
                return null;

            var paramStateObj = Expression.Parameter(typeof(object), "stateObj");
            var paramInstanceObj = Expression.Parameter(typeof(object), "instanceObj");

            var replacerMap = new Dictionary<ParameterExpression, Expression>();

            // Get the original strongly-typed parameter references injected via MatchExpressionNormalizer
            var originalStateParam = typedExpression.Parameters.Count > 1 ? typedExpression.Parameters[1] : null;
            var originalInstanceParam = typedExpression.Parameters.Count > 2 ? typedExpression.Parameters[2] : null;

            if (originalStateParam != null)
                replacerMap[originalStateParam] = Expression.Convert(paramInstanceObj, originalStateParam.Type);

            if (originalInstanceParam != null)
                replacerMap[originalInstanceParam] = Expression.Convert(paramStateObj, originalInstanceParam.Type);

            var replacer = new ParameterReplacer(replacerMap);
            var stringType = typeof(string);
            var formatMethod = typeof(ExactMatchAnalyzer).GetMethod(nameof(FormatValue), BindingFlags.Public | BindingFlags.Static);

            var initializers = sortedPairs.Select(pair =>
            {
                var replaced = replacer.Visit(pair.OtherSide);
                var converted = Expression.Convert(replaced, typeof(object));
                var targetTypeExpr = Expression.Constant(pair.PropertyType, typeof(Type));
                return Expression.Call(formatMethod, converted, targetTypeExpr);
            });

            var arrayExpr = Expression.NewArrayInit(stringType, initializers);
            return Expression.Lambda<Func<object, object, string[]>>(arrayExpr, paramStateObj, paramInstanceObj);
        }

        public static string FormatValue(object value, Type targetType)
        {
            if (value == null) return null;
            if (targetType != null)
            {
                var underlyingType = Nullable.GetUnderlyingType(targetType) ?? targetType;
                if (underlyingType.IsEnum)
                {
                    try
                    {
                        var enumValue = Enum.ToObject(underlyingType, value);
                        return enumValue.ToString();
                    }
                    catch
                    {
                        // Fallback to default formatting
                    }
                }
            }
            if (value is string s) return s;
            if (value is bool b) return b ? "true" : "false";
            if (value is DateTime dt) return dt.ToUniversalTime().ToString("O");
            if (value is IFormattable formattable) return formattable.ToString(null, CultureInfo.InvariantCulture);
            return value.ToString();
        }

        private sealed class ParameterReplacer : ExpressionVisitor
        {
            private readonly Dictionary<ParameterExpression, Expression> _map;

            public ParameterReplacer(Dictionary<ParameterExpression, Expression> map)
            {
                _map = map;
            }

            protected override Expression VisitParameter(ParameterExpression node)
            {
                if (_map.TryGetValue(node, out var replacement))
                    return replacement;
                return base.VisitParameter(node);
            }
        }
    }
}