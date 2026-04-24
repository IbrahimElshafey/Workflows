using Workflows.Handler;
using Workflows.Handler.Attributes;
using Workflows.Handler.BaseUse;
using Workflows.Handler.InOuts;
using Workflows.Handler.Testing;

namespace Tests;
public partial class ReplayTests
{
    [Fact]
    public async Task GoAfter_Test()
    {
        using var test = new TestShell(nameof(GoAfter_Test), typeof(GoAfterWorkflow));
        await test.ScanTypes();

        var logs = await test.GetLogs();
        Assert.Empty(logs);

        var instance = new GoAfterWorkflow();
        instance.Method1("Test");

        var signals = await test.GetSignals();
        Assert.Equal(1, signals.Count);
        var instances = await test.GetInstances<GoAfterWorkflow>();
        Assert.Equal(1, instances.Count);
        Assert.Equal(1, instances.Count(x => x.Status == WorkflowInstanceStatus.Completed));
        Assert.Equal(2, (instances[0].StateObject as GoAfterWorkflow).Counter);

        var waits = await test.GetWaits();
        Assert.Equal(1, waits.Count);
        Assert.Equal(1, waits.Count(x => x.Status == WaitStatus.Completed));

    }

    [Fact]
    public async Task GoBefore_Test()
    {
        using var test = new TestShell(nameof(GoBefore_Test), typeof(GoBeforeWorkflow));
        await test.ScanTypes();

        var logs = await test.GetLogs();
        Assert.Empty(logs);

        var instance = new GoBeforeWorkflow();
        instance.Method1("Test1");
        instance.Method2("Test1");
        instance.Method2("Test1");

        Assert.Empty(await test.RoundCheck(3, 3, 1));

        instance.Method1("Test2");
        instance.Method2("Test2");
        instance.Method2("Test2");
        Assert.Empty(await test.RoundCheck(6, 6, 2));

    }

    [Fact]
    public async Task GoBeforeWithNewMatch_Test()
    {
        using var test = new TestShell(nameof(GoBeforeWithNewMatch_Test), typeof(GoBeforeWithNewMatchWorkflow));
        await test.ScanTypes();

        var logs = await test.GetLogs();
        Assert.Empty(logs);

        var instance = new GoBeforeWithNewMatchWorkflow();
        instance.Method1("Test1");
        instance.Method2("Test1");
        instance.Method2("Back");

        logs = await test.GetLogs();
        Assert.Empty(logs);
        var signals = await test.GetSignals();
        Assert.Equal(3, signals.Count);
        var instances = await test.GetInstances<GoBeforeWithNewMatchWorkflow>();
        Assert.Single(instances);
        Assert.Equal(1, instances.Count(x => x.Status == WorkflowInstanceStatus.Completed));
        Assert.Equal(20, (instances[0].StateObject as GoBeforeWithNewMatchWorkflow).Counter);
        var waits = await test.GetWaits();
        Assert.Equal(3, waits.Count);
        Assert.Equal(3, waits.Count(x => x.Status == WaitStatus.Completed));

        instance.Method1("Test2");
        instance.Method2("Test2");
        instance.Method2("Back");
        logs = await test.GetLogs();
        Assert.Empty(logs);
        signals = await test.GetSignals();
        Assert.Equal(6, signals.Count);
        instances = await test.GetInstances<GoBeforeWithNewMatchWorkflow>();
        Assert.Equal(2, instances.Count);
        Assert.Equal(2, instances.Count(x => x.Status == WorkflowInstanceStatus.Completed));
        Assert.Equal(20, (instances[1].StateObject as GoBeforeWithNewMatchWorkflow).Counter);
        waits = await test.GetWaits();
        Assert.Equal(6, waits.Count);
        Assert.Equal(6, waits.Count(x => x.Status == WaitStatus.Completed));

    }


