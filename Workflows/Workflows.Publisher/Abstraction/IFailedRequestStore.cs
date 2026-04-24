using Workflows.Sender.InOuts;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Workflows.Sender.Abstraction
{
    public interface IFailedRequestStore
    {
        Task Add(FailedRequest request);
        Task Update(FailedRequest request);
        Task Remove(FailedRequest request);
        Task<bool> HasRequests();
        IEnumerable<FailedRequest> GetRequests();
    }
}
