using Workflows.Handler;
using Workflows.Handler.Attributes;
using Workflows.Handler.BaseUse;
using Workflows.Handler.InOuts;
using Workflows.Handler.Testing;

namespace Tests;

public partial class SubWorkflowsTests
{
    //same workflow and sub fnction wait the same wait
    [Fact]
    public async Task SameWaitTwiceSubAndParent_Test()
    {
        using var testShell = new TestShell(nameof(SameWaitTwiceSubAndParent_Test), typeof(SameWaitTwiceSubAndParent));
        await testShell.ScanTypes();

        Assert.Empty(await testShell.RoundCheck(0, 0, 0));


        var instance = new SameWaitTwiceSubAndParent();
        instance.Method1("f1");

        //Assert.Empty(await testShell.RoundCheck(1, 2, 0));
        Assert.Equal(1, await testShell.GetWaitsCount(x => x.Status == WaitStatus.Completed));
    }

    public class SameWaitTwiceSubAndParent : WorkflowContainer
    {
        [Workflow("WorkflowTwoLevels")]
        public async IAsyncEnumerable<Wait> Test()
        {
            int x = 100;
            yield return WaitGroup(new[]
            {
                WaitSubWorkflow(SubWorkflow1("f1")),
                WaitMethod<string, string>(Method1, $"M1")
            });
            await Task.Delay(100);
            if (x != 100)
                throw new Exception("Locals continuation problem.");
        }

        [SubWorkflow("SubWorkflow1")]
        public async IAsyncEnumerable<Wait> SubWorkflow1(string workflowInput)
        {
            int x = 10;
            yield return WaitMethod<string, string>(Method1, $"M1-{workflowInput}")
                .AfterMatch((_, _) =>
                {
                    if (x != 10)
                        throw new Exception("Closure in sub workflow problem.");
                    x += 10;
                });

            //yield return Wait<string, string>(Method1, "M1")
            //    .MatchIf((input, _) => input == workflowInput);

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