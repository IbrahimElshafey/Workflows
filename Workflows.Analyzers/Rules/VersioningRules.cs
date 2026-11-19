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
                    var liveProps = new System.Collections.Generic.HashSet<string>(typeSymbol.GetMembers()
                        .OfType<IPropertySymbol>()
                        .Where(p => !p.IsStatic && p.DeclaredAccessibility == Accessibility.Public)
                        .Select(p => p.Name));

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
    }
}
