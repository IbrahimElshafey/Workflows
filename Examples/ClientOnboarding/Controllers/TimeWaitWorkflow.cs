using Workflows.Handler;
using Workflows.Handler.Attributes;
using Workflows.Handler.BaseUse;

namespace ClientOnboarding.Controllers;

public class TimeWaitWorkflow : WorkflowContainer
{
    [Workflow("TestTimeWait")]
    public async IAsyncEnumerable<Wait> TestTimeWaitAtStart()
    {

        yield return WaitDelay(TimeSpan.FromDays(1), "one day");
        Console.WriteLine("Time wait at start matched.");
    }

    public int Method1(int input) => 10;
}