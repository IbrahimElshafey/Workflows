using Workflows.Handler;
using Workflows.Handler.Attributes;
using Workflows.Handler.BaseUse;
using Workflows.Handler.InOuts;
using Workflows.Handler.Testing;

namespace Tests;

public class MatchWithInstanceMethodCall
{
    [Fact]
    public async Task MatchWithInstanceMethodCall_Test()
    {
        using var test = new TestShell(nameof(MatchWithInstanceMethodCall_Test), typeof(TestClass));
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
        [Workflow("MatchWithInstanceMethodCall")]
        public async IAsyncEnumerable<Wait> Test()
        {
            yield return
                WaitMethod<string, string>(Method6, "M6")
                .MatchIf((input, output) => input == "Test" && InstanceCall(input, output));
        }

        private bool InstanceCall(string input, string output)
        {
            return output == "TestM6" && input.Length == 4;
        }

        [EmitSignal("Method6")] public string Method6(string input) => input + "M6";

    }
}