    [Fact]
    public async Task ReplayGoTo_Test()
    {
        using var test = new TestShell(nameof(ReplayGoTo_Test), typeof(ReplayGoToWorkflow));
        await test.ScanTypes();

        var logs = await test.GetLogs();
        Assert.Empty(logs);

        var instance = new ReplayGoToWorkflow();
        instance.Method1("Test1");
        instance.Method2("Test1");
        instance.Method2("Test1");

        logs = await test.GetLogs();
        Assert.Empty(logs);
        var signals = await test.GetSignals();
        Assert.Equal(3, signals.Count);
        var instances = await test.GetInstances<ReplayGoToWorkflow>();
        Assert.Single(instances);
        Assert.Equal(1, instances.Count(x => x.Status == WorkflowInstanceStatus.Completed));
        Assert.Equal(16, (instances[0].StateObject as ReplayGoToWorkflow).Counter);
        var waits = await test.GetWaits();
        Assert.Equal(3, waits.Count);
        Assert.Equal(3, waits.Count(x => x.Status == WaitStatus.Completed));

        instance.Method1("Test2");
        instance.Method2("Test2");
        instance.Method2("Test2");
        logs = await test.GetLogs();
        Assert.Empty(logs);
        signals = await test.GetSignals();
        Assert.Equal(6, signals.Count);
        instances = await test.GetInstances<ReplayGoToWorkflow>();
        Assert.Equal(2, instances.Count);
        Assert.Equal(2, instances.Count(x => x.Status == WorkflowInstanceStatus.Completed));
        Assert.Equal(16, (instances[1].StateObject as ReplayGoToWorkflow).Counter);
        waits = await test.GetWaits();
        Assert.Equal(6, waits.Count);
        Assert.Equal(6, waits.Count(x => x.Status == WaitStatus.Completed));

    }

    [Fact]
    public async Task GoToWithNewMatch_Test()
    {
        using var test = new TestShell(nameof(GoToWithNewMatch_Test), typeof(GoToWithNewMatchWorkflow));
        await test.ScanTypes();

        var logs = await test.GetLogs();
        Assert.Empty(logs);

        var instance = new GoToWithNewMatchWorkflow();
        instance.Method1("Test1");
        instance.Method2("Test1");
        instance.Method2("Back");

        logs = await test.GetLogs();
        Assert.Empty(logs);
        var signals = await test.GetSignals();
        Assert.Equal(3, signals.Count);
        var instances = await test.GetInstances<GoToWithNewMatchWorkflow>();
        Assert.Single(instances);
        Assert.Equal(1, instances.Count(x => x.Status == WorkflowInstanceStatus.Completed));
        Assert.Equal(16, (instances[0].StateObject as GoToWithNewMatchWorkflow).Counter);
        var waits = await test.GetWaits();
        Assert.Equal(3, waits.Count);
        Assert.Equal(3, waits.Count(x => x.Status == WaitStatus.Completed));

        instance.Method1("Test2");
        instance.Method2("Test2");
        instance.Method2("Back");
        logs = await test.GetLogs();
        Assert.Empty(logs);
        signals = await test.GetSignals();
        Assert.Equal(6, signals.Count);
        instances = await test.GetInstances<GoToWithNewMatchWorkflow>();
        Assert.Equal(2, instances.Count);
        Assert.Equal(2, instances.Count(x => x.Status == WorkflowInstanceStatus.Completed));
        Assert.Equal(16, (instances[1].StateObject as GoToWithNewMatchWorkflow).Counter);
        waits = await test.GetWaits();
        Assert.Equal(6, waits.Count);
        Assert.Equal(6, waits.Count(x => x.Status == WaitStatus.Completed));
    }

    public class GoBeforeWithNewMatchWorkflow : WorkflowContainer
    {
        public int Counter { get; set; }
        [Workflow("GoBeforeWithNewMatch")]
        public async IAsyncEnumerable<Wait> Test()
        {
            yield return
                WaitMethod<string, string>(Method1, "M1");
        Before_M2:
            Counter += 10;
            yield return
                WaitMethod<string, string>(Method2, "M2").
                MatchAny(Counter == 10).
                MatchIf(Counter == 20, (input, output) => input == "Back");

            if (Counter < 20)
                goto Before_M2;
            await Task.Delay(100);
        }

