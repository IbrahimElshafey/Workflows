using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading.Tasks;
using Workflows.Analyzers;
using Xunit;

namespace Workflows.Runner.Tests
{
    public class AnalyzerTests
    {
        private async Task<List<Diagnostic>> RunAnalyzerAsync(string source, bool includeWF211 = false)
        {
            var syntaxTree = CSharpSyntaxTree.ParseText(source);

            // Collect referenced assemblies from the current AppDomain
            var references = AppDomain.CurrentDomain.GetAssemblies()
                .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
                .Select(a => MetadataReference.CreateFromFile(a.Location))
                .Cast<MetadataReference>()
                .ToList();

            var compilation = CSharpCompilation.Create(
                "TestAssembly",
                new[] { syntaxTree },
                references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            var compilationWithAnalyzers = compilation.WithAnalyzers(
                ImmutableArray.Create<DiagnosticAnalyzer>(new WorkflowAnalyzer()));

            var diagnostics = await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync();
            var list = diagnostics.ToList();
            if (!includeWF211)
            {
                list.RemoveAll(d => d.Id == "WF211");
            }
            return list;
        }

        [Fact]
        public async Task WF003_AnonymousTypeInWithState_ShouldTriggerDiagnostic()
        {
            var source = @"
using System;
using System.Collections.Generic;
using Workflows.Definition;

namespace TestWorkflows
{
    public sealed class TestWorkflow : WorkflowContainer
    {
        public async IAsyncEnumerable<Wait> Run()
        {
            yield return WaitSignal<string>(""MySignal"")
                .WithState(new { OrderId = 123, Status = ""Pending"" });
        }
    }
}";

            var diagnostics = await RunAnalyzerAsync(source);
            
            diagnostics.Should().ContainSingle(d => d.Id == "WF003");
            diagnostics.First(d => d.Id == "WF003").GetMessage().Should().Contain("Passing anonymous type to '.WithState()' is disallowed");
        }

        [Fact]
        public async Task WF003_ValueTupleInWithState_ShouldNotTriggerDiagnostic()
        {
            var source = @"
using System;
using System.Collections.Generic;
using Workflows.Definition;

namespace TestWorkflows
{
    public sealed class TestWorkflow : WorkflowContainer
    {
        public async IAsyncEnumerable<Wait> Run()
        {
            yield return WaitSignal<string>(""MySignal"")
                .WithState((OrderId: 123, Status: ""Pending""));
        }
    }
}";

            var diagnostics = await RunAnalyzerAsync(source);
            
            diagnostics.Where(d => d.Id == "WF003").Should().BeEmpty();
        }

        [Fact]
        public async Task WF004_IDisposableLocalVariable_ShouldTriggerDiagnostic()
        {
            var source = @"
using System;
using System.Collections.Generic;
using Workflows.Definition;

namespace TestWorkflows
{
    public class MyDisposable : IDisposable
    {
        public void Dispose() { }
    }

    public sealed class TestWorkflow : WorkflowContainer
    {
        public async IAsyncEnumerable<Wait> Run()
        {
            MyDisposable disposable = new MyDisposable();
            yield return WaitSignal<string>(""MySignal"");
        }
    }
}";

            var diagnostics = await RunAnalyzerAsync(source);
            
            diagnostics.Should().ContainSingle(d => d.Id == "WF004");
            diagnostics.First(d => d.Id == "WF004").GetMessage().Should().Contain("is unserializable (IDisposable, Stream, or SqlConnection cannot be declared as local variables)");
        }

        [Fact]
        public async Task WF004_StreamLocalVariable_ShouldTriggerDiagnostic()
        {
            var source = @"
using System;
using System.Collections.Generic;
using System.IO;
using Workflows.Definition;

namespace TestWorkflows
{
    public sealed class TestWorkflow : WorkflowContainer
    {
        public async IAsyncEnumerable<Wait> Run()
        {
            Stream stream = new MemoryStream();
            yield return WaitSignal<string>(""MySignal"");
        }
    }
}";

            var diagnostics = await RunAnalyzerAsync(source);
            
            diagnostics.Should().ContainSingle(d => d.Id == "WF004");
        }

        [Fact]
        public async Task WF004_SqlConnectionLocalVariable_ShouldTriggerDiagnostic()
        {
            var source = @"
using System;
using System.Collections.Generic;
using Workflows.Definition;

namespace System.Data.SqlClient
{
    public class SqlConnection : IDisposable
    {
        public void Dispose() { }
    }
}

namespace TestWorkflows
{
    using System.Data.SqlClient;

    public sealed class TestWorkflow : WorkflowContainer
    {
        public async IAsyncEnumerable<Wait> Run()
        {
            SqlConnection conn = new SqlConnection();
            yield return WaitSignal<string>(""MySignal"");
        }
    }
}";

            var diagnostics = await RunAnalyzerAsync(source);
            
            diagnostics.Should().ContainSingle(d => d.Id == "WF004");
        }

        [Fact]
        public async Task WF004_SafeLocalVariables_ShouldNotTriggerDiagnostic()
        {
            var source = @"
using System;
using System.Collections.Generic;
using Workflows.Definition;

namespace TestWorkflows
{
    public sealed class TestWorkflow : WorkflowContainer
    {
        public async IAsyncEnumerable<Wait> Run()
        {
            int number = 42;
            string text = ""hello"";
            List<string> list = new List<string>();
            yield return WaitSignal<string>(""MySignal"", ""WaitName"");
        }
    }
}";

            var diagnostics = await RunAnalyzerAsync(source);
            
            diagnostics.Where(d => d.Id == "WF004").Should().BeEmpty();
        }

        [Fact]
        public async Task WF204_MissingWaitName_ShouldTriggerDiagnostic()
        {
            var source = @"
using System;
using System.Collections.Generic;
using Workflows.Definition;

namespace TestWorkflows
{
    public sealed class TestWorkflow : WorkflowContainer
    {
        public async IAsyncEnumerable<Wait> Run()
        {
            yield return WaitSignal<string>(""MySignal""); // name is omitted
        }
    }
}";

            var diagnostics = await RunAnalyzerAsync(source);
            diagnostics.Should().ContainSingle(d => d.Id == "WF204");
            diagnostics.First(d => d.Id == "WF204").GetMessage().Should().Contain("must specify a non-empty name");
        }

        [Fact]
        public async Task WF205_DuplicateWaitName_ShouldTriggerDiagnostic()
        {
            var source = @"
using System;
using System.Collections.Generic;
using Workflows.Definition;

namespace TestWorkflows
{
    public sealed class TestWorkflow : WorkflowContainer
    {
        public async IAsyncEnumerable<Wait> Run()
        {
            yield return WaitSignal<string>(""MySignal1"", ""WaitA"");
            yield return WaitSignal<string>(""MySignal2"", ""WaitA"");
        }
    }
}";

            var diagnostics = await RunAnalyzerAsync(source);
            diagnostics.Should().ContainSingle(d => d.Id == "WF205");
            diagnostics.First(d => d.Id == "WF205").GetMessage().Should().Contain("is already defined in workflow");
        }

        [Fact]
        public async Task WF206_NonPrivateSubWorkflow_ShouldTriggerDiagnostic()
        {
            var source = @"
using System;
using System.Collections.Generic;
using Workflows.Definition;

namespace TestWorkflows
{
    public sealed class TestWorkflow : WorkflowContainer
    {
        public async IAsyncEnumerable<Wait> Run()
        {
            yield return WaitSubWorkflow(Child(), ""Child"");
        }

        [SubWorkflow]
        public async IAsyncEnumerable<Wait> Child()
        {
            yield return WaitSignal<string>(""MySignal"", ""WaitName"");
        }
    }
}";

            var diagnostics = await RunAnalyzerAsync(source);
            diagnostics.Should().ContainSingle(d => d.Id == "WF206");
            diagnostics.First(d => d.Id == "WF206").GetMessage().Should().Contain("must be private");
        }

        [Fact]
        public async Task WF207_SubWorkflowInNonWorkflowContainer_ShouldTriggerDiagnostic()
        {
            var source = @"
using System;
using System.Collections.Generic;
using Workflows.Definition;

namespace TestWorkflows
{
    public class NonWorkflow
    {
        [SubWorkflow]
        private async IAsyncEnumerable<Wait> Child()
        {
            yield return null;
        }
    }
}";

            var diagnostics = await RunAnalyzerAsync(source);
            diagnostics.Should().ContainSingle(d => d.Id == "WF207");
            diagnostics.First(d => d.Id == "WF207").GetMessage().Should().Contain("does not inherit from WorkflowContainer");
        }

        [Fact]
        public async Task WF208_SubWorkflowMissingAttribute_ShouldTriggerDiagnostic()
        {
            var source = @"
using System;
using System.Collections.Generic;
using Workflows.Definition;

namespace TestWorkflows
{
    public sealed class TestWorkflow : WorkflowContainer
    {
        public async IAsyncEnumerable<Wait> Run()
        {
            yield return WaitSubWorkflow(Child(), ""Child"");
        }

        private async IAsyncEnumerable<Wait> Child() // returns wait async enum, but missing [SubWorkflow]
        {
            yield return WaitSignal<string>(""MySignal"", ""WaitName"");
        }
    }
}";

            var diagnostics = await RunAnalyzerAsync(source);
            diagnostics.Where(d => d.Id == "WF208").Should().HaveCount(2);
            diagnostics.First(d => d.Id == "WF208").GetMessage().Should().Contain("must be decorated with [SubWorkflow]");
        }

        [Fact]
        public async Task WF209_MissingWorkflowAttribute_ShouldTriggerDiagnostic()
        {
            var source = @"
using System;
using System.Collections.Generic;
using Workflows.Definition;

namespace TestWorkflows
{
    public sealed class MissingAttributeWorkflow : WorkflowContainer
    {
        public async IAsyncEnumerable<Wait> Run()
        {
            yield return WaitSignal<string>(""MySignal"", ""WaitName"");
        }
    }
}";

            var diagnostics = await RunAnalyzerAsync(source);
            diagnostics.Should().ContainSingle(d => d.Id == "WF209");
            diagnostics.First(d => d.Id == "WF209").GetMessage().Should().Contain("is missing [WorkflowAttribute]");
        }

        [Fact]
        public async Task WF209_WorkflowAttributePresent_ShouldNotTriggerDiagnostic()
        {
            var source = @"
using System;
using System.Collections.Generic;
using Workflows.Definition;

namespace TestWorkflows
{
    [Workflow(""MyWorkflow"", 1)]
    public sealed class HasAttributeWorkflow : WorkflowContainer
    {
        public async IAsyncEnumerable<Wait> Run()
        {
            yield return WaitSignal<string>(""MySignal"", ""WaitName"");
        }
    }
}";

            var diagnostics = await RunAnalyzerAsync(source);
            diagnostics.Where(d => d.Id == "WF209").Should().BeEmpty();
        }

        [Fact]
        public async Task WF_ERR_UNSAFE_STATE_ShouldTriggerDiagnostic_WhenLocalVariableAccessedAcrossWaitBoundary()
        {
            var source = @"
using System;
using System.Collections.Generic;
using Workflows.Definition;

namespace TestWorkflows
{
    [Workflow(""MyWorkflow"", 1)]
    public sealed class TestWorkflow : WorkflowContainer
    {
        public async IAsyncEnumerable<Wait> Run()
        {
            int localVal = 42;
            yield return WaitSignal<string>(""MySignal"", ""WaitName"");
            Console.WriteLine(localVal); // accessed across yield return
        }
    }
}";

            var diagnostics = await RunAnalyzerAsync(source);
            diagnostics.Should().ContainSingle(d => d.Id == "WF_ERR_UNSAFE_STATE");
        }

        [Fact]
        public async Task WF_ERR_UNSAFE_STATE_ShouldNotTriggerDiagnostic_WhenLocalVariableNotAccessedAcrossWaitBoundary()
        {
            var source = @"
using System;
using System.Collections.Generic;
using Workflows.Definition;

namespace TestWorkflows
{
    [Workflow(""MyWorkflow"", 1)]
    public sealed class TestWorkflow : WorkflowContainer
    {
        public async IAsyncEnumerable<Wait> Run()
        {
            int localVal = 42;
            Console.WriteLine(localVal); // used before yield return
            yield return WaitSignal<string>(""MySignal"", ""WaitName"");
        }
    }
}";

            var diagnostics = await RunAnalyzerAsync(source);
            diagnostics.Where(d => d.Id == "WF_ERR_UNSAFE_STATE").Should().BeEmpty();
        }

        [Fact]
        public async Task WF210_MissingRunMethod_ShouldTriggerDiagnostic()
        {
            var source = @"
using System;
using System.Collections.Generic;
using Workflows.Definition;

namespace TestWorkflows
{
    [Workflow(""MyWorkflow"", 1)]
    public sealed class TestWorkflowWithoutRun : WorkflowContainer
    {
    }
}";

            var diagnostics = await RunAnalyzerAsync(source);
            diagnostics.Should().ContainSingle(d => d.Id == "WF210");
        }

        [Fact]
        public async Task WF210_RunMethodPresent_ShouldNotTriggerDiagnostic()
        {
            var source = @"
using System;
using System.Collections.Generic;
using Workflows.Definition;

namespace TestWorkflows
{
    [Workflow(""MyWorkflow"", 1)]
    public sealed class TestWorkflowWithRun : WorkflowContainer
    {
        public async IAsyncEnumerable<Wait> Run()
        {
            yield break;
        }
    }
}";

            var diagnostics = await RunAnalyzerAsync(source);
            diagnostics.Where(d => d.Id == "WF210").Should().BeEmpty();
        }

        [Fact]
        public async Task WF210_StatefulWorkflow_RunMethodWithStateParameter_ShouldNotTriggerDiagnostic()
        {
            var source = @"
using System;
using System.Collections.Generic;
using Workflows.Definition;

namespace TestWorkflows
{
    public class MyState
    {
    }

    [Workflow(""MyWorkflow"", 1)]
    public sealed class TestWorkflowWithStateRun : WorkflowContainer
    {
        public async IAsyncEnumerable<Wait> Run(MyState state)
        {
            yield break;
        }
    }
}";

            var diagnostics = await RunAnalyzerAsync(source);
            diagnostics.Where(d => d.Id == "WF210").Should().BeEmpty();
        }

        private async Task<string> ApplyCodeFixAsync(string source, string diagnosticId)
        {
            var syntaxTree = CSharpSyntaxTree.ParseText(source);
            var references = AppDomain.CurrentDomain.GetAssemblies()
                .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
                .Select(a => MetadataReference.CreateFromFile(a.Location))
                .Cast<MetadataReference>()
                .ToList();

            var workspace = new AdhocWorkspace();
            var projectId = ProjectId.CreateNewId();
            var documentId = DocumentId.CreateNewId(projectId);

            var solution = workspace.CurrentSolution
                .AddProject(projectId, "TestProject", "TestAssembly", LanguageNames.CSharp)
                .AddMetadataReferences(projectId, references)
                .AddDocument(documentId, "TestFile.cs", source);

            var document = solution.GetDocument(documentId)!;
            var compilation = await document.Project.GetCompilationAsync();
            var compilationWithAnalyzers = compilation!.WithAnalyzers(
                ImmutableArray.Create<DiagnosticAnalyzer>(new WorkflowAnalyzer()));

            var diagnostics = await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync();
            var targetDiag = diagnostics.FirstOrDefault(d => d.Id == diagnosticId);
            if (targetDiag == null) return source;

            var codeFixProvider = new WorkflowCodeFixProvider();
            var actions = new List<CodeAction>();
            var context = new CodeFixContext(document, targetDiag, (action, diag) => actions.Add(action), default);

            await codeFixProvider.RegisterCodeFixesAsync(context);
            if (actions.Count == 0) return source;

            var fixAction = actions.First();
            var operations = await fixAction.GetOperationsAsync(default);
            var applyChangesOperation = operations.OfType<ApplyChangesOperation>().FirstOrDefault();
            if (applyChangesOperation == null) return source;

            var newDoc = applyChangesOperation.ChangedSolution.GetDocument(documentId)!;
            var newRoot = await newDoc.GetSyntaxRootAsync();
            return newRoot!.ToFullString();
        }

        [Fact]
        public async Task UnsafeStateFix_WithoutExistingStateClass_ShouldCreateStateClassAndChangeInheritance()
        {
            var source = @"
using System;
using System.Collections.Generic;
using Workflows.Definition;

namespace TestWorkflows
{
    [Workflow(""MyWorkflow"", 1)]
    public sealed class TestWorkflow : WorkflowContainer
    {
        public async IAsyncEnumerable<Wait> Run()
        {
            int localVal = 42;
            yield return WaitSignal<string>(""MySignal"", ""WaitName"");
            Console.WriteLine(localVal);
        }
    }
}";

            var fixedSource = await ApplyCodeFixAsync(source, "WF_ERR_UNSAFE_STATE");

            fixedSource.Should().Contain("public sealed class RunState");
            fixedSource.Should().Contain("public sealed class TestWorkflow : WorkflowContainer");
            fixedSource.Should().Contain("public async IAsyncEnumerable<Wait> Run(RunState state)");
            fixedSource.Should().Contain("state.LocalVal = 42;");
            fixedSource.Should().Contain("Console.WriteLine(state.LocalVal);");
            fixedSource.Should().NotContain("int localVal = 42;");
        }

        [Fact]
        public async Task UnsafeStateFix_WithExistingStateClass_ShouldMoveVariableToStateClass()
        {
            var source = @"
using System;
using System.Collections.Generic;
using Workflows.Definition;

namespace TestWorkflows
{
    public class TestWorkflowState
    {
    }

    [Workflow(""MyWorkflow"", 1)]
    public sealed class TestWorkflow : WorkflowContainer
    {
        public async IAsyncEnumerable<Wait> Run(TestWorkflowState state)
        {
            int localVal = 42;
            yield return WaitSignal<string>(""MySignal"", ""WaitName"");
            Console.WriteLine(localVal);
        }
    }
}";

            var fixedSource = await ApplyCodeFixAsync(source, "WF_ERR_UNSAFE_STATE");

            fixedSource.Should().Contain("public class TestWorkflowState");
            fixedSource.Should().Contain("public int LocalVal { get; set; }");
            fixedSource.Should().Contain("state.LocalVal = 42;");
            fixedSource.Should().Contain("Console.WriteLine(state.LocalVal);");
            fixedSource.Should().NotContain("int localVal = 42;");
        }

        [Fact]
        public async Task WF300_VersionBumped_ShouldTriggerWarning()
        {
            var source = @"
using System;
using System.Collections.Generic;
using Workflows.Definition;

namespace TestWorkflows
{
    [Workflow(""MyWorkflow"", 2)]
    public sealed class TestWorkflow : WorkflowContainer
    {
        public string MyProp { get; set; } = """";
        public async IAsyncEnumerable<Wait> Run() { yield break; }
    }
}";
            var schemaContent = @"{
  ""SchemaVersion"": 1,
  ""WorkflowName"": ""MyWorkflow"",
  ""AssemblyRootNamespace"": ""TestAssembly"",
  ""StateProperties"": [
    {
      ""Name"": ""MyProp"",
      ""TypeFqn"": ""string"",
      ""Nullable"": false
    }
  ]
}";

            var diagnostics = await RunAnalyzerWithAdditionalFilesAsync(source, "Schemas/MyWorkflow_Schema.json", schemaContent);
            diagnostics.Should().ContainSingle(d => d.Id == "WF300");
        }

        [Fact]
        public async Task WF300_CodeFix_ShouldGenerateArchivedFiles()
        {
            var source = @"
using System;
using System.Collections.Generic;
using Workflows.Definition;

namespace TestWorkflows
{
    [Workflow(""MyWorkflow"", 2)]
    public sealed class TestWorkflow : WorkflowContainer
    {
        public string MyProp { get; set; } = """";
        public async IAsyncEnumerable<Wait> Run() { yield break; }
    }
}";
            var schemaContent = @"{
  ""SchemaVersion"": 1,
  ""WorkflowName"": ""MyWorkflow"",
  ""AssemblyRootNamespace"": ""TestAssembly"",
  ""StateProperties"": [
    {
      ""Name"": ""MyProp"",
      ""TypeFqn"": ""string"",
      ""Nullable"": false
    }
  ]
}";

            var solution = await ApplyCodeFixWithAdditionalFilesAsync(source, "WF300", "Schemas/MyWorkflow_Schema.json", schemaContent);
            
            // Check that files are added
            var documents = solution.Projects.SelectMany(p => p.Documents).ToList();
            var additionalDocs = solution.Projects.SelectMany(p => p.AdditionalDocuments).ToList();

            documents.Any(d => d.Name == "MyWorkflow_V1.cs").Should().BeTrue();
            documents.Any(d => d.Name == "MyWorkflowV1_Layout.cs").Should().BeTrue();
            additionalDocs.Any(d => d.Name == "MyWorkflow_V1_Schema.json").Should().BeTrue();
            additionalDocs.Any(d => d.Name == "MyWorkflow_V1.csproj").Should().BeTrue();
            additionalDocs.Any(d => d.Name == "MyWorkflow_Schema.json").Should().BeTrue();

            // Verify namespace is rewritten in archived source
            var archivedDoc = documents.First(d => d.Name == "MyWorkflow_V1.cs");
            var syntaxRoot = await archivedDoc.GetSyntaxRootAsync();
            syntaxRoot!.ToFullString().Should().Contain("namespace TestWorkflows.Archive.MyWorkflow.V1");
        }

