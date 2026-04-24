using Workflows.Handler;
using Workflows.Handler.Attributes;
using Workflows.Handler.BaseUse;
using Workflows.Handler.InOuts;
using Workflows.Handler.Testing;

namespace Tests;

public partial class SubWorkflowsTests
{
    [Fact]
    public async Task WorkflowLevels_Test()
    {
        using var test = new TestShell(nameof(WorkflowLevels_Test), typeof(WorkflowLevels));
        await test.ScanTypes();

        var logs = await test.GetLogs();
        Assert.Empty(logs);

        var instance = new WorkflowLevels();
        instance.Method1("m1_1");
        instance.Method4("m4_1");
        instance.Method2("m2_1");
        instance.Method3("m3_1");

        logs = await test.GetLogs();
        Assert.Empty(logs);
        var signals = await test.GetSignals();
        Assert.Equal(4, signals.Count);
        var instances = await test.GetInstances<WorkflowLevels>(true);
        Assert.Equal(2, instances.Count);
        Assert.Equal(1, instances.Count(x => x.Status == WorkflowInstanceStatus.Completed));
        var waits = await test.GetWaits();
        Assert.Equal(7, waits.Count);
        Assert.Equal(7, waits.Count(x => x.Status == WaitStatus.Completed));


        instance.Method1("m1_2");
        instance.Method4("m4_2");
        instance.Method2("m2_2");
        instance.Method3("m3_2");

        logs = await test.GetLogs();
        Assert.Empty(logs);
        signals = await test.GetSignals();
        Assert.Equal(8, signals.Count);
        instances = await test.GetInstances<WorkflowLevels>(true);
        Assert.Equal(3, instances.Count);
        Assert.Equal(2, instances.Count(x => x.Status == WorkflowInstanceStatus.Completed));
        waits = await test.GetWaits();
        Assert.Equal(14, waits.Count);
        Assert.Equal(14, waits.Count(x => x.Status == WaitStatus.Completed));
    }

    public class WorkflowLevels : WorkflowContainer
    {
        [Workflow("WorkflowTwoLevels")]
        public async IAsyncEnumerable<Wait> Test()
        {
            int x = 100;
            yield return WaitSubWorkflow(SubWorkflow1(), "Wait sub workflow1");
            await Task.Delay(100);
            if (x != 100)
                throw new Exception("Locals continuation problem.");
        }

        [SubWorkflow("SubWorkflow1")]
        public async IAsyncEnumerable<Wait> SubWorkflow1()
        {
            int x = 10;
            yield return WaitMethod<string, string>(Method1, "M1")
                .MatchAny()
                .AfterMatch((_, _) =>
                {
                    if (x != 10)
                        throw new Exception("Closure in sub workflow problem.");
                    x += 10;
                });

            x += 10;
            yield return WaitMethod<string, string>(Method4, "M4")
                .MatchAny()
                .AfterMatch((_, _) =>
                {
                    if (x != 30)
                        throw new Exception("Closure restore in sub workflow problem.");
                });
            yield return WaitSubWorkflow(SubWorkflow2(), "Wait sub workflow2");
        }

        [SubWorkflow("SubWorkflow2")]
        public async IAsyncEnumerable<Wait> SubWorkflow2()
        {
            int x = 100;
            yield return WaitMethod<string, string>(Method2, "M2").MatchAny();
            if (x != 100)
                throw new Exception("Locals continuation problem.");
            x += 100;
            yield return WaitSubWorkflow(SubWorkflow3(), "Wait sub workflow3");
            if (x != 200)
                throw new Exception("Locals continuation problem.");
        }

        [SubWorkflow("SubWorkflow3")]
        public async IAsyncEnumerable<Wait> SubWorkflow3()
        {
            int x = 1000;
            yield return WaitMethod<string, string>(Method3, "M2").MatchAny();
            if (x != 1000)
                throw new Exception("Locals continuation problem.");
        }

        [EmitSignal("RequestAdded")] public string Method1(string input) => input + "M1";
        [EmitSignal("Method2")] public string Method2(string input) => input + "M2";
        [EmitSignal("Method3")] public string Method3(string input) => input + "M3";
        [EmitSignal("Method4")] public string Method4(string input) => input + "M4";
    }
}