using System;
using System.Threading.Tasks;
using Workflows.Abstraction.DTOs;
using Workflows.Abstraction.DTOs;

namespace Workflows.Abstraction.DataStore
{
    /// <summary>
    /// Internal DataStore service to query the registered blueprints.
    /// </summary>
    public interface IDefinitionRepository
    {
        /// <summary>
        /// Synchronizes the in-memory/database definitions with the Runner's package.
        /// Returns a detailed result of the sync operation.
        /// </summary>
        Task<RegistrationSyncResult> SyncDefinitionsAsync(BulkRegistrationPackage package);
        Task<WorkflowDefinition> GetDefinitionAsync(string workflowName, string version);
        Task<SignalDefinition> GetSignalDefinitionAsync(string signalName);
        Task<CommandDefinition> GetCommandDefinitionAsync(string commandName);
    }
}

