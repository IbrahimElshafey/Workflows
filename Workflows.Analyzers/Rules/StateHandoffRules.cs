using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Workflows.Analyzers.Rules
{
    /// <summary>
    /// Flags '.WithState(...)' calls that hand off values which are already reachable from the
    /// persisted workflow instance: container members and the workflow input state parameter.
    /// Only stack locals and method parameters genuinely need the explicit hand-off.
    /// </summary>
    public static class StateHandoffRules
    {
        public static void AnalyzeWithStateInvocation(
            SyntaxNodeAnalysisContext context,
            InvocationExpressionSyntax invocation,
            DiagnosticDescriptor containerMemberDescriptor,
            DiagnosticDescriptor inputStateDescriptor)
        {
            var methodSymbol = context.SemanticModel.GetSymbolInfo(invocation).Symbol as IMethodSymbol;
            if (methodSymbol == null || methodSymbol.Name != "WithState")
                return;

            if (!IsWorkflowSurface(methodSymbol.ContainingType))
                return;

            var containingMethodNode = invocation.FirstAncestorOrSelf<MethodDeclarationSyntax>();
            if (containingMethodNode == null)
                return;

            var containingMethodSymbol = context.SemanticModel.GetDeclaredSymbol(containingMethodNode);
            if (containingMethodSymbol == null || !WorkflowAnalyzer.InheritsFromWorkflowContainer(containingMethodSymbol.ContainingType))
                return;

            var argument = invocation.ArgumentList.Arguments.FirstOrDefault();
            if (argument == null)
                return;

            var expression = Unwrap(argument.Expression);

            // .WithState(this) - the whole container instance
            if (expression is ThisExpressionSyntax)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    containerMemberDescriptor, argument.GetLocation(), "this"));
                return;
            }

            var symbol = context.SemanticModel.GetSymbolInfo(expression).Symbol;
            if (symbol == null)
                return;

            // .WithState(state) - a parameter of a workflow/sub-workflow method. The runner keeps that
            // very object in Locals["state"] and re-invokes the method with it, so it stays reachable.
            if (symbol is IParameterSymbol parameterSymbol &&
                SymbolEqualityComparer.Default.Equals(parameterSymbol.ContainingSymbol, containingMethodSymbol) &&
                IsWorkflowDefinitionMethod(containingMethodSymbol))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    inputStateDescriptor, argument.GetLocation(), parameterSymbol.Name));
                return;
            }

            // .WithState(SomeContainerProperty) / .WithState(this.SomeProperty)
            if ((symbol is IPropertySymbol || symbol is IFieldSymbol) &&
                !symbol.IsStatic &&
                IsContainerMember(symbol.ContainingType, containingMethodSymbol.ContainingType))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    containerMemberDescriptor, argument.GetLocation(), expression.ToString()));
            }
        }

        /// <summary>
        /// A workflow (or sub-workflow) method is re-invoked by the runner on every resume with the state
        /// object kept in the machine state, so its parameter stays reachable from inside callbacks.
        /// </summary>
        private static bool IsWorkflowDefinitionMethod(ISymbol symbol)
        {
            if (symbol is not IMethodSymbol methodSymbol)
                return false;

            return methodSymbol.ReturnType is INamedTypeSymbol returnType &&
                   returnType.Name == "IAsyncEnumerable" &&
                   returnType.ContainingNamespace?.ToDisplayString() == "System.Collections.Generic";
        }

        private static bool IsWorkflowSurface(INamedTypeSymbol containingType)
        {
            if (containingType == null)
                return false;

            if (containingType.ContainingNamespace != null &&
                containingType.ContainingNamespace.ToDisplayString().StartsWith("Workflows"))
                return true;

            return containingType.Name.Contains("Builder") || containingType.Name.Contains("Wait");
        }

        private static bool IsContainerMember(ITypeSymbol memberContainingType, INamedTypeSymbol workflowType)
        {
            for (var type = workflowType; type != null; type = type.BaseType)
            {
                if (SymbolEqualityComparer.Default.Equals(type, memberContainingType))
                    return true;
            }

            return false;
        }

        private static ExpressionSyntax Unwrap(ExpressionSyntax expression)
        {
            while (expression is ParenthesizedExpressionSyntax parenthesized)
                expression = parenthesized.Expression;

            return expression;
        }
    }
}