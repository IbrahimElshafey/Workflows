using Workflows.Handler;
using Workflows.Handler.Attributes;
using Workflows.Handler.BaseUse;
using Workflows.Handler.InOuts;
using Workflows.Handler.Testing;

namespace Tests;

public partial class SubWorkflowsTests
{
    [Fact]
    public async Task WorkflowAfterFirst_Test()
    {
        using var test = new TestShell(nameof(WorkflowAfterFirst_Test), typeof(WorkflowAfterFirst));
        await test.ScanTypes();

        var logs = await test.GetLogs();
        Assert.Empty(logs);

        var instance = new WorkflowAfterFirst();
        instance.Method2("m2");
        instance.Method3("m3");

        Assert.Empty(await test.RoundCheck(2, 3, 1));
    }

    [Fact]
    public async Task WorkflowAtStart_Test()
    {
        using var test = new TestShell(nameof(WorkflowAtStart_Test), typeof(SubWorkflows));
        await test.ScanTypes();

        var logs = await test.GetLogs();
        Assert.Empty(logs);

        var instance = new SubWorkflows();
        instance.Method1("m12");

        var signals = await test.GetSignals();
        Assert.Single(signals);
        var instances = await test.GetInstances<SubWorkflows>(true);
        Assert.Equal(2, instances.Count);
        Assert.Equal(1, instances.Count(x => x.Status == WorkflowInstanceStatus.Completed));
        var waits = await test.GetWaits(null, true);
        Assert.Equal(4, waits.Count);
        Assert.Equal(2, waits.Count(x => x.Status == WaitStatus.Completed));
    }

    [Fact]
    public async Task TwoWorkflowsAtFirst_Test()
    {
        using var test = new TestShell(nameof(TwoWorkflowsAtFirst_Test), typeof(TwoWorkflowsAtFirst));
        await test.ScanTypes();

        var logs = await test.GetLogs();
        Assert.Empty(logs);

        var instance = new TwoWorkflowsAtFirst();
        instance.Method1("m1");
        instance.Method2("m2");

        var signals = await test.GetSignals();
        Assert.Equal(2, signals.Count);
        var instances = await test.GetInstances<SubWorkflows>(true);
        Assert.Equal(2, instances.Count);
        Assert.Equal(1, instances.Count(x => x.Status == WorkflowInstanceStatus.Completed));
        var waits = await test.GetWaits(null, true);
        Assert.Equal(10, waits.Count);
        Assert.Equal(5, waits.Count(x => x.Status == WaitStatus.Completed));


        instance.Method1("m3");
        instance.Method2("m4");

        signals = await test.GetSignals();
        Assert.Equal(4, signals.Count);
        instances = await test.GetInstances<SubWorkflows>(true);
        Assert.Equal(3, instances.Count);
        Assert.Equal(2, instances.Count(x => x.Status == WorkflowInstanceStatus.Completed));
        waits = await test.GetWaits(null, true);
        Assert.Equal(15, waits.Count);
        Assert.Equal(10, waits.Count(x => x.Status == WaitStatus.Completed));
    }

    public class TwoWorkflowsAtFirst : WorkflowContainer
    {
        [Workflow("TwoWorkflowsAtFirst")]
        public async IAsyncEnumerable<Wait> Test()
        {
            yield return WaitGroup(new[] 
            {
                 WaitSubWorkflow(SubWorkflow1()),
                 WaitSubWorkflow(SubWorkflow2())
            }, "Wait two sub workflows");
            await Task.Delay(100);
        }

        [SubWorkflow("SubWorkflow1")]
        public async IAsyncEnumerable<Wait> SubWorkflow1()
        {
            yield return WaitMethod<string, string>(Method1, "M1").MatchAny();
        }

        [SubWorkflow("SubWorkflow2")]
        public async IAsyncEnumerable<Wait> SubWorkflow2()
        {
            yield return WaitMethod<string, string>(Method2, "M2").MatchAny();
        }

        [EmitSignal("RequestAdded")] public string Method1(string input) => input + "M1";
        [EmitSignal("Method2")] public string Method2(string input) => input + "M2";
    }

    public class WorkflowAfterFirst : WorkflowContainer
    {
        public string Message { get; set; }

        [Workflow("WorkflowAfterFirst")]
        public async IAsyncEnumerable<Wait> Test()
        {
            yield return WaitMethod<string, string>(Method2, "M2");
            yield return WaitSubWorkflow(SubWorkflow2(155), "Wait sub workflow2");
            await Task.Delay(100);
        }
            
        [SubWorkflow("SubWorkflow2")]
        public async IAsyncEnumerable<Wait> SubWorkflow2(int funcInput)
        {
            int x = 100;
            yield return WaitMethod<string, string>(Method3, "M3")
                .MatchAny()
                //.AfterMatch(InstanceCall)
                //.AfterMatch(TestMethodClass.AfterMatchExternal)
                .AfterMatch((input, output) =>
                {
                    Message = $"Input: {input}, Output: {output}";
                    if (x != 100)
                        throw new Exception("Closure not saved for sub resumable workflow.");
                    funcInput += 5;
                })
                ;
            Console.WriteLine(x);
            if (funcInput != 160)
                throw new Exception("SubWorkflow input must be 160.");
        }

        private void InstanceCall(string arg1, string arg2)
        {
            Message = $"Input: {arg1}, Output: {arg2}";
        }

        [EmitSignal("Method2")] public string Method2(string input) => input + "M2";
        [EmitSignal("Method3")] public string Method3(string input) => input + "M3";
    }
    public static class TestMethodClass
    {
        public static void AfterMatchExternal(string input, string outPut) => Console.WriteLine($"{input}#{outPut}");
    }
    public class SubWorkflows : WorkflowContainer
    {
        [Workflow("WorkflowAtStart")]
        public async IAsyncEnumerable<Wait> WorkflowAtStart()
        {
            yield return WaitSubWorkflow(SubWorkflow(), "Wait sub workflow");
            await Task.Delay(100);
        }

        [SubWorkflow("SubWorkflow")]
        public async IAsyncEnumerable<Wait> SubWorkflow()
        {
            yield return WaitMethod<string, string>(Method1, "M1").MatchAny();
        }

        [EmitSignal("RequestAdded")] public string Method1(string input) => input + "M1";

    }
}