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
        public List<ComplexTypeSchema> NestedTypes { get; init; } = new();
        public List<BasicBlockSchema> MainCfg { get; init; } = new();
        public List<SubWorkflowSchema> SubWorkflows { get; init; } = new();
    }

    internal record StatePropertySchema
    {
        public string Name     { get; init; } = "";
        public string TypeFqn  { get; init; } = "";
        public bool   Nullable { get; init; }
    }

    internal record ComplexTypeSchema
    {
        public string TypeFqn { get; init; } = "";
        public List<StatePropertySchema> Properties { get; init; } = new();
    }

    internal record BasicBlockSchema
    {
        public int      BlockIndex    { get; init; }
        public string?  YieldWaitType { get; init; }
        public string?  YieldWaitName { get; init; }
        public string?  WithStateType { get; init; }
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

            var nestedTypes = new List<ComplexTypeSchema>();
            var visitedTypes = new HashSet<string>();

            foreach (var prop in targetSymbol.GetMembers().OfType<IPropertySymbol>())
            {
                ExtractNestedTypesRecursively(prop.Type, nestedTypes, visitedTypes);
            }

            var mainCfg = runSyntax != null
                ? ExtractCfg(runSyntax, model, nestedTypes, visitedTypes)
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
                        BasicBlocks  = syntax != null ? ExtractCfg(syntax, model, nestedTypes, visitedTypes)
                                                      : new List<BasicBlockSchema>()
                    };
                }).ToList();

            return new WorkflowSchema
            {
                SchemaVersion   = version,
                WorkflowName    = name,
                AssemblyRootNamespace = classSymbol.ContainingAssembly.Name,
                StateProperties = stateProps,
                NestedTypes     = nestedTypes,
                MainCfg         = mainCfg,
                SubWorkflows    = subWorkflows
            };
        }

        private static bool ReturnsIAsyncEnumerableWait(IMethodSymbol method)
        {
            return method.ReturnType.ToDisplayString().StartsWith("System.Collections.Generic.IAsyncEnumerable<")
                && method.ReturnType.ToDisplayString().Contains("Wait");
        }

        private static List<BasicBlockSchema> ExtractCfg(MethodDeclarationSyntax method, SemanticModel model,
                                                        List<ComplexTypeSchema> nestedTypes, HashSet<string> visitedTypes)
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
                string? withStateType = TryExtractWithStateType(expr, model, nestedTypes, visitedTypes);

                blocks.Add(new BasicBlockSchema
                {
                    BlockIndex = i + 1,
                    YieldWaitType = waitType,
                    YieldWaitName = waitName,
                    WithStateType = withStateType,
                    Edges = i + 2 <= yields.Count
                        ? new[] { i + 2 }
                        : Array.Empty<int>(),
                    YieldOrdinal = i + 1
                });
            }
            return blocks;
        }

        private static string? TryExtractWithStateType(ExpressionSyntax expr, SemanticModel model,
                                                        List<ComplexTypeSchema> nestedTypes, HashSet<string> visitedTypes)
        {
            if (expr is InvocationExpressionSyntax inv)
            {
                var curr = inv;
                while (curr != null)
                {
                    if (curr.Expression is MemberAccessExpressionSyntax mae && mae.Name.Identifier.Text == "WithState")
                    {
                        var symbol = model.GetSymbolInfo(curr).Symbol as IMethodSymbol;
                        if (symbol != null && symbol.TypeArguments.Length > 0)
                        {
                            var stateType = symbol.TypeArguments[0];
                            ExtractNestedTypesRecursively(stateType, nestedTypes, visitedTypes);
                            return stateType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                        }
                        if (curr.ArgumentList.Arguments.Count > 0)
                        {
                            var argType = model.GetTypeInfo(curr.ArgumentList.Arguments[0].Expression).Type;
                            if (argType != null)
                            {
                                ExtractNestedTypesRecursively(argType, nestedTypes, visitedTypes);
                                return argType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                            }
                        }
                    }

                    if (curr.Expression is MemberAccessExpressionSyntax memberAccess &&
                        memberAccess.Expression is InvocationExpressionSyntax innerInv)
                    {
                        curr = innerInv;
                    }
                    else
                    {
                        break;
                    }
                }
            }
            return null;
        }

        private static void ExtractNestedTypesRecursively(ITypeSymbol typeSymbol, List<ComplexTypeSchema> nestedTypes, HashSet<string> visitedTypes)
        {
            if (typeSymbol == null) return;

            // Handle collections / generics (List<T>, IEnumerable<T>, Dictionary<K,V>)
            if (typeSymbol is INamedTypeSymbol namedType && namedType.IsGenericType)
            {
                foreach (var typeArg in namedType.TypeArguments)
                {
                    ExtractNestedTypesRecursively(typeArg, nestedTypes, visitedTypes);
                }
            }

            // Handle arrays (T[])
            if (typeSymbol is IArrayTypeSymbol arrayType)
            {
                ExtractNestedTypesRecursively(arrayType.ElementType, nestedTypes, visitedTypes);
                return;
            }

            string fqn = typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            if (visitedTypes.Contains(fqn)) return;

            // Filter out system primitives, string, DateTime, Guid, etc.
            if (typeSymbol.SpecialType != SpecialType.None ||
                fqn.StartsWith("global::System.") ||
                typeSymbol.TypeKind == TypeKind.Enum)
            {
                return;
            }

            if (typeSymbol is INamedTypeSymbol complexSymbol &&
                (complexSymbol.TypeKind == TypeKind.Class || complexSymbol.TypeKind == TypeKind.Struct))
            {
                visitedTypes.Add(fqn);

                var props = complexSymbol.GetMembers()
                    .OfType<IPropertySymbol>()
                    .Where(p => !p.IsStatic && p.DeclaredAccessibility == Accessibility.Public)
                    .Select(p => new StatePropertySchema
                    {
                        Name = p.Name,
                        TypeFqn = p.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                        Nullable = p.NullableAnnotation == NullableAnnotation.Annotated
                    }).ToList();

                nestedTypes.Add(new ComplexTypeSchema
                {
                    TypeFqn = fqn,
                    Properties = props
                });

                foreach (var prop in props)
                {
                    var propTypeSymbol = complexSymbol.GetMembers()
                        .OfType<IPropertySymbol>()
                        .FirstOrDefault(p => p.Name == prop.Name)?.Type;

                    if (propTypeSymbol != null)
                    {
                        ExtractNestedTypesRecursively(propTypeSymbol, nestedTypes, visitedTypes);
                    }
                }
            }
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

