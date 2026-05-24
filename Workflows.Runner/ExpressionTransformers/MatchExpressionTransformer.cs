using System;
using System.Linq;
using System.Linq.Expressions;
using Workflows.Definition;
using Workflows.Runner.DataObjects;

namespace Workflows.Runner.ExpressionTransformers
{
    // ----------------------------------------------------------------------
    // 1. The Coordinator
    // ----------------------------------------------------------------------

    internal class MatchExpressionTransformer
    {
        /// <param name="matchExpression">The original match lambda from the wait definition.</param>
        public MatchTransformationResult Transform(LambdaExpression matchExpression, WorkflowContainer workflowInstance)
        {
            if (matchExpression == null)
                throw new ArgumentNullException(nameof(matchExpression));

            // Step 1: Analyze for Tier 1.5 (RAM Filter) directly using the ORIGINAL unnormalized expression
            var dynamicVisitor = new DynamicMatchVisitor(matchExpression);
            dynamicVisitor.Build();

            // Step 2: Analyze for Tier 1 (SQL Exact Match Extraction) using the Clean TypedResult
            var exactMatchAnalyzer = ExactMatchAnalyzer.Create(
                dynamicVisitor.TypedResult,
                dynamicVisitor.IsFullMatch && dynamicVisitor.IsExactMatchFullMatch,
                dynamicVisitor.PotentialExactMatchPairs);

            // Step 3: Normalize the original expression ONLY for the Runner (Tier 3 execution)
            var matchExpressionNormalizer = new MatchExpressionNormalizer();
            var normalizedExpression = matchExpressionNormalizer.Normalize(matchExpression, workflowInstance);

            // Step 4: Build & Return Result
            return new MatchTransformationResult
            {
                MatchExpression = normalizedExpression,

                // Tier 1 SQL Indexes
                SignalExactMatchPaths = exactMatchAnalyzer.SignalPaths.ToList(),
                InstanceExactMatchExpression = exactMatchAnalyzer.InstanceMatchExpression,
                IsExactMatchFullMatch = exactMatchAnalyzer.IsFullMatch,

                // Tier 1.5 RAM Pre-filter
                GenericMatchExpression = dynamicVisitor.Result,
                IsGenericMatchFullMatch = dynamicVisitor.IsFullMatch
            };
        }
    }
}