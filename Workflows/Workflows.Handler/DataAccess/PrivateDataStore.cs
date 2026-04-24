using Workflows.Handler.Abstraction.Abstraction;
using Workflows.Handler.InOuts.Entities;

namespace Workflows.Handler.DataAccess;

internal class PrivateDataStore : IPrivateDataStore
{
    private readonly WaitsDataContext _context;

    public PrivateDataStore(WaitsDataContext context)
    {
        _context = context;
    }

    public async Task<PrivateData> GetPrivateData(long id)
    {
        return await _context.PrivateData.FindAsync(id);
    }
}
