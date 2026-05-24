using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Workflows.Analyzers.Rules
{
    public static class ClosureRules
    {
        public static void AnalyzeInvocation(SyntaxNodeAnalysisContext context, InvocationExpressionSyntax invocation, IMethodSymbol methodSymbol, DiagnosticDescriptor descriptor)
        {
            var name = methodSymbol.Name;
            if (name == "MatchIf" || name == "AfterMatch" || name == "OnResult" ||
                name == "OnFailure" || name == "OnCanceled" || name == "RegisterCompensation" ||
                name == "GroupMatchFilter")
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
                            if (!HasAllowClosuresExemption(lambda, context.SemanticModel))
                            {
                                var captures = FindClosureCaptures(lambda, context.SemanticModel);
                                foreach (var capture in captures)
                                {
                                    var diagnostic = Diagnostic.Create(descriptor, capture.GetLocation(), capture.ToString());
                                    context.ReportDiagnostic(diagnostic);
                                }
                            }
                        }
                    }
                }
            }
        }

        private static bool HasAllowClosuresExemption(SyntaxNode node, SemanticModel semanticModel)
        {
            var parent = node.Parent;
            while (parent != null)
            {
                if (parent is MethodDeclarationSyntax methodDecl)
                {
                    var methodSymbol = semanticModel.GetDeclaredSymbol(methodDecl);
                    if (methodSymbol != null && HasAllowClosuresAttribute(methodSymbol)) return true;
                }
                else if (parent is ClassDeclarationSyntax classDecl)
                {
                    var classSymbol = semanticModel.GetDeclaredSymbol(classDecl);
                    if (classSymbol != null && HasAllowClosuresAttribute(classSymbol)) return true;
                }
                parent = parent.Parent;
            }
            return false;
        }

        private static bool HasAllowClosuresAttribute(ISymbol symbol)
        {
            foreach (var attr in symbol.GetAttributes())
            {
                var attrName = attr.AttributeClass?.Name;
                if (attrName == "WorkflowAttribute" || attrName == "Workflow")
                {
                    foreach (var arg in attr.NamedArguments)
                    {
                        if (arg.Key == "AllowClosures" && arg.Value.Value is bool allow && allow)
                        {
                            return true;
                        }
                    }
                }
            }
            return false;
        }

        private static List<SyntaxNode> FindClosureCaptures(AnonymousFunctionExpressionSyntax lambda, SemanticModel semanticModel)
        {
            var lambdaSymbol = semanticModel.GetSymbolInfo(lambda).Symbol;
            var walker = new ClosureCaptureWalker(semanticModel, lambdaSymbol, lambda);
            walker.Visit(lambda.Body);
            return walker.ClosureCaptures;
        }

        private class ClosureCaptureWalker : CSharpSyntaxWalker
        {
            private readonly SemanticModel _semanticModel;
            private readonly ISymbol? _lambdaSymbol;
            private readonly SyntaxNode _lambdaNode;
            private readonly HashSet<ISymbol> _declaredInside = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
            private readonly List<SyntaxNode> _closureCaptures = new List<SyntaxNode>();

            public ClosureCaptureWalker(SemanticModel semanticModel, ISymbol? lambdaSymbol, SyntaxNode lambdaNode)
            {
                _semanticModel = semanticModel;
                _lambdaSymbol = lambdaSymbol;
                _lambdaNode = lambdaNode;
            }

            public List<SyntaxNode> ClosureCaptures => _closureCaptures;

            public override void VisitParameter(ParameterSyntax node)
            {
                var symbol = _semanticModel.GetDeclaredSymbol(node);
                if (symbol != null) _declaredInside.Add(symbol);
                base.VisitParameter(node);
            }

            public override void VisitVariableDeclarator(VariableDeclaratorSyntax node)
            {
                var symbol = _semanticModel.GetDeclaredSymbol(node);
                if (symbol != null) _declaredInside.Add(symbol);
                base.VisitVariableDeclarator(node);
            }

            public override void VisitSingleVariableDesignation(SingleVariableDesignationSyntax node)
            {
                var symbol = _semanticModel.GetDeclaredSymbol(node);
                if (symbol != null) _declaredInside.Add(symbol);
                base.VisitSingleVariableDesignation(node);
            }

            public override void VisitIdentifierName(IdentifierNameSyntax node)
            {
                var symbol = _semanticModel.GetSymbolInfo(node).Symbol;
                if (symbol != null)
                {
                    // 1. Ignore static members
                    if (symbol.IsStatic)
                    {
                        base.VisitIdentifierName(node);
                        return;
                    }

                    // 2. Ignore the method itself (recursive/method group calls)
                    if (symbol.ContainingSymbol != null && SymbolEqualityComparer.Default.Equals(symbol.ContainingSymbol, _lambdaSymbol))
                    {
                        base.VisitIdentifierName(node);
                        return;
                    }

                    // 3. FIX: Safely ignore class-level Fields and Properties (Domain State)
                    if (symbol is IFieldSymbol || symbol is IPropertySymbol || symbol is IMethodSymbol)
                    {
                        base.VisitIdentifierName(node);
                        return;
                    }

                    // 4. Capture ONLY true local variables and parameters
                    if (symbol is ILocalSymbol || symbol is IParameterSymbol)
                    {
                        _closureCaptures.Add(node);
                    }
                }

                base.VisitIdentifierName(node);
            }

            // NOTE: Also ensure VisitThisExpression doesn't erroneously flag the 'this' keyword!
            public override void VisitThisExpression(ThisExpressionSyntax node)
            {
                // Remove _closureCaptures.Add(node); 
                base.VisitThisExpression(node);
            }

            public override void VisitBaseExpression(BaseExpressionSyntax node)
            {
                _closureCaptures.Add(node);
                base.VisitBaseExpression(node);
            }
        }
    }
}
