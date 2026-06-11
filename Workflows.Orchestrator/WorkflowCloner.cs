using System;
using System.Collections.Generic;
using Workflows.Abstraction.DTOs;
using Workflows.Abstraction.DTOs.Waits;
using Workflows.Abstraction.Helpers;
using Workflows.Abstraction.Enums;

namespace Workflows.Orchestrator
{
    public class WorkflowCloner : IWorkflowCloner
    {
        private readonly IObjectSerializer _serializer;

        public WorkflowCloner(IObjectSerializer serializer)
        {
            _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
        }

        public WorkflowStateDto CloneStateWithNewIds(WorkflowStateDto source, out Guid newTriggeringWaitId, Guid oldTriggeringWaitId)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));

            var clone = new WorkflowStateDto
            {
                Id = Guid.NewGuid(),
                Created = DateTime.UtcNow,
                Status = source.Status,
                WorkflowType = source.WorkflowType,
                StateObject = CloneStateObject(source.StateObject),
                Waits = new List<WaitInfrastructureDto>(),
                CancellationHistory = source.CancellationHistory != null ? new List<CancellationHistoryEntry>(source.CancellationHistory) : new()
            };

            // 1. Build a map of old wait ID -> new wait ID
            var idMap = new Dictionary<Guid, Guid>();
            BuildIdMapRecursive(source.Waits, idMap);

            // 2. Map the triggering wait ID
            if (idMap.TryGetValue(oldTriggeringWaitId, out var mappedTriggerId))
            {
                newTriggeringWaitId = mappedTriggerId;
            }
            else
            {
                newTriggeringWaitId = oldTriggeringWaitId;
            }

            // 3. Clone and apply new IDs to the waits
            foreach (var wait in source.Waits)
            {
                clone.Waits.Add(CloneAndRemapWaitRecursive(wait, idMap));
            }

            // 4. Update keys in Locals in StateObject
            if (clone.StateObject?.Locals != null)
            {
                var updatedLocals = new Dictionary<string, object>();
                foreach (var kvp in clone.StateObject.Locals)
                {
                    if (Guid.TryParse(kvp.Key, out var oldWaitId) && idMap.TryGetValue(oldWaitId, out var newWaitId))
                    {
                        updatedLocals[newWaitId.ToString()] = kvp.Value;
                    }
                    else
                    {
                        updatedLocals[kvp.Key] = kvp.Value;
                    }
                }
                clone.StateObject.Locals = updatedLocals;
            }

            return clone;
        }

        private void BuildIdMapRecursive(IEnumerable<WaitInfrastructureDto> waits, Dictionary<Guid, Guid> idMap)
        {
            if (waits == null) return;
            foreach (var w in waits)
            {
                idMap[w.Id] = Guid.NewGuid();
                if (w.ChildWaits != null)
                {
                    BuildIdMapRecursive(w.ChildWaits, idMap);
                }
            }
        }

        private WaitInfrastructureDto CloneAndRemapWaitRecursive(WaitInfrastructureDto wait, Dictionary<Guid, Guid> idMap)
        {
            // Serialize and deserialize to clone the wait DTO polymorphically
            var serialized = _serializer.Serialize(wait, SerializationScope.CompilerGeneratedClass);
            var cloned = (WaitInfrastructureDto)_serializer.Deserialize(serialized, wait.GetType(), SerializationScope.CompilerGeneratedClass);

            // Apply new ID
            if (idMap.TryGetValue(wait.Id, out var newId))
            {
                cloned.Id = newId;
            }
            else
            {
                cloned.Id = Guid.NewGuid();
            }

            cloned.IsPersisted = false; // Reset persistence flag so it gets indexed

            // Reset IsFirstWait so this real instance is never mistaken for a template
            if (cloned is SignalWaitDto clonedSignal)
                clonedSignal.IsFirstWait = false;

            // Reset parent ID
            if (wait.ParentWaitId.HasValue && idMap.TryGetValue(wait.ParentWaitId.Value, out var newParentId))
            {
                cloned.ParentWaitId = newParentId;
            }

            // Remap StateMachineObjectId for sub-workflows
            if (cloned is SubWorkflowWaitDto clonedSubWorkflow)
            {
                if (idMap.TryGetValue(clonedSubWorkflow.StateMachineObjectId, out var newSMId))
                {
                    clonedSubWorkflow.StateMachineObjectId = newSMId;
                }
            }

            // Clone and apply to children recursively
            if (wait.ChildWaits != null)
            {
                cloned.ChildWaits = new List<WaitInfrastructureDto>();
                foreach (var child in wait.ChildWaits)
                {
                    cloned.ChildWaits.Add(CloneAndRemapWaitRecursive(child, idMap));
                }
            }

            return cloned;
        }

        private WorkflowStateObject CloneStateObject(WorkflowStateObject source)
        {
            if (source == null) return null!;
            var serialized = _serializer.Serialize(source, SerializationScope.CompilerGeneratedClass);
            return _serializer.Deserialize<WorkflowStateObject>(serialized, SerializationScope.CompilerGeneratedClass)!;
        }
    }
}
