using Workflows.Handler;
using Workflows.Handler.Attributes;
using Workflows.Handler.BaseUse;
using Workflows.Handler.InOuts;
using Workflows.Handler.Testing;

namespace Tests;

public class ComplexMatchExpression
{
    [Fact]
    public async Task ComplexMatchExpression_Test()
    {
        using var test = new TestShell(nameof(ComplexMatchExpression_Test), typeof(TestClass));
        await test.ScanTypes();

        var logs = await test.GetLogs();
        Assert.Empty(logs);

        var instance = new TestClass();
        instance.Method6("Test");

        logs = await test.GetLogs();
        Assert.Empty(logs);
        var signals = await test.GetSignals();
        Assert.Equal(1, signals.Count);
        var instances = await test.GetInstances<TestClass>();
        Assert.Equal(1, instances.Count);
        Assert.Equal(1, instances.Count(x => x.Status == WorkflowInstanceStatus.Completed));
    }

    public class TestClass : WorkflowContainer
    {
        public int IntProp { get; set; } = 100;

        [Workflow("MatchWithInstanceMethodCall")]
        public async IAsyncEnumerable<Wait> Test()
        {
            yield return
                WaitMethod<string, string>(Method6, "M6")
                .MatchIf((input, output) =>
                    input == "Test" &&
                    InstanceCall(input, output) &&
                    (IntMethod() == 110 || Math.Max(input.Length, output.Length) > 1)
                );
        }

        private bool InstanceCall(string input, string output)
        {
            return output == "TestM6" && input.Length == 4;
        }

        private int IntMethod()
        {
            return IntProp + 10;
        }

        [EmitSignal("Method6")] public string Method6(string input) => input + "M6";

    }
}
