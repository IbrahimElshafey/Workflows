using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Workflows.Abstraction.DTOs.Registration;
using Workflows.Abstraction.Persistence;
using Workflows.Primitives;

namespace Workflows.Orchestrator.Data.EF
{
    public class EfDefinitionRepository : IDefinitionRepository
    {
        private readonly WorkflowsDbContext _dbContext;

        public EfDefinitionRepository(WorkflowsDbContext dbContext)
        {
            _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
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
                            _dbContext.WorkflowDefinitions.Add(new DbWorkflowDefinition
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
                            _dbContext.SignalDefinitions.Add(new DbSignalDefinition
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
                            _dbContext.CommandDefinitions.Add(new DbCommandDefinition
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
        }

        public async Task<WorkflowDefinition> GetDefinitionAsync(string workflowName, string version)
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
