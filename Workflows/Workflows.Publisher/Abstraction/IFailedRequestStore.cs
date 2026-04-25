using Workflows.Publisher.InOuts;
using System.Collections.Generic;
using System.Threading.Tasks;
using Workflows.Publisher.Abstraction;
using Workflows;
using Workflows.Publisher;

namespace Workflows.Publisher.Abstraction
{
    public interface IFailedRequestStore
    {
        Task Add(InOuts.FailedRequest request);
        Task Update(InOuts.FailedRequest request);
        Task Remove(InOuts.FailedRequest request);
        Task<bool> HasRequests();
        IEnumerable<InOuts.FailedRequest> GetRequests();
    }
}
