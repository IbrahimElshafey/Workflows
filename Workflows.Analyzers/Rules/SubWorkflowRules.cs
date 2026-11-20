using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Workflows.Analyzers.Rules
{
    public static class SubWorkflowRules
    {
        /// <summary>
        /// Flags a sub-workflow that hands itself to WaitSubWorkflow. Unbounded self-recursion grows the
        /// persisted snapshot without limit; bounded recursion is fine, so this is a warning rather than an error.
        /// </summary>
        public static void AnalyzeInvocation(
            SyntaxNodeAnalysisContext context,
            InvocationExpressionSyntax invocation,
            IMethodSymbol methodSymbol,
            DiagnosticDescriptor wf400)
        {
            if (methodSymbol.Name != "WaitSubWorkflow") return;
            if (!WorkflowAnalyzer.InheritsFromWorkflowContainer(methodSymbol.ContainingType)) return;
            if (invocation.ArgumentList.Arguments.Count == 0) return;

            var argument = invocation.ArgumentList.Arguments[0].Expression;
            var targetSymbol = context.SemanticModel.GetSymbolInfo(argument).Symbol as IMethodSymbol;
            if (targetSymbol == null) return;

            var enclosingMethod = context.SemanticModel.GetEnclosingSymbol(invocation.SpanStart) as IMethodSymbol;
            if (enclosingMethod == null) return;

            if (!SymbolEqualityComparer.Default.Equals(targetSymbol.OriginalDefinition, enclosingMethod.OriginalDefinition)) return;

            context.ReportDiagnostic(Diagnostic.Create(wf400, argument.GetLocation(), targetSymbol.Name));
        }
    }
}