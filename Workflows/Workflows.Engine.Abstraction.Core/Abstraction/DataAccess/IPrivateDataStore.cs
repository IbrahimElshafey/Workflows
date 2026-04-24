using Workflows.Handler.InOuts.Entities;
using System.Threading.Tasks;
namespace Workflows.Handler.Abstraction.Abstraction
{
    public interface IPrivateDataStore
    {
        Task<PrivateData> GetPrivateData(long id);
    }
}