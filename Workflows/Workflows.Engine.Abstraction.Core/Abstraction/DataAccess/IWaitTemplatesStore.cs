using Workflows.Handler.InOuts;
using Workflows.Handler.InOuts.Entities;
using System.Threading.Tasks;
using System.Collections.Generic;
namespace Workflows.Handler.Abstraction.Abstraction
{
    public interface IWaitTemplatesStore
    {
        Task<WaitTemplate> AddNewTemplate(
            byte[] hashResult,
            object currentWorkflowInstance,
            int funcId,
            int groupId,
            int? methodId,
            int inCodeLine,
            string cancelMethodAction,
            string afterMatchAction,
            MatchExpressionParts matchExpressionParts);
        Task<WaitTemplate> AddNewTemplate(byte[] hashResult, MethodWaitEntity methodWait);
        Task<WaitTemplate> CheckTemplateExist(byte[] hash, int funcId, int groupId);
        Task<List<WaitTemplate>> GetWaitTemplatesForWorkflow(int methodGroupId, int workflowId);
        Task<WaitTemplate> GetById(int templateId);
        Task<WaitTemplate> GetWaitTemplateWithBasicMatch(int methodWaitTemplateId);
    }
}