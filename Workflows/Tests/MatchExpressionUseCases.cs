using Workflows.Handler;
using Workflows.Handler.Attributes;
using Workflows.Handler.BaseUse;
using Workflows.Handler.InOuts;
using Workflows.Handler.Testing;

namespace Tests;

public class MatchExpressionUseCases
{
    [Fact]
    public async Task MatchExpressionUseCases_Test()
    {
        using var test = new TestShell(nameof(MatchExpressionUseCases_Test), typeof(TestClass));
        await test.ScanTypes();

        var logs = await test.GetLogs();
        Assert.Empty(logs);

        var instance = new TestClass();
        instance.Method6("Test");

        logs = await test.GetLogs();
        Assert.Empty(logs);
        var signals = await test.GetSignals();
        Assert.Single(signals);
        var instances = await test.GetInstances<TestClass>();
        Assert.Single(instances);
        Assert.Equal(1, instances.Count(x => x.Status == WorkflowInstanceStatus.Completed));
    }

    public class TestClass : WorkflowContainer
    {
        [MessagePack.IgnoreMember]
        public Dep1 dep1;//must be public if used in the expression trees and [MessagePack.IgnoreMember] to not serialize it
        private void SetDependencies()
        {
            dep1 = new Dep1(5);
        }

        [Workflow("MatchWithInstanceMethodCall")]
        public async IAsyncEnumerable<Wait> Test()
        {
            yield return
                WaitMethod<string, string>(Method6, "M6")
                .MatchIf((input, output) => 
                input == "Test" && //normal expression
                InstanceCall(input, output) && //instance call in current class
                dep1.MethodIndep(input) > 0 && //instance method in dependacies
                TestClass.StaticMethod(input) //Static method in current class
                );
        }

        public static bool StaticMethod(string input)
        {
            return input.Length == 4;
        }
        private bool InstanceCall(string input, string output)
        {
            return output == "TestM6" && input.Length == 4;
        }

        [EmitSignal("Method6")] public string Method6(string input) => input + "M6";
    }

    public class Dep1
    {
        public Dep1(int b)
        {

        }
        public int MethodIndep(string input) => input.Length;
    }
}
