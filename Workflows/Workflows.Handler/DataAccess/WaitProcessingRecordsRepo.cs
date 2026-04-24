using Workflows.Handler.Abstraction.Abstraction;
using Workflows.Handler.InOuts.Entities;

namespace Workflows.Handler.DataAccess;

internal class WaitProcessingRecordsRepo : ISignalWaitMatchStore
{
    private readonly WaitsDataContext _context;

    public WaitProcessingRecordsRepo(WaitsDataContext context)
    {
        _context = context;
    }

    public SignalWaitMatch Add(SignalWaitMatch waitProcessingRecord)
    {
        _context.WaitProcessingRecords.Add(waitProcessingRecord);
        return waitProcessingRecord;
    }
}