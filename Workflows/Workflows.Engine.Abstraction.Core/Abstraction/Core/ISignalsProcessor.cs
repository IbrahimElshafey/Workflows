using System;using System.Threading.Tasks; namespace Workflows.Handler.Core.Abstraction
{
    public interface ISignalsProcessor
    {

        /// <summary>
        /// Orchestrates the matching process between an incoming signal (pushed call) and pending workflow waits.
        /// It executes within a distributed lock to ensure data consistency for a specific signal-workflow pair.
        /// </summary>
        /// <remarks>
        /// This method loads potential templates, iterates through pending waits, and executes 
        /// a processing pipeline for each potential match. It commits changes only upon successful pipeline execution.
        /// </remarks>
        Task ProcessSignalMatchesAsync(int workflowId, long signalId, int methodGroupId, DateTime signalDate);
    }
}