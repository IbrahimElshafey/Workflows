using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Workflows.Abstraction.DTOs.Waits;
using Workflows.Abstraction.Runner;
using Workflows.Abstraction.DTOs.Registration;
using Workflows.Abstraction.Persistence;
using Workflows.Primitives;

namespace Workflows.Storage.EntityFrameworkCore
{
    public class DefinitionRepository : IDefinitionRepository
    {
        private readonly WorkflowsDbContext _dbContext;
        private readonly IServiceProvider _serviceProvider;

        public DefinitionRepository(WorkflowsDbContext dbContext, IServiceProvider serviceProvider)
        {
            _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
            _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        }

        public async Task<RegistrationSyncResult> SyncDefinitionsAsync(BulkRegistrationPackage package)
        {
            if (package == null) throw new ArgumentNullException(nameof(package));

            using (var transaction = await _dbContext.Database.BeginTransactionAsync())
            {
                try
                {
                    // Update/Sync Workflows
                    foreach (var w in package.Workflows)
                    {
                        var dbW = await _dbContext.WorkflowDefinitions.FindAsync(w.WorkflowName, w.Version);
                        if (dbW == null)
                        {
                            _dbContext.WorkflowDefinitions.Add(new WorkflowDefinitionEntity
                            {
                                WorkflowName = w.WorkflowName,
                                Version = w.Version,
                                WorkflowTypeName = w.WorkflowTypeName,
                                WorkflowTypeSchema = w.WorkflowTypeSchema,
                                RegisteredAt = w.RegisteredAt
                            });
                        }
                        else
                        {
                            dbW.WorkflowTypeName = w.WorkflowTypeName;
                            dbW.WorkflowTypeSchema = w.WorkflowTypeSchema;
                            dbW.RegisteredAt = w.RegisteredAt;
                            _dbContext.WorkflowDefinitions.Update(dbW);
                        }
                    }

                    // Update/Sync Signals
                    foreach (var s in package.Signals)
                    {
                        var dbS = await _dbContext.SignalDefinitions.FindAsync(s.SignalIdentifier);
                        if (dbS == null)
                        {
                            _dbContext.SignalDefinitions.Add(new SignalDefinitionEntity
                            {
                                SignalIdentifier = s.SignalIdentifier,
                                PayloadTypeName = s.PayloadTypeName,
                                PayloadSchema = s.PayloadSchema
                            });
                        }
                        else
                        {
                            dbS.PayloadTypeName = s.PayloadTypeName;
                            dbS.PayloadSchema = s.PayloadSchema;
                            _dbContext.SignalDefinitions.Update(dbS);
                        }
                    }

                    // Update/Sync Commands
                    foreach (var c in package.Commands)
                    {
                        var dbC = await _dbContext.CommandDefinitions.FindAsync(c.CommandName);
                        if (dbC == null)
                        {
                            _dbContext.CommandDefinitions.Add(new CommandDefinitionEntity
                            {
                                CommandName = c.CommandName,
                                PayloadTypeName = c.PayloadTypeName,
                                PayloadSchema = c.PayloadSchema,
                                ResultTypeName = c.ResultTypeName,
                                ResultSchema = c.ResultSchema,
                                DefaultTimeout = c.DefaultTimeout,
                                ExecutionMode = (int)c.ExecutionMode
                            });
                        }
                        else
                        {
                            dbC.PayloadTypeName = c.PayloadTypeName;
                            dbC.PayloadSchema = c.PayloadSchema;
                            dbC.ResultTypeName = c.ResultTypeName;
                            dbC.ResultSchema = c.ResultSchema;
                            dbC.DefaultTimeout = c.DefaultTimeout;
                            dbC.ExecutionMode = (int)c.ExecutionMode;
                            _dbContext.CommandDefinitions.Update(dbC);
                        }
                    }

                    await _dbContext.SaveChangesAsync();
                    await transaction.CommitAsync();
                }
                catch (Exception ex)
                {
                    await transaction.RollbackAsync();
                    return new RegistrationSyncResult
                    {
                        Success = false,
                        Errors = new List<RegistrationError>
                        {
                            new RegistrationError
                            {
                                EntityName = "BulkRegistration",
                                ErrorType = "DbSyncException",
                                Message = ex.Message
                            }
                        },
                        SyncTimestamp = DateTime.UtcNow
                    };
                }
            }

            // Now perform the initial registration run per workflow OUTSIDE the sync transaction
            try
            {
                var runner = _serviceProvider.GetService<IWorkflowRunner>();
                if (runner != null)
                {
                    foreach (var w in package.Workflows)
                    {
                        // A. Clean up previous first-wait instances for this workflow name to remain idempotent
                        var oldFirstWaitInstanceIds = await _dbContext.SignalWaits
                            .Where(sw => sw.IsFirstWait && _dbContext.WorkflowInstances.Any(wi => wi.Id == sw.WorkflowInstanceId && wi.WorkflowType == w.WorkflowName))
                            .Select(sw => sw.WorkflowInstanceId)
                            .Distinct()
                            .ToListAsync();

                        foreach (var oldInstId in oldFirstWaitInstanceIds)
                        {
                            var oldInst = await _dbContext.WorkflowInstances.FindAsync(oldInstId);
                            if (oldInst != null)
                            {
                                _dbContext.WorkflowInstances.Remove(oldInst);
                            }
                        }
                        await _dbContext.SaveChangesAsync();

                        // B. Run once to create the first-wait instance
                        var result = await runner.StartWorkflow(w.WorkflowName, null);
                        if (result != null)
                        {
                            var instanceId = result.Id;
                            var instance = await _dbContext.WorkflowInstances.FindAsync(instanceId);
                            if (instance != null)
                            {
                                // C. Validate that the first wait contains at least one SignalWait
                                ValidateFirstWait(instance.Waits);

                                // D. Set IsFirstWait = true on its DB index records
                                var dbSignalWaits = await _dbContext.SignalWaits
                                    .Where(sw => sw.WorkflowInstanceId == instanceId)
                                    .ToListAsync();

                                foreach (var dbSw in dbSignalWaits)
                                {
                                    dbSw.IsFirstWait = true;
                                }

                                // E. Set IsFirstWait = true inside the JSON column representation
                                UpdateIsFirstWaitRecursive(instance.Waits);

                                // Force EF Core to detect change by reassigning and setting state
                                instance.Waits = instance.Waits.ToList();
                                _dbContext.Entry(instance).State = EntityState.Modified;

                                await _dbContext.SaveChangesAsync();
                            }
                        }
                    }
                }

                return new RegistrationSyncResult
                {
                    Success = true,
                    WorkflowsRegistered = package.Workflows.Count,
                    SignalsRegistered = package.Signals.Count,
                    CommandsRegistered = package.Commands.Count,
                    SyncTimestamp = DateTime.UtcNow
                };
            }
            catch (Exception ex)
            {
                return new RegistrationSyncResult
                {
                    Success = false,
                    Errors = new List<RegistrationError>
                    {
                        new RegistrationError
                        {
                            EntityName = "BulkRegistration",
                            ErrorType = "DbSyncException",
                            Message = ex.Message
                        }
                    },
                    SyncTimestamp = DateTime.UtcNow
                };
            }
        }

