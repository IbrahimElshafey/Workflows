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
            WF000, WF001, WF002, WF003, WF004, WF005, WF103, WF201, WF202, WF203);

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
            var typeSymbol = (INamedTypeSymbol)context.Symbol;
            if (typeSymbol.TypeKind != TypeKind.Class) return;

            if (InheritsFromWorkflowContainer(typeSymbol))
            {
                // WF201: Sealed class check
                if (!typeSymbol.IsSealed)
                {
                    var syntax = typeSymbol.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() as ClassDeclarationSyntax;
                    if (syntax != null)
                    {
                        var diagnostic = Diagnostic.Create(WF201, syntax.Identifier.GetLocation(), typeSymbol.Name);
                        context.ReportDiagnostic(diagnostic);
                    }
                }

                // WF202: No overloaded workflow methods
                var workflowMethods = typeSymbol.GetMembers()
                    .OfType<IMethodSymbol>()
                    .Where(IsWorkflowMethod)
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
                            var diagnostic = Diagnostic.Create(WF202, syntax.Identifier.GetLocation(), method.Name);
                            context.ReportDiagnostic(diagnostic);
                        }
                    }
                }
            }
        }

        private static void AnalyzeAwaitForeach(SyntaxNodeAnalysisContext context)
        {
            var forEachStatement = (ForEachStatementSyntax)context.Node;
            if (forEachStatement.AwaitKeyword == default) return;

            var containingMethod = forEachStatement.FirstAncestorOrSelf<MethodDeclarationSyntax>();
            if (containingMethod == null) return;

            var methodSymbol = context.SemanticModel.GetDeclaredSymbol(containingMethod);
            if (!IsWorkflowMethod(methodSymbol)) return;

            var expr = forEachStatement.Expression;
            var typeInfo = context.SemanticModel.GetTypeInfo(expr);
            var type = typeInfo.Type as INamedTypeSymbol;

            if (type != null && (type.ConstructedFrom?.ToDisplayString() == "System.Collections.Generic.IAsyncEnumerable<T>" || type.Name == "IAsyncEnumerable"))
            {
                var typeArg = type.TypeArguments.FirstOrDefault();
                if (typeArg != null && (InheritsFromWait(typeArg) || typeArg.Name == "Wait"))
                {
                    var diagnostic = Diagnostic.Create(WF001, expr.GetLocation());
                    context.ReportDiagnostic(diagnostic);
                }
            }
        }

        private static void AnalyzeVariableDeclaration(SyntaxNodeAnalysisContext context)
        {
            var variableDecl = (VariableDeclarationSyntax)context.Node;
            var containingMethod = variableDecl.FirstAncestorOrSelf<MethodDeclarationSyntax>();
            if (containingMethod == null) return;

            var methodSymbol = context.SemanticModel.GetDeclaredSymbol(containingMethod);
            if (!IsWorkflowMethod(methodSymbol)) return;

            foreach (var variable in variableDecl.Variables)
            {
                var symbol = context.SemanticModel.GetDeclaredSymbol(variable) as ILocalSymbol;
                if (symbol == null) continue;

                var type = symbol.Type;
                if (type == null) continue;

                if (ImplementsDisposable(type))
                {
                    var diagnostic = Diagnostic.Create(WF002, variable.GetLocation(), symbol.Name, type.ToDisplayString());
                    context.ReportDiagnostic(diagnostic);
                }
            }
        }

        private static void AnalyzeYieldStatement(SyntaxNodeAnalysisContext context)
        {
            var yieldStatement = (YieldStatementSyntax)context.Node;
            var containingMethod = yieldStatement.FirstAncestorOrSelf<MethodDeclarationSyntax>();
            if (containingMethod == null) return;

            var methodSymbol = context.SemanticModel.GetDeclaredSymbol(containingMethod);
            if (!IsWorkflowMethod(methodSymbol)) return;

            var parent = yieldStatement.Parent;
            while (parent != null && parent != containingMethod)
            {
                if (parent is UsingStatementSyntax)
                {
                    var diagnostic = Diagnostic.Create(WF003, yieldStatement.GetLocation());
                    context.ReportDiagnostic(diagnostic);
                    return;
                }

                if (parent is BlockSyntax block)
                {
                    foreach (var stmt in block.Statements)
                    {
                        if (stmt.SpanStart >= yieldStatement.SpanStart) break;
                        if (stmt is LocalDeclarationStatementSyntax localDecl && localDecl.UsingKeyword != default)
                        {
                            var diagnostic = Diagnostic.Create(WF003, yieldStatement.GetLocation());
                            context.ReportDiagnostic(diagnostic);
                            return;
                        }
                    }
                }
                parent = parent.Parent;
            }
        }

        private static void AnalyzeMemberAccessExpression(SyntaxNodeAnalysisContext context)
        {
            var memberAccess = (MemberAccessExpressionSyntax)context.Node;
            var containingMethod = memberAccess.FirstAncestorOrSelf<MethodDeclarationSyntax>();
            if (containingMethod == null) return;

            var methodSymbol = context.SemanticModel.GetDeclaredSymbol(containingMethod);
            if (!IsWorkflowMethod(methodSymbol)) return;

            var symbol = context.SemanticModel.GetSymbolInfo(memberAccess).Symbol;
            if (symbol != null)
            {
                var displayString = symbol.ToDisplayString();
                if (displayString.StartsWith("System.Threading.AsyncLocal") && displayString.EndsWith(".Value"))
                {
                    var diagnostic = Diagnostic.Create(WF004, memberAccess.GetLocation());
                    context.ReportDiagnostic(diagnostic);
                    return;
                }
                if (displayString.Contains("HttpContext"))
                {
                    var diagnostic = Diagnostic.Create(WF004, memberAccess.GetLocation());
                    context.ReportDiagnostic(diagnostic);
                    return;
                }
            }
        }

        private static void AnalyzeIdentifierName(SyntaxNodeAnalysisContext context)
        {
            var identifier = (IdentifierNameSyntax)context.Node;
            if (identifier.Parent is MemberAccessExpressionSyntax memberAccess && memberAccess.Name == identifier)
            {
                return;
            }

            var containingMethod = identifier.FirstAncestorOrSelf<MethodDeclarationSyntax>();
            if (containingMethod == null) return;

            var methodSymbol = context.SemanticModel.GetDeclaredSymbol(containingMethod);
            if (!IsWorkflowMethod(methodSymbol)) return;

            var symbol = context.SemanticModel.GetSymbolInfo(identifier).Symbol;
            if (symbol != null)
            {
                var displayString = symbol.ToDisplayString();
                if (displayString.Contains("System.Threading.AsyncLocal") && displayString.Contains(".Value"))
                {
                    var diagnostic = Diagnostic.Create(WF004, identifier.GetLocation());
                    context.ReportDiagnostic(diagnostic);
                }
                else if (displayString.Contains("HttpContext"))
                {
                    var diagnostic = Diagnostic.Create(WF004, identifier.GetLocation());
                    context.ReportDiagnostic(diagnostic);
                }
            }
        }

        private static void AnalyzeAssignmentExpression(SyntaxNodeAnalysisContext context)
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
                var diagnostic = Diagnostic.Create(WF103, assignment.Left.GetLocation(), symbol.Name);
                context.ReportDiagnostic(diagnostic);
            }
        }

        private static void AnalyzeInvocationExpression(SyntaxNodeAnalysisContext context)
        {
            var invocation = (InvocationExpressionSyntax)context.Node;

            AnalyzeWithStateInvocation(context, invocation);

            var methodSymbol = context.SemanticModel.GetSymbolInfo(invocation).Symbol as IMethodSymbol;
            if (methodSymbol == null) return;

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
                            // WF000: Check closures
                            if (!HasAllowClosuresExemption(lambda, context.SemanticModel))
                            {
                                var captures = FindClosureCaptures(lambda, context.SemanticModel);
                                foreach (var capture in captures)
                                {
                                    var diagnostic = Diagnostic.Create(WF000, capture.GetLocation(), capture.ToString());
                                    context.ReportDiagnostic(diagnostic);
                                }
                            }

                            // WF203: MatchIf purity warning
                            if (name == "MatchIf")
                            {
                                var walker = new MatchIfBodyWalker(context.SemanticModel);
                                walker.Visit(lambda.Body);
                                foreach (var call in walker.ImpureInvocations)
                                {
                                    var diagnostic = Diagnostic.Create(WF203, call.GetLocation(), call.ToString());
                                    context.ReportDiagnostic(diagnostic);
                                }
                            }
                        }
                    }
                }
            }
        }

        private static void AnalyzeWithStateInvocation(SyntaxNodeAnalysisContext context, InvocationExpressionSyntax invocation)
        {
            var methodSymbol = context.SemanticModel.GetSymbolInfo(invocation).Symbol as IMethodSymbol;
            if (methodSymbol == null || methodSymbol.Name != "WithState") return;

            var containingMethodNode = invocation.FirstAncestorOrSelf<MethodDeclarationSyntax>();
            if (containingMethodNode == null) return;

            var containingMethodSymbol = context.SemanticModel.GetDeclaredSymbol(containingMethodNode);
            if (containingMethodSymbol == null) return;

            var containingType = containingMethodSymbol.ContainingType;
            if (!InheritsFromWorkflowContainer(containingType)) return;

            bool isSyncHelper = !IsWorkflowMethod(containingMethodSymbol) &&
                                 containingMethodSymbol.ReturnType.ToDisplayString() != "System.Threading.Tasks.Task" &&
                                 !containingMethodSymbol.ReturnType.ToDisplayString().StartsWith("System.Threading.Tasks.Task<") &&
                                 containingMethodSymbol.ReturnType.ToDisplayString() != "System.Threading.Tasks.ValueTask" &&
                                 !containingMethodSymbol.ReturnType.ToDisplayString().StartsWith("System.Threading.Tasks.ValueTask<");

            if (!isSyncHelper) return;

            foreach (var arg in invocation.ArgumentList.Arguments)
            {
                var symbol = context.SemanticModel.GetSymbolInfo(arg.Expression).Symbol;
                if (symbol != null && (symbol is ILocalSymbol || symbol is IParameterSymbol))
                {
                    if (SymbolEqualityComparer.Default.Equals(symbol.ContainingSymbol, containingMethodSymbol))
                    {
                        var diagnostic = Diagnostic.Create(WF005, arg.GetLocation(), symbol.Name, containingMethodSymbol.Name);
                        context.ReportDiagnostic(diagnostic);
                    }
                }
            }
        }

        private static bool InheritsFromWorkflowContainer(INamedTypeSymbol? typeSymbol)
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

        private static bool InheritsFromWait(ITypeSymbol? typeSymbol)
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

        private static bool IsWorkflowMethod(IMethodSymbol? methodSymbol)
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

        private static bool ImplementsDisposable(ITypeSymbol typeSymbol)
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
                if (node.Parent is MemberAccessExpressionSyntax memberAccess && memberAccess.Name == node)
                {
                    base.VisitIdentifierName(node);
                    return;
                }

                var symbolInfo = _semanticModel.GetSymbolInfo(node);
                var symbol = symbolInfo.Symbol ?? symbolInfo.CandidateSymbols.FirstOrDefault();
                if (symbol != null)
                {
                    if (symbol is INamespaceSymbol || symbol is ITypeSymbol)
                    {
                        base.VisitIdentifierName(node);
                        return;
                    }

                    if (symbol is ILocalSymbol || symbol is IParameterSymbol || symbol is IFieldSymbol || symbol is IPropertySymbol || symbol is IMethodSymbol)
                    {
                        if (_declaredInside.Contains(symbol))
                        {
                            base.VisitIdentifierName(node);
                            return;
                        }

                        if (symbol.IsStatic)
                        {
                            base.VisitIdentifierName(node);
                            return;
                        }

                        if (symbol.ContainingSymbol != null && SymbolEqualityComparer.Default.Equals(symbol.ContainingSymbol, _lambdaSymbol))
                        {
                            base.VisitIdentifierName(node);
                            return;
                        }

                        if (symbol.ContainingType != null)
                        {
                            _closureCaptures.Add(node);
                        }
                        else if (symbol is ILocalSymbol || symbol is IParameterSymbol)
                        {
                            _closureCaptures.Add(node);
                        }
                    }
                }
                base.VisitIdentifierName(node);
            }

            public override void VisitThisExpression(ThisExpressionSyntax node)
            {
                _closureCaptures.Add(node);
                base.VisitThisExpression(node);
            }

            public override void VisitBaseExpression(BaseExpressionSyntax node)
            {
                _closureCaptures.Add(node);
                base.VisitBaseExpression(node);
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
