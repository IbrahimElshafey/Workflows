using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Workflows.Analyzers
{
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public class WorkflowAnalyzer : DiagnosticAnalyzer
    {
        public const string DiagnosticIdWF000 = "WF000";
        public const string DiagnosticIdWF001 = "WF001";
        public const string DiagnosticIdWF002 = "WF002";
        public const string DiagnosticIdWF003 = "WF003";
        public const string DiagnosticIdWF004 = "WF004";
        public const string DiagnosticIdWF005 = "WF005";
        public const string DiagnosticIdWF006 = "WF006";
        public const string DiagnosticIdWF103 = "WF103";
        public const string DiagnosticIdWF201 = "WF201";
        public const string DiagnosticIdWF202 = "WF202";
        public const string DiagnosticIdWF203 = "WF203";

        private static readonly DiagnosticDescriptor WF000 = new DiagnosticDescriptor(
            DiagnosticIdWF000,
            "Strict No-Closure Enforcement",
            "Lambda expression captures variable '{0}' from the outer scope, which violates the strict no-closure constraint. Pass state explicitly using '.WithState(state)' or map it to a class-level property.",
            "Workflow.Safety",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true,
            description: "All builder callbacks must be closure-free to allow compilation and persistence.");

        private static readonly DiagnosticDescriptor WF001 = new DiagnosticDescriptor(
            DiagnosticIdWF001,
            "Unsupported Sub-Workflow Execution",
            "Iterating directly over a sub-workflow using 'await foreach' is unsupported. Yield execution to the Runner via 'yield return WaitSubWorkflow(...)' instead.",
            "Workflow.Serialization",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        private static readonly DiagnosticDescriptor WF002 = new DiagnosticDescriptor(
            DiagnosticIdWF002,
            "Unserializable Workflow State",
            "Local variable '{0}' of type '{1}' implements IDisposable/IAsyncDisposable, which cannot be held across yield boundaries",
            "Workflow.Serialization",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        private static readonly DiagnosticDescriptor WF003 = new DiagnosticDescriptor(
            DiagnosticIdWF003,
            "Yield Inside Using Block",
            "Yield return statement is not allowed inside a using block or while a using declaration is active",
            "Workflow.Serialization",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        private static readonly DiagnosticDescriptor WF004 = new DiagnosticDescriptor(
            DiagnosticIdWF004,
            "Invalid AsyncLocal Capture",
            "Access to AsyncLocal/HttpContext is invalid within a workflow. Ambient thread contexts do not survive workflow dehydration/rehydration.",
            "Workflow.Serialization",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        private static readonly DiagnosticDescriptor WF005 = new DiagnosticDescriptor(
            DiagnosticIdWF005,
            "State Machine Blindspot (Synchronous Locals)",
            "Passing local variable '{0}' from synchronous helper method '{1}' to '.WithState()' is unsafe as the state will vanish on workflow rehydration",
            "Workflow.Safety",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        private static readonly DiagnosticDescriptor WF006 = new DiagnosticDescriptor(
            DiagnosticIdWF006,
            "Anonymous Types Disallowed for State",
            "Passing anonymous type to '.WithState()' is disallowed as anonymous types cannot be reliably deserialized across runs.",
            "Workflow.Serialization",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        private static readonly DiagnosticDescriptor WF103 = new DiagnosticDescriptor(
            DiagnosticIdWF103,
            "Mutable Class Property inside Parallel Closures",
            "Mutating class field/property '{0}' inside a parallel GroupWait closure can cause race conditions",
            "Workflow.Purity",
            DiagnosticSeverity.Warning,
            isEnabledByDefault: true);

        private static readonly DiagnosticDescriptor WF201 = new DiagnosticDescriptor(
            DiagnosticIdWF201,
            "Unsealed Workflow Container",
            "Workflow container class '{0}' must be marked as sealed to guarantee perfect AssemblyQualifiedName resolution",
            "Workflow.Structure",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        private static readonly DiagnosticDescriptor WF202 = new DiagnosticDescriptor(
            DiagnosticIdWF202,
            "Overloaded Workflow Methods",
            "Workflow method '{0}' returning IAsyncEnumerable<Wait> must not be overloaded to prevent routing ambiguity",
            "Workflow.Structure",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        private static readonly DiagnosticDescriptor WF203 = new DiagnosticDescriptor(
            DiagnosticIdWF203,
            "Impure Match Logic",
            "Using custom C# method call '{0}' inside MatchIf is warned. It cannot be parsed by Orchestrator for Tier 1.5 indexing, causing fallback to heavy Runner evaluation.",
            "Workflow.Routing",
            DiagnosticSeverity.Warning,
            isEnabledByDefault: true);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(
            WF000, WF001, WF002, WF003, WF004, WF005, WF006, WF103, WF201, WF202, WF203);

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();

            context.RegisterSymbolAction(AnalyzeNamedType, SymbolKind.NamedType);
            context.RegisterSyntaxNodeAction(AnalyzeInvocationExpression, SyntaxKind.InvocationExpression);
            context.RegisterSyntaxNodeAction(AnalyzeYieldStatement, SyntaxKind.YieldReturnStatement, SyntaxKind.YieldBreakStatement);
            context.RegisterSyntaxNodeAction(AnalyzeAwaitForeach, SyntaxKind.ForEachStatement);
            context.RegisterSyntaxNodeAction(AnalyzeVariableDeclaration, SyntaxKind.VariableDeclaration);
            context.RegisterSyntaxNodeAction(AnalyzeMemberAccessExpression, SyntaxKind.SimpleMemberAccessExpression);
            context.RegisterSyntaxNodeAction(AnalyzeAssignmentExpression, SyntaxKind.SimpleAssignmentExpression);
            context.RegisterSyntaxNodeAction(AnalyzeIdentifierName, SyntaxKind.IdentifierName);
        }

        private static void AnalyzeNamedType(SymbolAnalysisContext context)
        {
            Rules.StructureRules.AnalyzeNamedType(context, WF201, WF202);
        }

        private static void AnalyzeAwaitForeach(SyntaxNodeAnalysisContext context)
        {
            Rules.SerializationRules.AnalyzeAwaitForeach(context, WF001);
        }

        private static void AnalyzeVariableDeclaration(SyntaxNodeAnalysisContext context)
        {
            Rules.SerializationRules.AnalyzeVariableDeclaration(context, WF002);
        }

        private static void AnalyzeYieldStatement(SyntaxNodeAnalysisContext context)
        {
            Rules.SerializationRules.AnalyzeYieldStatement(context, WF003);
        }

        private static void AnalyzeMemberAccessExpression(SyntaxNodeAnalysisContext context)
        {
            Rules.SerializationRules.AnalyzeMemberAccessExpression(context, WF004);
        }

        private static void AnalyzeIdentifierName(SyntaxNodeAnalysisContext context)
        {
            Rules.SerializationRules.AnalyzeIdentifierName(context, WF004);
        }

        private static void AnalyzeAssignmentExpression(SyntaxNodeAnalysisContext context)
        {
            Rules.PurityRules.AnalyzeAssignmentExpression(context, WF103);
        }

        private static void AnalyzeInvocationExpression(SyntaxNodeAnalysisContext context)
        {
            var invocation = (InvocationExpressionSyntax)context.Node;
            Rules.SerializationRules.AnalyzeWithStateInvocation(context, invocation, WF005);
            Rules.SerializationRules.AnalyzeWithStateAnonymousType(context, invocation, WF006);

            var methodSymbol = context.SemanticModel.GetSymbolInfo(invocation).Symbol as IMethodSymbol;
            if (methodSymbol == null) return;

            Rules.ClosureRules.AnalyzeInvocation(context, invocation, methodSymbol, WF000);
            Rules.PurityRules.AnalyzeMatchIfPurity(context, invocation, methodSymbol, WF203);
        }

        internal static bool InheritsFromWorkflowContainer(INamedTypeSymbol? typeSymbol)
        {
            while (typeSymbol != null)
            {
                if (typeSymbol.ToDisplayString() == "Workflows.Definition.WorkflowContainer" ||
                    typeSymbol.Name == "WorkflowContainer")
                {
                    return true;
                }
                typeSymbol = typeSymbol.BaseType;
            }
            return false;
        }

        internal static bool InheritsFromWait(ITypeSymbol? typeSymbol)
        {
            while (typeSymbol != null)
            {
                if (typeSymbol.ToDisplayString() == "Workflows.Definition.Wait" ||
                    typeSymbol.Name == "Wait")
                {
                    return true;
                }
                typeSymbol = typeSymbol.BaseType;
            }
            return false;
        }

        internal static bool IsWorkflowMethod(IMethodSymbol? methodSymbol)
        {
            if (methodSymbol == null) return false;
            if (!InheritsFromWorkflowContainer(methodSymbol.ContainingType)) return false;

            var returnType = methodSymbol.ReturnType as INamedTypeSymbol;
            if (returnType == null) return false;

            if (returnType.ConstructedFrom?.ToDisplayString() == "System.Collections.Generic.IAsyncEnumerable<T>" ||
                returnType.Name == "IAsyncEnumerable")
            {
                var typeArg = returnType.TypeArguments.FirstOrDefault();
                if (typeArg != null && (InheritsFromWait(typeArg) || typeArg.Name == "Wait"))
                {
                    return true;
                }
            }
            return false;
        }

        internal static bool ImplementsDisposable(ITypeSymbol typeSymbol)
        {
            foreach (var iface in typeSymbol.AllInterfaces)
            {
                var name = iface.ToDisplayString();
                if (name == "System.IDisposable" || name == "System.IAsyncDisposable")
                {
                    return true;
                }
            }
            var selfName = typeSymbol.ToDisplayString();
            if (selfName == "System.IDisposable" || selfName == "System.IAsyncDisposable")
            {
                return true;
            }
            return false;
        }
    }
}
