using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Workflows.Analyzers.Rules
{
    public static class PurityRules
    {
        public static void AnalyzeAssignmentExpression(SyntaxNodeAnalysisContext context, DiagnosticDescriptor wf103)
        {
            var assignment = (AssignmentExpressionSyntax)context.Node;
            var lambda = assignment.FirstAncestorOrSelf<AnonymousFunctionExpressionSyntax>();
            if (lambda == null) return;

            bool insideWaitGroup = false;
            var parent = lambda.Parent;
            while (parent != null)
            {
                if (parent is InvocationExpressionSyntax invocation)
                {
                    var methodSymbol = context.SemanticModel.GetSymbolInfo(invocation).Symbol as IMethodSymbol;
                    if (methodSymbol != null && (methodSymbol.Name == "WaitGroup" || methodSymbol.ContainingType?.Name == "WaitGroup"))
                    {
                        insideWaitGroup = true;
                        break;
                    }
                }
                parent = parent.Parent;
            }

            if (!insideWaitGroup) return;

            var symbol = context.SemanticModel.GetSymbolInfo(assignment.Left).Symbol;
            if (symbol != null && (symbol is IFieldSymbol || symbol is IPropertySymbol) && !symbol.IsStatic)
            {
                var diagnostic = Diagnostic.Create(wf103, assignment.Left.GetLocation(), symbol.Name);
                context.ReportDiagnostic(diagnostic);
            }
        }

        public static void AnalyzeMatchIfPurity(SyntaxNodeAnalysisContext context, InvocationExpressionSyntax invocation, IMethodSymbol methodSymbol, DiagnosticDescriptor wf203)
        {
            var name = methodSymbol.Name;
            if (name == "MatchIf")
            {
                var contType = methodSymbol.ContainingType;
                if (contType != null && (contType.ContainingNamespace?.ToDisplayString().StartsWith("Workflows") == true ||
                                         contType.Name.Contains("Builder") ||
                                         contType.Name.Contains("Wait")))
                {
                    foreach (var arg in invocation.ArgumentList.Arguments)
                    {
                        if (arg.Expression is AnonymousFunctionExpressionSyntax lambda)
                        {
                            var walker = new MatchIfBodyWalker(context.SemanticModel);
                            walker.Visit(lambda.Body);
                            foreach (var call in walker.ImpureInvocations)
                            {
                                var diagnostic = Diagnostic.Create(wf203, call.GetLocation(), call.ToString());
                                context.ReportDiagnostic(diagnostic);
                            }
                        }
                    }
                }
            }
        }

        private class MatchIfBodyWalker : CSharpSyntaxWalker
        {
            private readonly SemanticModel _semanticModel;
            private readonly List<InvocationExpressionSyntax> _impureInvocations = new List<InvocationExpressionSyntax>();

            public MatchIfBodyWalker(SemanticModel semanticModel)
            {
                _semanticModel = semanticModel;
            }

            public List<InvocationExpressionSyntax> ImpureInvocations => _impureInvocations;

            public override void VisitInvocationExpression(InvocationExpressionSyntax node)
            {
                var symbol = _semanticModel.GetSymbolInfo(node).Symbol as IMethodSymbol;
                if (symbol != null)
                {
                    var containingType = symbol.ContainingType;
                    if (containingType != null)
                    {
                        var ns = containingType.ContainingNamespace?.ToDisplayString() ?? "";
                        bool isBcl = ns == "System" || ns.StartsWith("System.");
                        if (!isBcl)
                        {
                            bool isSignalMethod = false;
                            if (node.Expression is MemberAccessExpressionSyntax memberAccess)
                            {
                                var lhsSymbol = _semanticModel.GetSymbolInfo(memberAccess.Expression).Symbol;
                                if (lhsSymbol is IParameterSymbol)
                                {
                                    isSignalMethod = true;
                                }
                            }

                            if (!isSignalMethod)
                            {
                                _impureInvocations.Add(node);
                            }
                        }
                    }
                }
                base.VisitInvocationExpression(node);
            }
        }
    }
}
