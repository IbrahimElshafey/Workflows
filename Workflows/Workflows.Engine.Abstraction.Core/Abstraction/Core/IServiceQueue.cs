using Workflows.Handler.InOuts;

using System;using System.Threading.Tasks; namespace Workflows.Handler.Core.Abstraction
{
    //todo:Candidate for Inbox/Outbox pattern
    public interface IServiceQueue
    {
        /// <summary>
        /// Aspect or client use ISignalDispatcher to enque work and it calls this method to mark AffectedWorkflows
        /// </summary>
        Task IdentifyAffectedWorkflows(long signalId, DateTime sendDate, string methodUrn);
        Task EnqueueEffectionPerWorkflow(PotentialSignalEffection signalImapction);
        Task ProcessSignalLocally(long signalId, string methodUrn, DateTime signalDate);
        Task SendSignalToAnotherWorkflowService(PotentialSignalEffection callImapction);
    }
}