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
        public const string DiagnosticIdWF003 = "WF003";
        public const string DiagnosticIdWF004 = "WF004";
        public const string DiagnosticIdWF005 = "WF005";
        public const string DiagnosticIdWF006 = "WF006";
        public const string DiagnosticIdWF007 = "WF007";
        public const string DiagnosticIdWF103 = "WF103";
        public const string DiagnosticIdWF201 = "WF201";
        public const string DiagnosticIdWF202 = "WF202";
        public const string DiagnosticIdWF203 = "WF203";
        public const string DiagnosticIdWF204 = "WF204";
        public const string DiagnosticIdWF205 = "WF205";
        public const string DiagnosticIdWF206 = "WF206";
        public const string DiagnosticIdWF207 = "WF207";
        public const string DiagnosticIdWF208 = "WF208";
        public const string DiagnosticIdWF209 = "WF209";

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

        private static readonly DiagnosticDescriptor WF003 = new DiagnosticDescriptor(
            DiagnosticIdWF003,
            "No Anonymous Types",
            "Passing anonymous type to '.WithState()' is disallowed. Use ValueTuple or record for state to ensure serialization stability.",
            "Workflow.Serialization",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        private static readonly DiagnosticDescriptor WF004 = new DiagnosticDescriptor(
            DiagnosticIdWF004,
            "No Unserializable Locals",
            "Local variable '{0}' of type '{1}' is unserializable (IDisposable, Stream, or SqlConnection cannot be declared as local variables).",
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
            "Yield Inside Using Block",
            "Yield return statement is not allowed inside a using block or while a using declaration is active",
            "Workflow.Serialization",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        private static readonly DiagnosticDescriptor WF007 = new DiagnosticDescriptor(
            DiagnosticIdWF007,
            "Invalid AsyncLocal Capture",
            "Access to AsyncLocal/HttpContext is invalid within a workflow. Ambient thread contexts do not survive workflow dehydration/rehydration.",
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

        private static readonly DiagnosticDescriptor WF204 = new DiagnosticDescriptor(
            DiagnosticIdWF204,
            "Missing Wait Name",
            "Wait name is mandatory. Wait definition '{0}' must specify a non-empty name.",
            "Workflow.Structure",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        private static readonly DiagnosticDescriptor WF205 = new DiagnosticDescriptor(
            DiagnosticIdWF205,
            "Duplicate Wait Name",
            "Wait name '{0}' is already defined in workflow '{1}'. Wait names must be unique within a workflow.",
            "Workflow.Structure",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        private static readonly DiagnosticDescriptor WF206 = new DiagnosticDescriptor(
            DiagnosticIdWF206,
            "Sub-Workflow Must Be Private",
            "Sub-workflow method '{0}' must be private to prevent usage outside of its parent workflow container.",
            "Workflow.Structure",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        private static readonly DiagnosticDescriptor WF207 = new DiagnosticDescriptor(
            DiagnosticIdWF207,
            "Sub-Workflow Attribute in Invalid Class",
            "Sub-workflow method '{0}' is decorated with [SubWorkflow] but the containing class does not inherit from WorkflowContainer.",
            "Workflow.Structure",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        private static readonly DiagnosticDescriptor WF208 = new DiagnosticDescriptor(
            DiagnosticIdWF208,
            "Sub-Workflow Missing Attribute",
            "Sub-workflow method '{0}' must be decorated with [SubWorkflow] attribute.",
            "Workflow.Structure",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        private static readonly DiagnosticDescriptor WF209 = new DiagnosticDescriptor(
            DiagnosticIdWF209,
            "Missing Workflow Attribute",
            "Workflow container class '{0}' is missing [WorkflowAttribute]. Concrete workflow classes must have the attribute.",
            "Workflow.Structure",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(
            WF000, WF001, WF003, WF004, WF005, WF006, WF007, WF103, WF201, WF202, WF203, WF204, WF205, WF206, WF207, WF208, WF209);

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
            Rules.StructureRules.AnalyzeNamedType(context, WF201, WF202, WF209);
            Rules.WaitRules.AnalyzeNamedType(context, WF204, WF205, WF206, WF207, WF208);
        }

        private static void AnalyzeAwaitForeach(SyntaxNodeAnalysisContext context)
        {
            Rules.SerializationRules.AnalyzeAwaitForeach(context, WF001);
        }

        private static void AnalyzeVariableDeclaration(SyntaxNodeAnalysisContext context)
        {
            Rules.SerializationRules.AnalyzeVariableDeclaration(context, WF004);
        }

        private static void AnalyzeYieldStatement(SyntaxNodeAnalysisContext context)
        {
            Rules.SerializationRules.AnalyzeYieldStatement(context, WF006);
        }

        private static void AnalyzeMemberAccessExpression(SyntaxNodeAnalysisContext context)
        {
            Rules.SerializationRules.AnalyzeMemberAccessExpression(context, WF007);
        }

        private static void AnalyzeIdentifierName(SyntaxNodeAnalysisContext context)
        {
            Rules.SerializationRules.AnalyzeIdentifierName(context, WF007);
        }

        private static void AnalyzeAssignmentExpression(SyntaxNodeAnalysisContext context)
        {
            Rules.PurityRules.AnalyzeAssignmentExpression(context, WF103);
        }

        private static void AnalyzeInvocationExpression(SyntaxNodeAnalysisContext context)
        {
            var invocation = (InvocationExpressionSyntax)context.Node;
            Rules.SerializationRules.AnalyzeWithStateInvocation(context, invocation, WF005);
            Rules.SerializationRules.AnalyzeWithStateAnonymousType(context, invocation, WF003);

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
