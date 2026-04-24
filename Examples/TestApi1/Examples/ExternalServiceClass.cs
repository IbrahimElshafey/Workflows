using Workflows.Handler.Attributes;

namespace TestApi1.Examples
{
    public class ExternalServiceClass
    {

        [EmitSignal("TestController.ExternalMethodTest")]
        public int ExternalMethodTest(object o)
        {
            return default;
        }

        [EmitSignal("TestController.ExternalMethodTest2")]
        public int ExternalMethodTest2(string o)
        {
            return default;
        }

        [EmitSignal("CodeInDllTest.SayHello")]
        public string SayHelloExport(string userName)
        {
            return userName;
        }

        [EmitSignal("CodeInDllTest.SayGoodby")]
        public string SayGoodby(string userName)
        {
            return userName;
        }
    }
}