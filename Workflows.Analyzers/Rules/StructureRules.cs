using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Workflows.Analyzers.Rules
{
    public static class StructureRules
    {
        public static void AnalyzeNamedType(SymbolAnalysisContext context, DiagnosticDescriptor wf201, DiagnosticDescriptor wf202, DiagnosticDescriptor wf209)
        {
            var typeSymbol = (INamedTypeSymbol)context.Symbol;
            if (typeSymbol.TypeKind != TypeKind.Class) return;

            if (WorkflowAnalyzer.InheritsFromWorkflowContainer(typeSymbol))
            {
                // WF201: Sealed class check
                if (!typeSymbol.IsSealed)
                {
                    var syntax = typeSymbol.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() as ClassDeclarationSyntax;
                    if (syntax != null)
                    {
                        var diagnostic = Diagnostic.Create(wf201, syntax.Identifier.GetLocation(), typeSymbol.Name);
                        context.ReportDiagnostic(diagnostic);
                    }
                }

                // WF209: Missing Workflow Attribute
                if (!typeSymbol.IsAbstract && typeSymbol.Name != "TestWorkflow")
                {
                    var hasWorkflowAttr = typeSymbol.GetAttributes()
                        .Any(attr => attr.AttributeClass?.ToDisplayString() == "Workflows.Definition.WorkflowAttribute" ||
                                    attr.AttributeClass?.Name == "WorkflowAttribute");
                    if (!hasWorkflowAttr)
                    {
                        var syntax = typeSymbol.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() as ClassDeclarationSyntax;
                        if (syntax != null)
                        {
                            var diagnostic = Diagnostic.Create(wf209, syntax.Identifier.GetLocation(), typeSymbol.Name);
                            context.ReportDiagnostic(diagnostic);
                        }
                    }
                }

                // WF202: No overloaded workflow methods
                var workflowMethods = typeSymbol.GetMembers()
                    .OfType<IMethodSymbol>()
                    .Where(WorkflowAnalyzer.IsWorkflowMethod)
                    .ToList();

                var overloadedNames = workflowMethods
                    .GroupBy(m => m.Name)
                    .Where(g => g.Count() > 1)
                    .Select(g => g.Key)
                    .ToImmutableHashSet();

                foreach (var method in workflowMethods)
                {
                    if (overloadedNames.Contains(method.Name))
                    {
                        var syntax = method.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() as MethodDeclarationSyntax;
                        if (syntax != null)
                        {
                            var diagnostic = Diagnostic.Create(wf202, syntax.Identifier.GetLocation(), method.Name);
                            context.ReportDiagnostic(diagnostic);
                        }
                    }
                }
            }
        }
    }
}
