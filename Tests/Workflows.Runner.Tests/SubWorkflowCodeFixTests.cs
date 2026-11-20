using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using Workflows.Analyzers;
using Xunit;

namespace Workflows.Runner.Tests
{
    /// <summary>
    /// WF400 code fix: a tail self-recursive sub-workflow becomes a flat loop over the state parameter.
    /// </summary>
    public class SubWorkflowCodeFixTests
    {
        private static readonly MetadataReference[] References = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
            .Select(a => MetadataReference.CreateFromFile(a.Location))
            .Cast<MetadataReference>()
            .ToArray();

        private const string RecursiveWorkflow = @"
using System.Collections.Generic;
using System.Threading.Tasks;
using Workflows.Definition;

namespace TestWorkflows
{
    public sealed class Ready { }

    public class ReviewState
    {
        public int Round { get; set; }
    }

    public sealed class RecursiveReview : WorkflowContainer
    {
        public async IAsyncEnumerable<Wait> Review(ReviewState state)
        {
            yield return WaitSignal<Ready>(""Ready"", ""Wait for the next round"");

            if (state.Round > 3)
            {
                yield break;
            }

            yield return WaitSubWorkflow(Review(new ReviewState()), ""Review"", ""Recurse"");
        }
    }
}
";

        [Fact]
        public async Task WF400_CodeFix_ShouldFlattenTailSelfRecursionIntoAWhileLoop()
        {
            var (offered, fixedCode) = await ApplyFixAsync(RecursiveWorkflow);

            offered.Should().Be(1);
            fixedCode.Should().Contain("while (true)");
            fixedCode.Should().Contain("state = new ReviewState();");
            fixedCode.Should().Contain("continue;");
            fixedCode.Should().NotContain("WaitSubWorkflow(Review");
            fixedCode.Should().Contain("yield break;", "the original early exit must survive the rewrite");
            fixedCode.Should().Contain("WaitSignal<Ready>", "the body must still yield its wait");
        }

        [Fact]
        public async Task WF400_CodeFix_ShouldProduceCodeThatStillAnalyzesClean()
        {
            var (offered, fixedCode) = await ApplyFixAsync(RecursiveWorkflow);

            offered.Should().Be(1);
            (await GetAnalyzerIdsAsync(fixedCode)).Should().NotContain(WorkflowAnalyzer.DiagnosticIdWF400);
            (await GetCompilerErrorsAsync(fixedCode)).Should().BeEmpty();
        }

        [Fact]
        public async Task WF400_CodeFix_ShouldNotBeOfferedWhenTheSelfCallIsNotInTailPosition()
        {
            var source = @"
using System.Collections.Generic;
using System.Threading.Tasks;
using Workflows.Definition;

namespace TestWorkflows
{
    public sealed class Ready { }

    public class ReviewState
    {
        public int Round { get; set; }
    }

    public sealed class RecursiveReview : WorkflowContainer
    {
        public async IAsyncEnumerable<Wait> Review(ReviewState state)
        {
            yield return WaitSubWorkflow(Review(new ReviewState()), ""Review"", ""Recurse"");

            yield return WaitSignal<Ready>(""Ready"", ""Wait for the next round"");
        }
    }
}
";

            var (offered, _) = await ApplyFixAsync(source);

            offered.Should().Be(0, "flattening a call that is not the last statement would change behaviour");
        }

        private static async Task<(int Offered, string FixedCode)> ApplyFixAsync(string source)
        {
            var (solution, documentId) = CreateSolution(source);
            var document = solution.GetDocument(documentId)!;

            var diagnostic = await GetDiagnosticAsync(document);
            if (diagnostic == null) return (0, source);

            var actions = new List<CodeAction>();
            var context = new CodeFixContext(
                document,
                diagnostic,
                (action, _) => actions.Add(action),
                CancellationToken.None);

            await new WorkflowCodeFixProvider().RegisterCodeFixesAsync(context);
            if (actions.Count == 0) return (0, source);

            var operations = await actions[0].GetOperationsAsync(CancellationToken.None);
            var changed = operations.OfType<ApplyChangesOperation>().Single().ChangedSolution;
            var fixedDocument = changed.GetDocument(documentId)!;

            return (actions.Count, (await fixedDocument.GetTextAsync()).ToString());
        }

        private static async Task<IReadOnlyList<string>> GetAnalyzerIdsAsync(string source)
        {
            var (solution, documentId) = CreateSolution(source);
            var document = solution.GetDocument(documentId)!;
            var compilation = await document.Project.GetCompilationAsync();

            var diagnostics = await compilation!
                .WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new WorkflowAnalyzer()))
                .GetAnalyzerDiagnosticsAsync();

            return diagnostics.Select(d => d.Id).Distinct().ToList();
        }

        private static async Task<IReadOnlyList<string>> GetCompilerErrorsAsync(string source)
        {
            var (solution, documentId) = CreateSolution(source);
            var document = solution.GetDocument(documentId)!;
            var compilation = await document.Project.GetCompilationAsync();

            return compilation!.GetDiagnostics()
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .Select(d => d.ToString())
                .ToList();
        }

        private static async Task<Diagnostic?> GetDiagnosticAsync(Document document)
        {
            var compilation = await document.Project.GetCompilationAsync();
            var diagnostics = await compilation!
                .WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new WorkflowAnalyzer()))
                .GetAnalyzerDiagnosticsAsync();

            return diagnostics.FirstOrDefault(d => d.Id == WorkflowAnalyzer.DiagnosticIdWF400);
        }

        private static (Solution Solution, DocumentId DocumentId) CreateSolution(string source)
        {
            var workspace = new AdhocWorkspace();
            var projectId = ProjectId.CreateNewId();
            var documentId = DocumentId.CreateNewId(projectId);

            var solution = workspace.CurrentSolution
                .AddProject(projectId, "TestAssembly", "TestAssembly", LanguageNames.CSharp)
                .AddMetadataReferences(projectId, References)
                .AddDocument(documentId, "TestFile.cs", SourceText.From(source))
                .WithProjectCompilationOptions(
                    projectId,
                    new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            return (solution, documentId);
        }
    }
}