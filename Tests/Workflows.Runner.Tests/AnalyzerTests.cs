using FluentAssertions;
using Microsoft.CodeAnalysis;
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
        private async Task<List<Diagnostic>> RunAnalyzerAsync(string source)
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
            return diagnostics.ToList();
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
        public override async IAsyncEnumerable<Wait> Run()
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
        public override async IAsyncEnumerable<Wait> Run()
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
        public override async IAsyncEnumerable<Wait> Run()
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
        public override async IAsyncEnumerable<Wait> Run()
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
        public override async IAsyncEnumerable<Wait> Run()
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
        public override async IAsyncEnumerable<Wait> Run()
        {
            int number = 42;
            string text = ""hello"";
            List<string> list = new List<string>();
            yield return WaitSignal<string>(""MySignal"");
        }
    }
}";

            var diagnostics = await RunAnalyzerAsync(source);
            
            diagnostics.Where(d => d.Id == "WF004").Should().BeEmpty();
        }
    }
}
