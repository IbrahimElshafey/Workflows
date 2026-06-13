using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Workflows.Analyzers
{
    internal record WorkflowSchema
    {
        public int SchemaVersion          { get; init; }
        public string WorkflowName        { get; init; } = "";
        public string AssemblyRootNamespace { get; init; } = "";
        public List<StatePropertySchema> StateProperties { get; init; } = new();
        public List<BasicBlockSchema> MainCfg { get; init; } = new();
        public List<SubWorkflowSchema> SubWorkflows { get; init; } = new();
    }

    internal record StatePropertySchema
    {
        public string Name     { get; init; } = "";
        public string TypeFqn  { get; init; } = "";
        public bool   Nullable { get; init; }
    }

    internal record BasicBlockSchema
    {
        public int      BlockIndex    { get; init; }
        public string?  YieldWaitType { get; init; }
        public string?  YieldWaitName { get; init; }
        public int[]    Edges         { get; init; } = Array.Empty<int>();
        public int      YieldOrdinal  { get; init; }
    }

    internal record SubWorkflowSchema
    {
        public string MethodName     { get; init; } = "";
        public string MethodFullPath { get; init; } = "";
        public List<BasicBlockSchema> BasicBlocks { get; init; } = new();
    }

    internal static class WorkflowSchemaExtractor
    {
        public static WorkflowSchema? Extract(INamedTypeSymbol classSymbol, SemanticModel model,
                                              ClassDeclarationSyntax classSyntax)
        {
            var attr = classSymbol.GetAttributes()
                .FirstOrDefault(a => a.AttributeClass?.Name == "WorkflowAttribute");
            if (attr == null) return null;

            string name    = (string)attr.ConstructorArguments[0].Value!;
            int    version = (int)attr.ConstructorArguments[1].Value!;

            string startMethod = attr.NamedArguments
                .FirstOrDefault(kv => kv.Key == "StartMethod").Value.Value as string ?? "Run";

            var runMethod = classSymbol.GetMembers()
                .OfType<IMethodSymbol>()
                .FirstOrDefault(m => m.Name == startMethod && ReturnsIAsyncEnumerableWait(m));

            var runSyntax = runMethod?.DeclaringSyntaxReferences
                .FirstOrDefault()?.GetSyntax() as MethodDeclarationSyntax;

            var stateTypeSymbol = WorkflowAnalyzer.GetWorkflowStateType(classSymbol) as INamedTypeSymbol;
            var targetSymbol = stateTypeSymbol ?? classSymbol;

            var stateProps = targetSymbol.GetMembers()
                .OfType<IPropertySymbol>()
                .Where(p => !p.IsStatic && p.SetMethod != null && p.DeclaredAccessibility == Accessibility.Public)
                .Select(p => new StatePropertySchema
                {
                    Name     = p.Name,
                    TypeFqn  = p.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    Nullable = p.NullableAnnotation == NullableAnnotation.Annotated
                }).ToList();

            var mainCfg = runSyntax != null
                ? ExtractCfg(runSyntax, model)
                : new List<BasicBlockSchema>();

            var subWorkflows = classSymbol.GetMembers()
                .OfType<IMethodSymbol>()
                .Where(m => m.GetAttributes().Any(a => a.AttributeClass?.Name == "SubWorkflowAttribute")
                         && ReturnsIAsyncEnumerableWait(m))
                .Select(m =>
                {
                    var syntax = m.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax()
                        as MethodDeclarationSyntax;
                    return new SubWorkflowSchema
                    {
                        MethodName   = m.Name,
                        MethodFullPath = classSymbol.ToDisplayString() + "." + m.Name,
                        BasicBlocks  = syntax != null ? ExtractCfg(syntax, model)
                                                      : new List<BasicBlockSchema>()
                    };
                }).ToList();

            return new WorkflowSchema
            {
                SchemaVersion   = version,
                WorkflowName    = name,
                AssemblyRootNamespace = classSymbol.ContainingAssembly.Name,
                StateProperties = stateProps,
                MainCfg         = mainCfg,
                SubWorkflows    = subWorkflows
            };
        }

        private static bool ReturnsIAsyncEnumerableWait(IMethodSymbol method)
        {
            return method.ReturnType.ToDisplayString().StartsWith("System.Collections.Generic.IAsyncEnumerable<")
                && method.ReturnType.ToDisplayString().Contains("Wait");
        }

        private static List<BasicBlockSchema> ExtractCfg(MethodDeclarationSyntax method, SemanticModel model)
        {
            var yields = method.DescendantNodes()
                .OfType<YieldStatementSyntax>()
                .Where(y => y.IsKind(SyntaxKind.YieldReturnStatement))
                .ToList();

            var blocks = new List<BasicBlockSchema>();
            for (int i = 0; i < yields.Count; i++)
            {
                var expr = yields[i].Expression!;
                var typeInfo = model.GetTypeInfo(expr);
                string? waitType = typeInfo.Type?.Name;
                string? waitName = TryExtractWaitName(expr, model);

                blocks.Add(new BasicBlockSchema
                {
                    BlockIndex = i + 1,
                    YieldWaitType = waitType,
                    YieldWaitName = waitName,
                    Edges = i + 2 <= yields.Count
                        ? new[] { i + 2 }
                        : Array.Empty<int>(),
                    YieldOrdinal = i + 1
                });
            }
            return blocks;
        }

        private static string? TryExtractWaitName(ExpressionSyntax expr, SemanticModel model)
        {
            if (expr is InvocationExpressionSyntax inv)
            {
                var root = GetRootInvocation(inv);
                foreach (var arg in root.ArgumentList.Arguments.Reverse())
                {
                    var constVal = model.GetConstantValue(arg.Expression);
                    if (constVal.HasValue && constVal.Value is string name && name.Length > 0)
                        return name;
                }
            }
            return null;
        }

        private static InvocationExpressionSyntax GetRootInvocation(InvocationExpressionSyntax inv)
        {
            var expr = inv.Expression;
            while (expr is MemberAccessExpressionSyntax mae
                   && mae.Expression is InvocationExpressionSyntax inner)
            {
                expr = inner.Expression;
                inv = inner;
            }
            return inv;
        }
    }
}

namespace System.Runtime.CompilerServices
{
    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    internal static class IsExternalInit { }
}

