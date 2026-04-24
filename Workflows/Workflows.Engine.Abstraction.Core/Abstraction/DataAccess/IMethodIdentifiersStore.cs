using Workflows.Handler.InOuts;
using Workflows.Handler.InOuts.Entities;
using System.Threading.Tasks;
namespace Workflows.Handler.Abstraction.Abstraction
{
    public interface IMethodIdentifiersStore
    {
        //todo:revist this for rename
        Task AddMethodIdentifier(MethodData methodData);
        Task<(int MethodId, int GroupId)> GetId(MethodWaitEntity methodWait);
        Task<MethodIdentifier> GetMethodIdentifierById(int? methodWaitMethodToWaitId);
        Task<bool> CanPublishFromExternal(string methodUrn);
    }
}