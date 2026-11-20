using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Workflows.Analyzers.Rules
{
    public static class VersioningRules
    {
        public static void AnalyzeNamedType(SymbolAnalysisContext context, DiagnosticDescriptor wf300, DiagnosticDescriptor wf301)
        {
            var typeSymbol = (INamedTypeSymbol)context.Symbol;
            if (typeSymbol.TypeKind != TypeKind.Class) return;

            var filePath = typeSymbol.Locations.FirstOrDefault()?.SourceTree?.FilePath?.Replace('\\', '/');
            if (filePath == null) return;

            var isArchived = filePath.Contains("/Archive/");

            if (isArchived)
            {
                var folderPath = Path.GetDirectoryName(filePath)?.Replace('\\', '/');
                if (folderPath == null) return;

                var schemaFile = context.Options.AdditionalFiles
                    .FirstOrDefault(f => f.Path.Replace('\\', '/').StartsWith(folderPath) 
                                      && f.Path.EndsWith("_Schema.json"));

                if (schemaFile == null) return;

                var schemaText = schemaFile.GetText(context.CancellationToken)?.ToString();
                if (schemaText == null) return;

                var syntax = typeSymbol.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() as ClassDeclarationSyntax;
                if (syntax == null) return;

                var inheritsWrapper = typeSymbol.BaseType != null && typeSymbol.BaseType.Name == "WorkflowStateWrapper";
                if (inheritsWrapper || WorkflowAnalyzer.InheritsFromWorkflowContainer(typeSymbol))
                {
                    // WF301 must consider the archived state graph, not just the archived type's own
                    // properties: the schema flattens every nested state type (property types, collection
                    // element types) and every `.WithState<T>()` argument into the same `Name` list.
                    var liveProps = CollectStateMemberNames(typeSymbol, context.Compilation);

                    var propMatches = Regex.Matches(schemaText, @"""Name""\s*:\s*""([^""]+)""");
                    foreach (Match match in propMatches)
                    {
                        var propName = match.Groups[1].Value;
                        if (propName == "SchemaVersion" || propName == "WorkflowName" || propName == "AssemblyRootNamespace" || propName == "TypeFqn") continue;

                        if (!liveProps.Contains(propName))
                        {
                            var diagnostic = Diagnostic.Create(wf301, syntax.Identifier.GetLocation(), propName, typeSymbol.Name, "deleted", schemaFile.Path);
                            context.ReportDiagnostic(diagnostic);
                        }
                    }
                }
            }
            else
            {
                if (WorkflowAnalyzer.InheritsFromWorkflowContainer(typeSymbol))
                {
                    var attr = typeSymbol.GetAttributes()
                        .FirstOrDefault(a => a.AttributeClass?.Name == "WorkflowAttribute");
                    if (attr == null) return;

                    string wfName = (string)attr.ConstructorArguments[0].Value!;
                    int wfVersion = (int)attr.ConstructorArguments[1].Value!;

                    var schemaFile = context.Options.AdditionalFiles
                        .FirstOrDefault(f => Path.GetFileName(f.Path) == $"{wfName}_Schema.json");

                    if (schemaFile == null) return;

                    var schemaText = schemaFile.GetText(context.CancellationToken)?.ToString();
                    if (schemaText == null) return;

                    var match = Regex.Match(schemaText, @"""SchemaVersion""\s*:\s*(\d+)");
                    if (match.Success && int.TryParse(match.Groups[1].Value, out var schemaVersion))
                    {
                        if (schemaVersion < wfVersion)
                        {
                            var syntax = attr.ApplicationSyntaxReference!.GetSyntax(context.CancellationToken);
                            
                            var props = ImmutableDictionary<string, string?>.Empty
                                .Add("WorkflowName", wfName)
                                .Add("FromVersion", schemaVersion.ToString())
                                .Add("ToVersion", wfVersion.ToString());

                            var diagnostic = Diagnostic.Create(wf300, syntax.GetLocation(), props, wfName, schemaVersion, wfVersion);
                            context.ReportDiagnostic(diagnostic);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Collects the property names an archived state graph can still reconstruct: the root type, every
        /// nested type reachable through public properties (unwrapping collections, arrays and nullables),
        /// and every type passed to a <c>WithState&lt;T&gt;()</c> call inside the archived type.
        /// </summary>
        private static System.Collections.Generic.HashSet<string> CollectStateMemberNames(INamedTypeSymbol root, Compilation compilation)
        {
            var names = new System.Collections.Generic.HashSet<string>();
            var visited = new System.Collections.Generic.HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
            var pending = new System.Collections.Generic.Queue<INamedTypeSymbol>();
            pending.Enqueue(root);

            for (var guard = 0; pending.Count > 0 && guard < 512; guard++)
            {
                var current = pending.Dequeue();
                if (!visited.Add(current)) continue;

                foreach (var property in current.GetMembers().OfType<IPropertySymbol>())
                {
                    if (property.IsStatic || property.IsIndexer) continue;
                    if (property.DeclaredAccessibility == Accessibility.Public) names.Add(property.Name);

                    foreach (var referenced in Unwrap(property.Type))
                    {
                        if (!visited.Contains(referenced)) pending.Enqueue(referenced);
                    }
                }

                foreach (var withState in CollectWithStateTypes(current, compilation))
                {
                    foreach (var referenced in Unwrap(withState))
                    {
                        if (!visited.Contains(referenced)) pending.Enqueue(referenced);
                    }
                }
            }

            return names;
        }

        private static System.Collections.Generic.IEnumerable<INamedTypeSymbol> Unwrap(ITypeSymbol type)
        {
            switch (type)
            {
                case IArrayTypeSymbol array:
                    foreach (var t in Unwrap(array.ElementType)) yield return t;
                    break;
                case INamedTypeSymbol named:
                    foreach (var arg in named.TypeArguments)
                    {
                        foreach (var t in Unwrap(arg)) yield return t;
                    }
                    if (named.NullableAnnotation == NullableAnnotation.Annotated && named.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T)
                    {
                        yield break;
                    }
                    yield return named;
                    break;
            }
        }

        private static System.Collections.Generic.IEnumerable<INamedTypeSymbol> CollectWithStateTypes(INamedTypeSymbol type, Compilation compilation)
        {
            var results = new System.Collections.Generic.List<INamedTypeSymbol>();
            foreach (var reference in type.DeclaringSyntaxReferences)
            {
                var syntax = reference.GetSyntax();
                var model = compilation.GetSemanticModel(syntax.SyntaxTree);

                foreach (var invocation in syntax.DescendantNodes().OfType<InvocationExpressionSyntax>())
                {
                    if (invocation.Expression is not MemberAccessExpressionSyntax access) continue;
                    if (access.Name.Identifier.Text != "WithState") continue;

                    if (model.GetSymbolInfo(invocation).Symbol is IMethodSymbol method)
                    {
                        if (method.TypeArguments.Length > 0)
                        {
                            if (method.TypeArguments[0] is INamedTypeSymbol explicitType) results.Add(explicitType);
                        }
                        else if (invocation.ArgumentList.Arguments.Count > 0 &&
                                 model.GetTypeInfo(invocation.ArgumentList.Arguments[0].Expression).Type is INamedTypeSymbol argumentType)
                        {
                            results.Add(argumentType);
                        }
                    }
                }
            }
            return results;
        }
    }
}
