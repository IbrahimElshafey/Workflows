using Workflows.Handler;
using Workflows.Handler.Attributes;
using Workflows.Handler.BaseUse;

namespace TestApi1.Examples
{
    public class TestExternalMethodPush : WorkflowContainer
    {

        public string Result { get; set; }

        [Workflow("TestExternalMethodPush.WorkflowThatWaitExternal")]
        public async IAsyncEnumerable<Wait> WorkflowThatWaitExternal()
        {
            yield return
             WaitMethod<string, string>(Method123, "External method [Method123]")
                 .MatchIf((input, output) => input[0] == 'M')
                 .AfterMatch((input, output) => Result = output);
            Console.WriteLine($"Output is :{Result}");
            Console.WriteLine("^^^Success for WorkflowThatWaitExternal^^^");
        }

        [EmitSignal("PublisherController.Method123", FromExternal = true)]
        public string Method123(string input) => default;
    }
}
