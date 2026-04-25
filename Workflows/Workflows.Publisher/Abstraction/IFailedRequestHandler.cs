using Workflows.Publisher.InOuts;
using System.Threading.Tasks;
using Workflows.Publisher.Abstraction;
using Workflows;
using Workflows.Publisher;

namespace Workflows.Publisher.Abstraction
{
    public interface IFailedRequestHandler
    {
        Task EnqueueFailedRequest(InOuts.FailedRequest failedRequest);
        Task HandleFailedRequests();
    }
}
