using Workflows.Handler.InOuts;
using Workflows.Handler.InOuts.Entities;
using System.Linq.Expressions;

using System;
using System.Threading.Tasks;
using System.Collections.Generic;
namespace Workflows.Handler.Abstraction.Abstraction
{
    public interface IWaitsStore
    {
        Task CancelOpenedWaitsForState(int stateId);
        Task CancelSubWaits(long parentId, long signalId);
        Task<WaitEntity> GetWaitParent(WaitEntity wait);

        /// <summary>
        /// Get workflows that have active waits for given Signal URN
        /// </summary>
        Task<List<PotentialSignalEffection>> GetImpactedWorkflows(string signalUrn, DateTime signalDate);
        Task<PotentialSignalEffection> GetSignalEffectionInCurrentService(string methodUrn, DateTime signalDate);
        Task RemoveFirstWaitIfExist(int workflowIdentifier);
        Task<bool> SaveWait(WaitEntity newWait);
        Task<MethodWaitEntity> GetMethodWait(long waitId, params Expression<Func<MethodWaitEntity, object>>[] includes);
        Task<List<MethodWaitEntity>> GetPendingWaitsForTemplate(
            int templateId,
            string mandatoryPart,
            DateTime signalDate,
            params Expression<Func<MethodWaitEntity, object>>[] includes);
        Task<List<MethodWaitEntity>> GetPendingWaitsForWorkflow(int rootWorkflowId, int methodGroupId,DateTime signalDate);
    }
}