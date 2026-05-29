using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Workflows.Analyzers.Rules
{
    public static class WaitRules
    {
        public static void AnalyzeNamedType(
            SymbolAnalysisContext context,
            DiagnosticDescriptor wf204,
            DiagnosticDescriptor wf205,
            DiagnosticDescriptor wf206,
            DiagnosticDescriptor wf207,
            DiagnosticDescriptor wf208)
        {
            var typeSymbol = (INamedTypeSymbol)context.Symbol;
            if (typeSymbol.TypeKind != TypeKind.Class) return;

            bool inheritsFromWorkflow = WorkflowAnalyzer.InheritsFromWorkflowContainer(typeSymbol);

            // Check methods of the class for sub-workflow constraints
            foreach (var member in typeSymbol.GetMembers().OfType<IMethodSymbol>())
            {
                bool hasSubWorkflowAttr = member.GetAttributes().Any(a => a.AttributeClass?.Name == "SubWorkflowAttribute" || a.AttributeClass?.Name == "SubWorkflow");

                if (hasSubWorkflowAttr && !inheritsFromWorkflow)
                {
                    // WF207: [SubWorkflow] in a class that is not a WorkflowContainer
                    var syntax = member.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() as MethodDeclarationSyntax;
                    var loc = syntax?.Identifier.GetLocation() ?? member.Locations.FirstOrDefault() ?? Location.None;
                    context.ReportDiagnostic(Diagnostic.Create(wf207, loc, member.Name));
                    continue;
                }

                if (inheritsFromWorkflow)
                {
                    bool isRunMethod = member.Name == "Run";
                    bool returnsWaitAsyncEnum = WorkflowAnalyzer.IsWorkflowMethod(member);

                    if (returnsWaitAsyncEnum && !isRunMethod)
                    {
                        // It is a sub-workflow method
                        if (!hasSubWorkflowAttr)
                        {
                            // WF208: Missing [SubWorkflow] attribute
                            var syntax = member.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() as MethodDeclarationSyntax;
                            var loc = syntax?.Identifier.GetLocation() ?? member.Locations.FirstOrDefault() ?? Location.None;
                            context.ReportDiagnostic(Diagnostic.Create(wf208, loc, member.Name));
                        }

                        if (member.DeclaredAccessibility != Accessibility.Private)
                        {
                            // WF206: Sub-workflow must be private
                            var syntax = member.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() as MethodDeclarationSyntax;
                            var loc = syntax?.Identifier.GetLocation() ?? member.Locations.FirstOrDefault() ?? Location.None;
                            context.ReportDiagnostic(Diagnostic.Create(wf206, loc, member.Name));
                        }
                    }
                    else if (hasSubWorkflowAttr)
                    {
                        // Decorated with [SubWorkflow] but doesn't return async enum or is named Run
                        if (member.DeclaredAccessibility != Accessibility.Private)
                        {
                            var syntax = member.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() as MethodDeclarationSyntax;
                            var loc = syntax?.Identifier.GetLocation() ?? member.Locations.FirstOrDefault() ?? Location.None;
                            context.ReportDiagnostic(Diagnostic.Create(wf206, loc, member.Name));
                        }
                    }
                }
            }

            if (inheritsFromWorkflow)
            {
                // Gather all wait definitions and check for missing/duplicate names
                var seenNames = new Dictionary<string, (Location Location, string Name)>(StringComparer.OrdinalIgnoreCase);

                foreach (var syntaxRef in typeSymbol.DeclaringSyntaxReferences)
                {
                    var classSyntax = syntaxRef.GetSyntax() as ClassDeclarationSyntax;
                    if (classSyntax == null) continue;

                    var semanticModel = context.Compilation.GetSemanticModel(classSyntax.SyntaxTree);
                    var invocations = classSyntax.DescendantNodes().OfType<InvocationExpressionSyntax>();

                    foreach (var invocation in invocations)
                    {
                        var methodSymbol = semanticModel.GetSymbolInfo(invocation).Symbol as IMethodSymbol;
                        if (methodSymbol == null) continue;

                        var contType = methodSymbol.ContainingType;
                        if (contType == null || !WorkflowAnalyzer.InheritsFromWorkflowContainer(contType)) continue;

                        string paramName = null;
                        var methodName = methodSymbol.Name;

                        if (methodName == "WaitSignal" || methodName == "WaitUntil" ||
                            methodName == "WaitDelay" || methodName == "WaitSubWorkflow" ||
                            methodName == "WaitGroup")
                        {
                            paramName = "name";
                        }
                        else if (methodName == "ExecuteCommand")
                        {
                            paramName = "commandName";
                        }
                        else if (methodName == "Compensate")
                        {
                            paramName = "compasenationToken";
                        }

                        if (paramName != null)
                        {
                            var nameArgInfo = GetWaitNameFromArgument(invocation, methodSymbol, paramName, semanticModel);

                            if (nameArgInfo == null)
                            {
                                // Omitted / missing wait name
                                var loc = invocation.GetLocation();
                                context.ReportDiagnostic(Diagnostic.Create(wf204, loc, methodName));
                            }
                            else
                            {
                                var (argSyntax, nameVal) = nameArgInfo.Value;

                                if (string.IsNullOrWhiteSpace(nameVal))
                                {
                                    // Empty / null wait name
                                    var loc = argSyntax.GetLocation();
                                    context.ReportDiagnostic(Diagnostic.Create(wf204, loc, methodName));
                                }
                                else if (nameVal != "DYNAMIC_EXPRESSION")
                                {
                                    // Valid constant wait name. Check for duplicate within the same class context.
                                    var loc = argSyntax.GetLocation();
                                    if (seenNames.TryGetValue(nameVal, out var existing))
                                    {
                                        context.ReportDiagnostic(Diagnostic.Create(wf205, loc, nameVal, typeSymbol.Name));
                                    }
                                    else
                                    {
                                        seenNames[nameVal] = (loc, nameVal);
                                    }
                                }
                            }
                        }

                        // WF208 check for WaitSubWorkflow parameter target
                        if (methodName == "WaitSubWorkflow" && invocation.ArgumentList.Arguments.Count > 0)
                        {
                            var subWorkflowArg = invocation.ArgumentList.Arguments[0].Expression;
                            if (subWorkflowArg is InvocationExpressionSyntax subInv)
                            {
                                var subMethodSymbol = semanticModel.GetSymbolInfo(subInv).Symbol as IMethodSymbol;
                                if (subMethodSymbol != null)
                                {
                                    bool hasAttr = subMethodSymbol.GetAttributes().Any(a => a.AttributeClass?.Name == "SubWorkflowAttribute" || a.AttributeClass?.Name == "SubWorkflow");
                                    if (!hasAttr)
                                    {
                                        var loc = subInv.GetLocation();
                                        context.ReportDiagnostic(Diagnostic.Create(wf208, loc, subMethodSymbol.Name));
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }

        private static (ArgumentSyntax Argument, string? NameVal)? GetWaitNameFromArgument(
            InvocationExpressionSyntax invocation,
            IMethodSymbol methodSymbol,
            string parameterName,
            SemanticModel semanticModel)
        {
            var parameter = methodSymbol.Parameters.FirstOrDefault(p => p.Name == parameterName);
            if (parameter == null) return null;

            int parameterIndex = methodSymbol.Parameters.IndexOf(parameter);
            ArgumentSyntax matchingArgument = null;

            // Check named arguments
            foreach (var arg in invocation.ArgumentList.Arguments)
            {
                if (arg.NameColon != null && arg.NameColon.Name.Identifier.Text == parameterName)
                {
                    matchingArgument = arg;
                    break;
                }
            }

            // Check positional arguments
            if (matchingArgument == null && parameterIndex < invocation.ArgumentList.Arguments.Count)
            {
                var arg = invocation.ArgumentList.Arguments[parameterIndex];
                if (arg.NameColon == null)
                {
                    matchingArgument = arg;
                }
            }

            if (matchingArgument == null) return null;

            var optionalVal = semanticModel.GetConstantValue(matchingArgument.Expression);
            if (optionalVal.HasValue)
            {
                return (matchingArgument, optionalVal.Value?.ToString());
            }

            // If not constant, return the argument but with a placeholder so we don't flag as missing
            return (matchingArgument, "DYNAMIC_EXPRESSION");
        }
    }
}