        [Fact]
        public async Task WF301_ArchivedSchemaDrift_ShouldTriggerError()
        {
            var source = @"
using System;
using System.Collections.Generic;
using Workflows.Definition;

namespace TestWorkflows.Archive.MyWorkflow.V1
{
    public sealed class MyWorkflowV1 : WorkflowStateWrapper
    {
        public MyWorkflowV1(Workflows.Abstraction.DTOs.WorkflowStateDto dto) : base(dto) {}
    }
}";
            var schemaContent = @"{
  ""SchemaVersion"": 1,
  ""WorkflowName"": ""MyWorkflow"",
  ""AssemblyRootNamespace"": ""TestAssembly"",
  ""StateProperties"": [
    {
      ""Name"": ""MyProp"",
      ""TypeFqn"": ""string"",
      ""Nullable"": false
    }
  ]
}";

            // The file path must contain /Archive/ for WF301 to check it.
            var syntaxTree = CSharpSyntaxTree.ParseText(source, path: "d:/MySrc/Workflows/Archive/MyWorkflow/V1/MyWorkflow_V1.cs");
            var references = AppDomain.CurrentDomain.GetAssemblies()
                .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
                .Select(a => MetadataReference.CreateFromFile(a.Location))
                .Cast<MetadataReference>()
                .ToList();

