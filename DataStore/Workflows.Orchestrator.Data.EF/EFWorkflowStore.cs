using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Workflows.Abstraction.DTOs;
using Workflows.Abstraction.DTOs.Waits;
using Workflows.Orchestrator.Data.EF.Entities;
using Newtonsoft.Json;

namespace Workflows.Orchestrator.Data.EF
{
    public interface IWorkflowStore
    {
        Task SaveWorkflowStateAsync(Guid workflowId, WorkflowStateDto state);
        Task<WorkflowStateDto?> LoadWorkflowStateAsync(Guid workflowId);
        Task<List<SignalWaitMatch>> FindMatchingWaitsAsync(string signalIdentifier, string signalDataJson);
    }

    public class SignalWaitMatch
    {
        public Guid WorkflowId { get; set; }
        public Guid WaitId { get; set; }
        public SignalWaitDto WaitDto { get; set; } = null!;
    }

    public class EFWorkflowStore : IWorkflowStore
    {
        private readonly WorkflowsDbContext _dbContext;

        public EFWorkflowStore(WorkflowsDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        public async Task SaveWorkflowStateAsync(Guid workflowId, WorkflowStateDto state)
        {
            var entity = await _dbContext.WorkflowStates
                .Include(w => w.Waits)
                .FirstOrDefaultAsync(w => w.Id == workflowId);

            var stateJson = JsonConvert.SerializeObject(state);

            if (entity == null)
            {
                entity = new WorkflowStateEntity
                {
                    Id = workflowId,
                    WorkflowType = state.WorkflowType,
                    Version = 1,
                    StateJson = stateJson,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };
                _dbContext.WorkflowStates.Add(entity);
            }
            else
            {
                entity.StateJson = stateJson;
                entity.Version += 1;
                entity.UpdatedAt = DateTime.UtcNow;
                _dbContext.Waits.RemoveRange(entity.Waits);
            }

            foreach (var wait in state.Waits)
            {
                if (wait is SignalWaitDto signalWait)
                {
                    var exactMatchPaths = signalWait.SignalExactMatchPaths != null ? signalWait.SignalExactMatchPaths.ToList() : new List<string>();
                    entity.Waits.Add(new WaitEntity
                    {
                        Id = wait.Id,
                        WorkflowInstanceId = workflowId,
                        WaitType = "Signal",
                        SignalIdentifier = signalWait.SignalIdentifier,
                        ExactMatchPart = signalWait.ExactMatchPart,
                        SignalExactMatchPaths = string.Join(",", exactMatchPaths),
                        WaitDtoJson = JsonConvert.SerializeObject(signalWait)
                    });
                }
                else
                {
                    entity.Waits.Add(new WaitEntity
                    {
                        Id = wait.Id,
                        WorkflowInstanceId = workflowId,
                        WaitType = wait.GetType().Name,
                        SignalIdentifier = string.Empty,
                        WaitDtoJson = JsonConvert.SerializeObject(wait)
                    });
                }
            }

            await _dbContext.SaveChangesAsync();
        }

        public async Task<WorkflowStateDto?> LoadWorkflowStateAsync(Guid workflowId)
        {
            var entity = await _dbContext.WorkflowStates.FirstOrDefaultAsync(w => w.Id == workflowId);
            if (entity == null) return null;

            return JsonConvert.DeserializeObject<WorkflowStateDto>(entity.StateJson);
        }

        public async Task<List<SignalWaitMatch>> FindMatchingWaitsAsync(string signalIdentifier, string signalDataJson)
        {
            // Exact Match Query
            var exactMatches = await _dbContext.Waits
                .Where(w => w.WaitType == "Signal" && w.SignalIdentifier == signalIdentifier)
                .ToListAsync();

            // Partial Match using Raw SQL Query
            var rawSqlMatches = await _dbContext.Waits
                .FromSqlRaw("SELECT * FROM Waits WHERE WaitType = 'Signal' AND SignalIdentifier LIKE {0}", "%" + signalIdentifier + "%")
                .ToListAsync();

            var potentialWaits = exactMatches.Union(rawSqlMatches).Distinct().ToList();
            var matches = new List<SignalWaitMatch>();

            foreach (var waitEntity in potentialWaits)
            {
                var waitDto = JsonConvert.DeserializeObject<SignalWaitDto>(waitEntity.WaitDtoJson);
                if (waitDto == null) continue;

                matches.Add(new SignalWaitMatch
                {
                    WorkflowId = waitEntity.WorkflowInstanceId,
                    WaitId = waitEntity.Id,
                    WaitDto = waitDto
                });
            }

            return matches;
        }
    }
}
