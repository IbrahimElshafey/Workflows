using Hangfire;
using Medallion.Threading;
using Microsoft.EntityFrameworkCore;
using Workflows.Handler.InOuts;
using System.Reflection;

using System;using System.Threading.Tasks; namespace Workflows.Handler.Core.Abstraction
{
    public interface IWorkflowsSettings
    {
        IGlobalConfiguration HangfireConfig { get; }
        DbContextOptionsBuilder WaitsDbConfig { get; }
        string CurrentServiceUrl { get; }
        int CurrentServiceId { get; set; }

        IDistributedLockProvider DistributedLockProvider { get; }
        string[] DllsToScan { get; }

        bool ForceRescan { get; set; }
        string CurrentWaitsDbName { get; }
        string CurrentServiceName => Assembly.GetEntryAssembly().GetName().Name;

        CleanDatabaseSettings CleanDbSettings { get; }
        WaitStatus WaitStatusIfProcessingError { get; }
    }
}
