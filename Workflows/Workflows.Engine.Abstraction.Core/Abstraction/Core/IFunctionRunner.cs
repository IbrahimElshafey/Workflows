using System.Collections.Generic;
using System.Threading.Tasks;
using Workflows.Handler.BaseUse;
using Workflows.Handler.InOuts.Entities;
namespace Workflows.Handler.Core.Abstraction
{
    public interface IWorkflowRunner: IAsyncEnumerator<Wait>
    {
        Wait Current { get; }
        WaitEntity CurrentWaitEntity { get; }
        int State { get; set; }
        ValueTask DisposeAsync();
        ValueTask<bool> MoveNextAsync();
    }
}