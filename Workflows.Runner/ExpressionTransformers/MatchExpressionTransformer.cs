using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text.Json;
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

            // Step 0: Normalize to Expression<Func<object, object, object, bool>> (signalData, state, instance).
            var matchExpressionNormalizer = new MatchExpressionNormalizer();
            var normalizedExpression = matchExpressionNormalizer.Normalize(matchExpression, workflowInstance);

            // Step 1: Analyze for Tier 1 (SQL Exact Match Extraction)
            var exactMatchAnalyzer = new ExactMatchAnalyzer(matchExpression);
            exactMatchAnalyzer.Analyze();

            // Step 2: Analyze for Tier 1.5 (JsonElement RAM Filter)
            var dynamicVisitor = new DynamicMatchVisitor(normalizedExpression);
            dynamicVisitor.Build();

            // Step 3: Build & Return completely Immutable Result
            return new MatchTransformationResult
            {
                MatchExpression = normalizedExpression,

                // Tier 1 SQL Indexes
                SignalExactMatchPaths = exactMatchAnalyzer.SignalExactMatchPaths,
                InstanceExactMatchExpression = exactMatchAnalyzer.InstanceExactMatchExpression,
                IsExactMatchFullMatch = exactMatchAnalyzer.IsExactMatchFullMatch,

                // Tier 1.5 RAM Pre-filter
                GenericMatchExpression = dynamicVisitor.Result,
                IsGenericMatchFullMatch = dynamicVisitor.IsFullMatch
            };
        }
    }
}