using Workflows.Handler.InOuts.Entities;
using System.Threading.Tasks;
namespace Workflows.Handler.Core.Abstraction
{
    public interface IFirstWaitProcessor
    {
        /// <summary>
        /// Duplicate first wait in case it's matched so incoming waits of same type could match too
        /// </summary>
        Task<MethodWaitEntity> DuplicateFirstWait(MethodWaitEntity firstMatchedMethodWait);

        /// <summary>
        /// While scanning a resumable workflow save first wait in DB
        /// </summary>
        Task RegisterFirstWait(int workflowId, string methodUrn);
    }
}