using Workflows.Handler;
using Workflows.Handler.Attributes;
using Workflows.Handler.BaseUse;
using Workflows.Handler.InOuts;
using Workflows.Handler.Testing;

namespace Tests;
public partial class ReplayTests
{
    [Fact]
    public async Task ReplayInSubWorkflow_Test()
    {
        using var test = new TestShell(nameof(ReplayInSubWorkflow_Test), typeof(ReplayInSubWorkflow));
        await test.ScanTypes();

        var logs = await test.GetLogs();
        Assert.Empty(logs);

        var instance = new ReplayInSubWorkflow();
        instance.Method6("Test");
        instance.Method1("Test");
        instance.Method2("Test");
        instance.Method2("Back");
        instance.Method3("Test");
        instance.Method4("Test");
        instance.Method4("Back");
        instance.Method5("Test");

        logs = await test.GetLogs();
        Assert.Empty(logs);
        var signals = await test.GetSignals();
        Assert.Equal(8, signals.Count);
        var instances = await test.GetInstances<ReplayInSubWorkflow>();
        Assert.Single(instances);
        Assert.Equal(1, instances.Count(x => x.Status == WorkflowInstanceStatus.Completed));
        //Assert.Equal(1, (instances[0].StateObject as ReplayInSubWorkflow).Counter1);

        //var waits = await test.GetWaits();
        //Assert.Equal(1, waits.Count);
        //Assert.Equal(1, waits.Count(x => x.Status == WaitStatus.Completed));
    }

    public class ReplayInSubWorkflow : WorkflowContainer
    {
        public int SharedCounter { get; set; }
        [Workflow("ReplayInSubWorkflows")]
        public async IAsyncEnumerable<Wait> Test()
        {
            yield return WaitMethod<string, string>(Method6, "M6");
            yield return WaitGroup(new[]
            {
                WaitSubWorkflow(PathOneWorkflow("123")),
                WaitSubWorkflow(PathTwoWorkflow())
            }, "Wait Two Paths");//wait two sub workflows
            yield return WaitMethod<string, string>(Method5, "M5").MatchAny();
        }
        public int Counter1 { get; set; }
        public int Counter2 { get; set; }

        [SubWorkflow("PathOneWorkflow")]
        public async IAsyncEnumerable<Wait> PathOneWorkflow(string workflowInput)
        {
            var x = 0;
            yield return
                   WaitMethod<string, string>(Method1, "M1")
                   .MatchAny()
                   .AfterMatch((_, _) =>
                   {
                       SharedCounter += 10;
                       x += 15;
                       if (workflowInput != "123")
                           throw new Exception("Workflow input must be 123");
                       workflowInput = "789";
                   });

            Counter1 += 10;
        M2_Wait:
            yield return
                WaitMethod<string, string>(Method2, "M2")
                .MatchAny(Counter1 == 10)
                .MatchIf(Counter1 == 13, (input, output) => input == "Back")
                .AfterMatch((_, _) =>
                {
                    if (Counter1 == 13 && x != 30)
                        throw new Exception("closure in replay problem");
                });

            Counter1 += 3;
            x += 15;
            //if (Counter1 < 16)
            //    yield return GoBackTo<string, string>("M2", (input, output) => input == "Back");
            if (Counter1 < 16)
                goto M2_Wait;
            if (workflowInput != "789")
                throw new Exception("Workflow input must be 789");
            await Task.Delay(100);
        }

        [SubWorkflow("PathTwoWorkflow")]
        public async IAsyncEnumerable<Wait> PathTwoWorkflow()
        {
            var x = 100;
            yield return
                  WaitMethod<string, string>(Method3, "M3")
                  .MatchAny()
                  .AfterMatch((_, _) => SharedCounter += 10);

            Counter2 += 10;
        M4:
            yield return
                WaitMethod<string, string>(Method4, "M4")
                .MatchAny(Counter2 == 10)
                .MatchIf(Counter2 == 13, (input, _) => input == "Back")
                .AfterMatch((_, _) =>
                {
                    if (!(x == 100 || x == 120))
                        throw new Exception("Locals continuation problem");
                    Console.WriteLine(x);
                });

            Counter2 += 3;
            x += 20;
            if (Counter2 < 16)
                goto M4;

            await Task.Delay(100);
        }

        [EmitSignal("Method1")]
        public string Method1(string input) => input + "M1";
        [EmitSignal("Method2")] public string Method2(string input) => input + "M2";
        [EmitSignal("Method3")] public string Method3(string input) => input + "M3";
        [EmitSignal("Method4")] public string Method4(string input) => input + "M4";
        [EmitSignal("Method5")] public string Method5(string input) => input + "M5";
        [EmitSignal("Method6")] public string Method6(string input) => input + "M6";
    }
}
