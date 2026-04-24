using Workflows.Handler;
using Workflows.Handler.Attributes;
using Workflows.Handler.BaseUse;

namespace ReferenceLibrary
{
    public class CodeInDllTest : WorkflowContainer
    {
        public string UserName { get; set; }

        [Workflow("TestWorkflowInDll")]
        public async IAsyncEnumerable<Wait> TestWorkflowInDll()
        {
            var x = 1;
            yield return WaitGroup(
                new[]
                {
                    WaitMethod<string, string>(SayHello, "Wait say hello").
                    AfterMatch((userName, helloMsg) => UserName = userName),

                    WaitMethod<string, string>(Method123, "Wait Method123").
                    AfterMatch((input, output) => UserName = output)
                },
                "Wait first")
                .MatchAny();
            yield return WaitMethod<string, string>
               (SayGoodby, "Wait say goodby")
               .MatchIf((userName, helloMsg) => userName == UserName)
               .AfterMatch((userName, helloMsg) =>
               {
                   if (x == 1)
                   {
                       x++;
                       throw new Exception("Exception for test.");
                   }
                   UserName = userName;
               })
               ;
            Console.WriteLine("Done");
        }

        [EmitSignal("CodeInDllTest.SayHello")]
        public string SayHello(string userName)
        {
            return $"Hello, {userName}.";
        }

        [EmitSignal("CodeInDllTest.SayGoodby")]
        public string SayGoodby(string userName)
        {
            return $"Goodby, {userName}.";
        }

        [EmitSignal("PublisherController.Method123", FromExternal = true)]
        public string Method123(string input) => default;
    }
}