            var compilation = CSharpCompilation.Create(
                "TestAssembly",
                new[] { syntaxTree },
                references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            var additionalText = new InMemoryAdditionalText("d:/MySrc/Workflows/Archive/MyWorkflow/V1/MyWorkflow_V1_Schema.json", schemaContent);
            var compilationWithAnalyzers = compilation.WithAnalyzers(
                ImmutableArray.Create<DiagnosticAnalyzer>(new WorkflowAnalyzer()),
                new AnalyzerOptions(ImmutableArray.Create<AdditionalText>(additionalText)));

            var diagnostics = await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync();
            diagnostics.Should().ContainSingle(d => d.Id == "WF301");
        }

        private async Task<List<Diagnostic>> RunAnalyzerWithAdditionalFilesAsync(string source, string schemaPath, string schemaContent)
        {
            var syntaxTree = CSharpSyntaxTree.ParseText(source);
            var references = AppDomain.CurrentDomain.GetAssemblies()
                .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
                .Select(a => MetadataReference.CreateFromFile(a.Location))
                .Cast<MetadataReference>()
                .ToList();

            var compilation = CSharpCompilation.Create(
                "TestAssembly",
                new[] { syntaxTree },
                references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            var additionalText = new InMemoryAdditionalText(schemaPath, schemaContent);
            var compilationWithAnalyzers = compilation.WithAnalyzers(
                ImmutableArray.Create<DiagnosticAnalyzer>(new WorkflowAnalyzer()),
                new AnalyzerOptions(ImmutableArray.Create<AdditionalText>(additionalText)));

            var diagnostics = await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync();
            return diagnostics.ToList();
        }

        private async Task<Solution> ApplyCodeFixWithAdditionalFilesAsync(string source, string diagnosticId, string schemaPath, string schemaContent)
        {
            var syntaxTree = CSharpSyntaxTree.ParseText(source);
            var references = AppDomain.CurrentDomain.GetAssemblies()
                .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
                .Select(a => MetadataReference.CreateFromFile(a.Location))
                .Cast<MetadataReference>()
                .ToList();

            var workspace = new AdhocWorkspace();
            var projectId = ProjectId.CreateNewId();
            var documentId = DocumentId.CreateNewId(projectId);

            var solution = workspace.CurrentSolution
                .AddProject(projectId, "TestProject", "TestAssembly", LanguageNames.CSharp)
                .AddMetadataReferences(projectId, references)
                .AddDocument(documentId, "TestFile.cs", source);

            var additionalText = new InMemoryAdditionalText(schemaPath, schemaContent);
            solution = solution.AddAdditionalDocument(DocumentId.CreateNewId(projectId), System.IO.Path.GetFileName(schemaPath), schemaContent, new[] { "Schemas" }, schemaPath);

            var document = solution.GetDocument(documentId)!;
            var compilation = await document.Project.GetCompilationAsync();
            
            var compilationWithAnalyzers = compilation!.WithAnalyzers(
                ImmutableArray.Create<DiagnosticAnalyzer>(new WorkflowAnalyzer()),
                new AnalyzerOptions(ImmutableArray.Create<AdditionalText>(additionalText)));

            var diagnostics = await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync();
            var targetDiag = diagnostics.FirstOrDefault(d => d.Id == diagnosticId);
            if (targetDiag == null) return solution;

            var codeFixProvider = new WorkflowCodeFixProvider();
            var actions = new List<CodeAction>();
            var context = new CodeFixContext(document, targetDiag, (action, diag) => actions.Add(action), default);

            await codeFixProvider.RegisterCodeFixesAsync(context);
            if (actions.Count == 0) return solution;

            var fixAction = actions.First();
            var operations = await fixAction.GetOperationsAsync(default);
            var applyChangesOperation = operations.OfType<ApplyChangesOperation>().FirstOrDefault();
            if (applyChangesOperation == null) return solution;

            return applyChangesOperation.ChangedSolution;
        }

        [Fact]
        public async Task WF209_MissingWorkflowAttribute_OnClassWithRunMethod_ShouldTriggerDiagnostic()
        {
            var source = @"
using System;
using System.Collections.Generic;
using Workflows.Definition;

namespace TestWorkflows
{
    public sealed class ClassWithRunMethod
    {
        public async IAsyncEnumerable<Wait> Run()
        {
            yield break;
        }
    }
}";

            var diagnostics = await RunAnalyzerAsync(source);
            diagnostics.Should().ContainSingle(d => d.Id == "WF209");
            diagnostics.First(d => d.Id == "WF209").GetMessage().Should().Contain("is missing [WorkflowAttribute]");
        }

        [Fact]
        public async Task WF211_MethodWithNoParameter_ShouldTriggerDiagnostic()
        {
            var source = @"
using System;
using System.Collections.Generic;
using Workflows.Definition;

namespace TestWorkflows
{
    [Workflow(""TestWorkflow"", 1)]
    public sealed class TestWorkflow : WorkflowContainer
    {
        public async IAsyncEnumerable<Wait> Run()
        {
            yield break;
        }
    }
}";

            var diagnostics = await RunAnalyzerAsync(source, includeWF211: true);
            diagnostics.Should().ContainSingle(d => d.Id == "WF211");
            diagnostics.First(d => d.Id == "WF211").GetMessage().Should().Contain("must accept a state DTO parameter");
        }

        [Fact]
        public async Task WF211_MethodWithMultipleParameters_ShouldTriggerDiagnostic()
        {
            var source = @"
using System;
using System.Collections.Generic;
using Workflows.Definition;

namespace TestWorkflows
{
    [Workflow(""TestWorkflow"", 1)]
    public sealed class TestWorkflow : WorkflowContainer
    {
        public class MyState {}

        public async IAsyncEnumerable<Wait> Run(MyState state, int otherParam)
        {
            yield break;
        }
    }
}";

            var diagnostics = await RunAnalyzerAsync(source, includeWF211: true);
            diagnostics.Should().ContainSingle(d => d.Id == "WF211");
            diagnostics.First(d => d.Id == "WF211").GetMessage().Should().Contain("must accept a state DTO parameter");
        }

        [Fact]
        public async Task WF211_MethodWithExactlyOneParameter_ShouldNotTriggerDiagnostic()
        {
            var source = @"
using System;
using System.Collections.Generic;
using Workflows.Definition;

namespace TestWorkflows
{
    [Workflow(""TestWorkflow"", 1)]
    public sealed class TestWorkflow : WorkflowContainer
    {
        public class MyState {}

        public async IAsyncEnumerable<Wait> Run(MyState state)
        {
            yield break;
        }
    }
}";

            var diagnostics = await RunAnalyzerAsync(source, includeWF211: true);
            diagnostics.Where(d => d.Id == "WF211").Should().BeEmpty();
        }
    }

    public class InMemoryAdditionalText : AdditionalText
    {
        private readonly string _content;

        public InMemoryAdditionalText(string path, string content)
        {
            Path = path;
            _content = content;
        }

        public override string Path { get; }

        public override Microsoft.CodeAnalysis.Text.SourceText GetText(System.Threading.CancellationToken cancellationToken = default)
        {
            return Microsoft.CodeAnalysis.Text.SourceText.From(_content);
        }
    }
}

