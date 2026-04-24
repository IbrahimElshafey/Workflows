using Workflows.Sender.InOuts;
using System.Threading.Tasks;

namespace Workflows.Sender.Abstraction
{
    public interface IFailedRequestHandler
    {
        Task EnqueueFailedRequest(FailedRequest failedRequest);
        Task HandleFailedRequests();
    }
}