        private void ValidateFirstWait(List<WaitInfrastructureDto> waits)
        {
            if (waits == null || waits.Count == 0)
            {
                throw new InvalidOperationException("Workflow first wait cannot be empty. It must be a SignalWait or a GroupWait containing at least one SignalWait.");
            }

            bool hasSignalWait = false;
            foreach (var wait in waits)
            {
                if (ContainsSignalWait(wait))
                {
                    hasSignalWait = true;
                    break;
                }
            }

            if (!hasSignalWait)
            {
                throw new InvalidOperationException("Workflow first wait must be a SignalWait or a GroupWait containing at least one SignalWait.");
            }
        }

        private bool ContainsSignalWait(WaitInfrastructureDto wait)
        {
            if (wait is SignalWaitDto)
            {
                return true;
            }
            if (wait.ChildWaits != null)
            {
                foreach (var child in wait.ChildWaits)
                {
                    if (ContainsSignalWait(child))
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        private void UpdateIsFirstWaitRecursive(IEnumerable<WaitInfrastructureDto> waits)
        {
            if (waits == null) return;
            foreach (var wait in waits)
            {
                if (wait is SignalWaitDto sw)
                {
                    sw.IsFirstWait = true;
                }
                if (wait.ChildWaits != null)
                {
                    UpdateIsFirstWaitRecursive(wait.ChildWaits);
                }
            }
        }

        public async Task<WorkflowDefinition> GetDefinitionAsync(string workflowName, int version)
        {
            var db = await _dbContext.WorkflowDefinitions.FindAsync(workflowName, version);
            if (db == null) return null;
            return new WorkflowDefinition
            {
                WorkflowName = db.WorkflowName,
                Version = db.Version,
                WorkflowTypeName = db.WorkflowTypeName,
                WorkflowTypeSchema = db.WorkflowTypeSchema,
                RegisteredAt = db.RegisteredAt
            };
        }

        public async Task<SignalDefinition> GetSignalDefinitionAsync(string signalName)
        {
            var db = await _dbContext.SignalDefinitions.FindAsync(signalName);
            if (db == null) return null;
            return new SignalDefinition
            {
                SignalIdentifier = db.SignalIdentifier,
                PayloadTypeName = db.PayloadTypeName,
                PayloadSchema = db.PayloadSchema
            };
        }

        public async Task<CommandDefinition> GetCommandDefinitionAsync(string commandName)
        {
            var db = await _dbContext.CommandDefinitions.FindAsync(commandName);
            if (db == null) return null;
            return new CommandDefinition
            {
                CommandName = db.CommandName,
                PayloadTypeName = db.PayloadTypeName,
                PayloadSchema = db.PayloadSchema,
                ResultTypeName = db.ResultTypeName,
                ResultSchema = db.ResultSchema,
                DefaultTimeout = db.DefaultTimeout,
                ExecutionMode = (CommandExecutionMode)db.ExecutionMode
            };
        }
    }
}
