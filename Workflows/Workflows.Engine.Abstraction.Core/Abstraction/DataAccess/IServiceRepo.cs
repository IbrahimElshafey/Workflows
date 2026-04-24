using Workflows.Handler.InOuts.Entities;

using System;using System.Threading.Tasks; namespace Workflows.Handler.Abstraction.Abstraction
{
    public interface IServiceRepo
    {
        Task UpdateDllScanDate(ServiceData dll);
        Task DeleteOldScanData(DateTime dateBeforeScan);
        Task<ServiceData> FindServiceDataForScan(string assemblyName);
        Task<ServiceData> GetServiceData(string assemblyName);
    }
}