        [EmitSignal("RequestAdded")] public string Method1(string input) => input + "M1";
        [EmitSignal("Method2")] public string Method2(string input) => input + "M2";
    }
    public class GoAfterWorkflow : WorkflowContainer
    {
        public int Counter { get; set; }

        [Workflow("GoAfterWorkflow")]
        public async IAsyncEnumerable<Wait> Test()
        {
            int localCounter = 10;
            yield return
                WaitMethod<string, string>(Method1, "M1")
                .AfterMatch((_, _) => localCounter += 10);
        after_m1:
            Counter++;
            if (Counter < 2)
                goto after_m1;

            if (localCounter != 20)
                throw new Exception("Closure restore in replay problem.");
            await Task.Delay(100);
        }

        [EmitSignal("RequestAdded")] public string Method1(string input) => input + "M1";
    }

    public class GoBeforeWorkflow : WorkflowContainer
    {
        public int Counter { get; set; }
        [Workflow("GoBeforeWorkflow")]
        public async IAsyncEnumerable<Wait> Test()
        {
            int localCounter = 10;
            yield return
                WaitMethod<string, string>(Method1, "M1");
        
        before_m2:
            localCounter += 10;
            Counter += 10;
            yield return
                WaitMethod<string, string>(Method2, "M2")
                .MatchAny()
                .AfterMatch((_, _) => localCounter += 10);

            if (Counter < 20)
                goto before_m2;
            //yield return GoBackBefore("M2");
            if (localCounter != 50)
                throw new Exception("Local variable should be 50");
            if (Counter != 20)
                throw new Exception("Counter should be 20");
            await Task.Delay(100);
        }

        [EmitSignal("RequestAdded")] public string Method1(string input) => input + "M1";
        [EmitSignal("Method2")] public string Method2(string input) => input + "M2";
    }

    public class GoToWithNewMatchWorkflow : WorkflowContainer
    {
        public int Counter { get; set; }
        [Workflow("ReplayGoToWorkflow")]
        public async IAsyncEnumerable<Wait> Test()
        {
            yield return
                WaitMethod<string, string>(Method1, "M1");

            Counter += 10;
            var x = 5;
        M2:
            yield return
                WaitMethod<string, string>(Method2, "M2")
                .MatchAny(x == 5)
                .MatchIf(x == 10, (input, output) => input == "Back" && x == 10)
                .AfterMatch((_, _) =>
                {
                    if (x != 5 && x != 10)
                        throw new Exception("Closure continuation problem.");
                });

            Counter += 3;
            x *= 2;

            if (Counter < 16)
                goto M2;

            await Task.Delay(100);
        }

        [EmitSignal("RequestAdded")] public string Method1(string input) => input + "M1";
        [EmitSignal("Method2")] public string Method2(string input) => input + "M2";
    }
    public class ReplayGoToWorkflow : WorkflowContainer
    {
        public int Counter { get; set; }
        [Workflow("ReplayGoToWorkflow")]
        public async IAsyncEnumerable<Wait> Test()
        {
            var localCounter = 10;
            yield return
                WaitMethod<string, string>(Method1, "M1")
                .AfterMatch((_, _) => localCounter += 10);

            Counter += 10;
        M2_Wait:
            yield return
                WaitMethod<string, string>(Method2, "M2")
                .AfterMatch((_, _) => localCounter += 10)
                .MatchAny();

            Counter += 3;
            localCounter += 10;

            if (Counter < 16)
                goto M2_Wait;

            if (localCounter != 60)
                throw new Exception("Locals continuation problem.");
            await Task.Delay(100);
        }

        [EmitSignal("RequestAdded")] public string Method1(string input) => input + "M1";
        [EmitSignal("Method2")] public string Method2(string input) => input + "M2";
    }
}