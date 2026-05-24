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
        // ── Public result ────────────────────────────────────────────────────────
        public bool IsFullMatch { get; }
        public IReadOnlyList<string> SignalPaths { get; }
        public Expression<Func<object, object, string[]>>? InstanceMatchExpression { get; }

        public static ExactMatchAnalyzer Create(
            LambdaExpression? typedExpression,
            bool isFullMatch,
            List<(string SignalPath, Expression OtherSide, Type PropertyType)> pairs)
        {
            if (typedExpression is null)
                return new ExactMatchAnalyzer(isFullMatch: false);

            var sorted = pairs
                .OrderBy(p => p.SignalPath)
                .ToList();

            var paths = sorted.Select(p => p.SignalPath).ToList();
            var matchExpr = BuildInstanceMatchExpression(
                typedExpression,
                sorted);

            return new ExactMatchAnalyzer(
                isFullMatch: isFullMatch,
                signalPaths: paths,
                instanceMatchExpression: matchExpr);
        }

        private ExactMatchAnalyzer(bool isFullMatch, List<string>? signalPaths = null, Expression<Func<object, object, string[]>>? instanceMatchExpression = null)
        {
            IsFullMatch = isFullMatch;
            SignalPaths = signalPaths ?? new List<string>();
            InstanceMatchExpression = instanceMatchExpression;
        }

        public static string? FormatValue(object? value, Type targetType)
        {
            if (value == null) return null;
            var underlyingTargetType = Nullable.GetUnderlyingType(targetType) ?? targetType;
            if (underlyingTargetType.IsEnum)
            {
                if (value.GetType() == underlyingTargetType)
                {
                    return value.ToString();
                }
                try
                {
                    var enumValue = Enum.ToObject(underlyingTargetType, value);
                    return enumValue.ToString();
                }
                catch
                {
                    return value.ToString();
                }
            }
            return Convert.ToString(value, CultureInfo.InvariantCulture);
        }

        private static Expression<Func<object, object, string[]>> BuildInstanceMatchExpression(
            LambdaExpression originalLambda,
            List<(string SignalPath, Expression OtherSide, Type PropertyType)> sortedPairs)
        {
            var paramSignal = Expression.Parameter(typeof(object), "signal");
            var paramState = Expression.Parameter(typeof(object), "state");

            var originalStateParam = originalLambda.Parameters.Count > 1 ? originalLambda.Parameters[1] : null;
            var originalInstanceParam = originalLambda.Parameters.Count > 2 ? originalLambda.Parameters[2] : null;

            var replacerMap = new Dictionary<ParameterExpression, Expression>();
            if (originalStateParam != null)
                replacerMap[originalStateParam] = Expression.Convert(paramState, originalStateParam.Type);
            if (originalInstanceParam != null)
                replacerMap[originalInstanceParam] = Expression.Convert(paramState, originalInstanceParam.Type);

            var replacer = new ParameterReplacer(replacerMap);
            var stringType = typeof(string);
            var formatMethod = typeof(ExactMatchAnalyzer).GetMethod(nameof(FormatValue), BindingFlags.Public | BindingFlags.Static);

            var initializers = sortedPairs.Select(pair =>
            {
                var replaced = replacer.Visit(pair.OtherSide);
                var converted = Expression.Convert(replaced, typeof(object));
                var targetTypeExpr = Expression.Constant(pair.PropertyType, typeof(Type));
                return Expression.Call(formatMethod!, converted, targetTypeExpr);
            });

            var arrayExpr = Expression.NewArrayInit(stringType, initializers);
            return Expression.Lambda<Func<object, object, string[]>>(arrayExpr, paramSignal, paramState);
        }

        private sealed class ParameterReplacer(Dictionary<ParameterExpression, Expression> map)
            : ExpressionVisitor
        {
            protected override Expression VisitParameter(ParameterExpression node) =>
                map.GetValueOrDefault(node) ?? base.VisitParameter(node);
        }
    }
}