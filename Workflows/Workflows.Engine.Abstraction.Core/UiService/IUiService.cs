using System.Collections.Generic;
using System.Threading.Tasks;
using Workflows.Handler.InOuts.Entities;
using Workflows.Handler.UiService.InOuts;
namespace Workflows.Handler.UiService
{
    public interface IUiService
    {
        Task<List<LogRecord>> GetLogs(int page = 0, int serviceId = -1, int statusCode = -1);
        Task<List<MethodGroupInfo>> GetMethodGroupsSummary(int serviceId = -1, string searchTerm = null);
        Task<List<MethodInGroupInfo>> GetMethodsInGroup(int groupId);
        Task<List<ServiceData>> GetServices();
        Task<List<ServiceInfo>> GetServicesSummary();
        Task<SignalDetails> GetSignalDetails(long signalId);
        Task<List<SignalInfo>> GetSignals(int page = 0, int serviceId = -1, string searchTerm = null);
        Task<List<MethodWaitDetails>> GetWaitsInGroup(int groupId);
        Task<WorkflowInstanceDetails> GetWorkflowInstanceDetails(int instanceId);
        Task<List<WorkflowInstanceInfo>> GetWorkflowInstances(int workflowId);
        Task<List<WorkflowInfo>> GetWorkflowsSummary(int serviceId = -1, string searchTerm = null);
    }
}
