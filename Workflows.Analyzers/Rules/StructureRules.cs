using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Workflows.Analyzers.Rules
{
    public static class StructureRules
    {
        public static void AnalyzeNamedType(SymbolAnalysisContext context, DiagnosticDescriptor wf201, DiagnosticDescriptor wf202, DiagnosticDescriptor wf209, DiagnosticDescriptor wf210)
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

                // WF210: Missing Run Method check
                if (!typeSymbol.IsAbstract && typeSymbol.Name != "TestWorkflow")
                {
                    var workflowAttr = typeSymbol.GetAttributes().FirstOrDefault(a => 
                        a.AttributeClass?.ToDisplayString() == "Workflows.Definition.WorkflowAttribute" ||
                        a.AttributeClass?.Name == "WorkflowAttribute");

                    string startMethodName = "Run";
                    if (workflowAttr != null)
                    {
                        var startMethodArg = workflowAttr.NamedArguments.FirstOrDefault(kv => kv.Key == "StartMethod").Value;
                        if (startMethodArg.Value is string customStartMethodName && !string.IsNullOrEmpty(customStartMethodName))
                        {
                            startMethodName = customStartMethodName;
                        }
                    }

                    var stateType = WorkflowAnalyzer.GetWorkflowStateType(typeSymbol);
                    var hasRunMethod = typeSymbol.GetMembers()
                        .OfType<IMethodSymbol>()
                        .Any(m => m.Name == startMethodName && 
                                  WorkflowAnalyzer.IsWorkflowMethod(m) &&
                                  (m.Parameters.Length == 0 || (stateType != null && m.Parameters.Length == 1 && SymbolEqualityComparer.Default.Equals(m.Parameters[0].Type, stateType))));
                    if (!hasRunMethod)
                    {
                        var syntax = typeSymbol.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() as ClassDeclarationSyntax;
                        if (syntax != null)
                        {
                            var diagnostic = Diagnostic.Create(wf210, syntax.Identifier.GetLocation(), typeSymbol.Name);
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
