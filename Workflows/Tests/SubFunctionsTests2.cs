using Workflows.Handler;
using Workflows.Handler.Attributes;
using Workflows.Handler.BaseUse;
using Workflows.Handler.Testing;

namespace Tests;

public partial class SubWorkflowsTests
{
    [Fact]
    public async Task SameSubWorkflowTwice_Test()
    {
        using var test = new TestShell(nameof(SameSubWorkflowTwice_Test), typeof(SameSubWorkflowTwice));
        await test.ScanTypes();

        var logs = await test.GetLogs();
        Assert.Empty(logs);

        var instance = new SameSubWorkflowTwice();
        instance.Method1("f1");
        instance.Method1("f1");

        instance.Method1("f2");
        instance.Method1("f2");

        instance.Method2("f1");
        instance.Method2("f2");

        Assert.Empty(await test.RoundCheck(6, 9, 1));
    }

    public class SameSubWorkflowTwice : WorkflowContainer
    {
        [Workflow("WorkflowTwoLevels")]
        public async IAsyncEnumerable<Wait> Test()
        {
            int x = 100;
            yield return WaitGroup(new[] 
            {
                 WaitSubWorkflow(SubWorkflow1("f1")),
                 WaitSubWorkflow(SubWorkflow1("f2")) 
            }, "Wait sub workflow1 twice");
            await Task.Delay(100);
            if (x != 100)
                throw new Exception("Locals continuation problem.");
        }

        [SubWorkflow("SubWorkflow1")]
        public async IAsyncEnumerable<Wait> SubWorkflow1(string workflowInput)
        {
            int x = 10;
            yield return WaitMethod<string, string>(Method1, $"M1-{workflowInput}")
                .MatchIf((input, _) => input == workflowInput)
                .AfterMatch((_, _) =>
                {
                    if (x != 10)
                        throw new Exception("Closure in sub workflow problem.");
                    x += 10;
                });

            yield return WaitMethod<string, string>(Method1, "M1")
                .MatchIf((input, _) => input == workflowInput);

            x += 10;
            yield return WaitMethod<string, string>(Method2, "M2")
                .MatchIf((input, _) => input == workflowInput)
                .AfterMatch((_, _) =>
                {
                    if (x != 30)
                        throw new Exception("Closure restore in sub workflow problem.");
                    x += 10;
                });
            if (x != 40)
                throw new Exception("Closure restore in sub workflow problem.");
        }



        [EmitSignal("Method1")] public string Method1(string input) => input + "M1";
        [EmitSignal("Method2")] public string Method2(string input) => input + "M2";
        //[PushCall("Method3")] public string Method3(string input) => input + "M3";
        //[PushCall("Method4")] public string Method4(string input) => input + "M4";
    }
}