using Workflows.Handler;
using Workflows.Handler.Attributes;
using Workflows.Handler.BaseUse;
using Workflows.Handler.Testing;

namespace Tests;

public class Attributes_Test
{
    [Fact]
    public async Task NotSyncWorkflow_Test()
    {
        using var test = new TestShell(nameof(NotSyncWorkflow_Test), typeof(AttributesUsageClass));
        await test.ScanTypes();
        var errorLogs = await test.GetLogs();
        Assert.NotEmpty(errorLogs);
    }

    public class AttributesUsageClass : WorkflowContainer
    {
        [Workflow("NotAsync")]
        public IAsyncEnumerable<Wait> NotAsync()
        {
            throw new NotImplementedException();
        }

        [EmitSignal("MethodToWait")]
        public string MethodToWait(string input)
        {
            return input?.Length.ToString();
        }
    }
}