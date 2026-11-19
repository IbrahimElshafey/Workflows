using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using Workflows.Abstraction.DTOs;
using Workflows.Abstraction.DTOs.Waits;
using Workflows.Abstraction.Enums;
using Workflows.Abstraction.Helpers;
using Workflows.Abstraction.Persistence;
using Workflows.Definition;
using Workflows.Communication.Abstraction;
using Workflows.Abstraction.Orchestrator;

namespace Workflows.Runner.Migration
{
    public interface IWorkflowMigrationExecutor
    {
        Task MigrateAsync(Guid workflowInstanceId, CancellationToken ct);
    }

    public class StatePropertySchema
    {
        public string Name { get; set; } = "";
        public string TypeFqn { get; set; } = "";
        public bool Nullable { get; set; }
    }

    public class BasicBlockSchema
    {
        public int BlockIndex { get; set; }
        public string? YieldWaitType { get; set; }
        public string? YieldWaitName { get; set; }
        public int[] Edges { get; set; } = Array.Empty<int>();
        public int YieldOrdinal { get; set; }
    }

    public class SubWorkflowSchema
    {
        public string MethodName { get; set; } = "";
        public string MethodFullPath { get; set; } = "";
        public List<BasicBlockSchema> BasicBlocks { get; set; } = new();
    }

    public class WorkflowVersionManifest
    {
        public int SchemaVersion { get; set; }
        public string WorkflowName { get; set; } = "";
        public List<StatePropertySchema> StateProperties { get; set; } = new();
        public List<BasicBlockSchema> MainCfg { get; set; } = new();
        public List<SubWorkflowSchema> SubWorkflows { get; set; } = new();

        public BasicBlockSchema? FindBlockByName(string waitName)
        {
            return MainCfg.FirstOrDefault(b => string.Equals(b.YieldWaitName, waitName, StringComparison.OrdinalIgnoreCase));
        }

        public BasicBlockSchema? FindSubWorkflowBlockByName(string methodFullPath, string waitName)
        {
            var sub = SubWorkflows.FirstOrDefault(s => string.Equals(s.MethodFullPath, methodFullPath, StringComparison.OrdinalIgnoreCase)
                                                    || string.Equals(s.MethodName, methodFullPath, StringComparison.OrdinalIgnoreCase)
                                                    || methodFullPath.EndsWith("." + s.MethodName, StringComparison.OrdinalIgnoreCase));
            return sub?.BasicBlocks.FirstOrDefault(b => string.Equals(b.YieldWaitName, waitName, StringComparison.OrdinalIgnoreCase));
        }

        public static int ResolveExactYieldOrdinal(Type? workflowContainerType, string waitName, int fallbackOrdinal)
        {
            if (workflowContainerType == null || string.IsNullOrEmpty(waitName))
                return fallbackOrdinal;

            try
            {
                var nestedTypes = workflowContainerType.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic);
                var stateMachineType = nestedTypes.FirstOrDefault(t => t.Name.StartsWith("<") && t.Name.Contains(">d__"));
                if (stateMachineType == null) return fallbackOrdinal;

                var moveNextMethod = stateMachineType.GetMethod("MoveNext", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (moveNextMethod == null) return fallbackOrdinal;

                var body = moveNextMethod.GetMethodBody();
                if (body == null) return fallbackOrdinal;

                var ilBytes = body.GetILAsByteArray();
                if (ilBytes == null) return fallbackOrdinal;

                var module = moveNextMethod.Module;
                int lastLoadedStateIndex = -1;

                for (int i = 0; i < ilBytes.Length; i++)
                {
                    byte b = ilBytes[i];

                    // ldc.i4.0 to ldc.i4.8 (0x16 to 0x1E)
                    if (b >= 0x16 && b <= 0x1E)
                    {
                        lastLoadedStateIndex = b - 0x16;
                    }
                    // ldc.i4.s (0x1F)
                    else if (b == 0x1F && i + 1 < ilBytes.Length)
                    {
                        lastLoadedStateIndex = (sbyte)ilBytes[i + 1];
                        i += 1;
                    }
                    // ldc.i4 (0x20)
                    else if (b == 0x20 && i + 4 < ilBytes.Length)
                    {
                        lastLoadedStateIndex = BitConverter.ToInt32(ilBytes, i + 1);
                        i += 4;
                    }
                    // ldstr (0x72)
                    else if (b == 0x72 && i + 4 < ilBytes.Length)
                    {
                        int token = BitConverter.ToInt32(ilBytes, i + 1);
                        i += 4;
                        try
                        {
                            string str = module.ResolveString(token);
                            if (string.Equals(str, waitName, StringComparison.OrdinalIgnoreCase))
                            {
                                if (lastLoadedStateIndex >= 0)
                                {
                                    return lastLoadedStateIndex;
                                }
                            }
                        }
                        catch { }
                    }
                }
            }
            catch
            {
            }
            return fallbackOrdinal;
        }

        public static WorkflowVersionManifest Load(string workflowName, int version)
        {
            var baseDirs = new[]
            {
                AppDomain.CurrentDomain.BaseDirectory,
                Directory.GetCurrentDirectory(),
                Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location)
            };

            var filenames = new[]
            {
                $"Schemas/{workflowName}_Schema.json",
                $"Schemas\\{workflowName}_Schema.json",
                $"Archive/{workflowName}/V{version}/{workflowName}_V{version}_Schema.json",
                $"Archive\\{workflowName}\\V{version}\\{workflowName}_V{version}_Schema.json",
                $"{workflowName}_Schema.json",
                $"{workflowName}_V{version}_Schema.json"
            };

            foreach (var baseDir in baseDirs.Where(d => !string.IsNullOrEmpty(d)))
            {
                var currentDir = baseDir;
                while (currentDir != null)
                {
                    foreach (var filename in filenames)
                    {
                        var fullPath = Path.Combine(currentDir, filename);
                        if (File.Exists(fullPath))
                        {
                            try
                            {
                                var json = File.ReadAllText(fullPath);
                                var manifest = JsonConvert.DeserializeObject<WorkflowVersionManifest>(json);
                                if (manifest != null) return manifest;
                            }
                            catch
                            {
                            }
                        }
                    }
                    currentDir = Directory.GetParent(currentDir)?.FullName;
                }
            }

            Console.WriteLine($"[WARNING] Schema sidecar for {workflowName} V{version} not found in any directories.");
            return new WorkflowVersionManifest { WorkflowName = workflowName, SchemaVersion = version };
        }
    }

    internal class WorkflowMigrationExecutor<TOld, TNew, TMigration> : IWorkflowMigrationExecutor
        where TOld : WorkflowStateWrapper
        where TNew : WorkflowStateWrapper
        where TMigration : WorkflowMigration<TOld, TNew>, new()
    {
        private readonly IWorkflowStore _store;
        private readonly IObjectSerializer _serializer;
        private readonly Mapper _mapper;
        private readonly IMessageDispatcher _messageDispatcher;
        private readonly IOrchestrator? _orchestrator;

        public WorkflowMigrationExecutor(
            IWorkflowStore store,
            IObjectSerializer serializer,
            Mapper mapper,
            IMessageDispatcher messageDispatcher,
            IOrchestrator? orchestrator = null)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
            _mapper = mapper ?? throw new ArgumentNullException(nameof(mapper));
            _messageDispatcher = messageDispatcher ?? throw new ArgumentNullException(nameof(messageDispatcher));
            _orchestrator = orchestrator;
        }

        public async Task MigrateAsync(Guid workflowInstanceId, CancellationToken ct)
        {
            // ── 1. Load the V1 state from DB ─────────────────────────────────────
            var v1State = await _store.GetInstanceStateAsync(workflowInstanceId)
                ?? throw new InvalidOperationException($"Instance {workflowInstanceId} not found.");

            // Get instance types using reflection on BaseType generic arguments
            var oldInstanceType = typeof(TOld).BaseType!.GetGenericArguments()[0];
            var newInstanceType = typeof(TNew).BaseType!.GetGenericArguments()[0];

            // ── 2. Reconstruct typed V1 and V2 wrappers ───────────────────────────
            object? v1StateInput = null;
            if (v1State.StateObject.Locals != null && v1State.StateObject.Locals.TryGetValue("state", out var stateObj))
            {
                v1StateInput = stateObj;
            }
            else
            {
                v1StateInput = v1State.StateObject.Instance;
            }

            var v1InstanceJson = _serializer.Serialize(v1StateInput, SerializationScope.CompilerGeneratedClass);
            var v1Instance = _serializer.Deserialize(v1InstanceJson, oldInstanceType, SerializationScope.CompilerGeneratedClass);
            var v1Wrapper = (TOld)Activator.CreateInstance(typeof(TOld), v1State, v1Instance)!;

            var v2Instance = Activator.CreateInstance(newInstanceType)!;
            var v2State = new WorkflowStateDto
            {
                Id = workflowInstanceId,
                Created = v1State.Created,
                Status = v1State.Status,
                WorkflowType = typeof(TNew).Namespace!.Replace(".Archive", "").Split('.').Last() // or get from TMigration attribute
            };

            // Read metadata attribute to get target workflow details
            var attr = typeof(TMigration).GetCustomAttribute<WorkflowMigrationAttribute>()
                ?? throw new InvalidOperationException($"Migration {typeof(TMigration).Name} is missing [WorkflowMigrationAttribute].");

            v2State.WorkflowType = attr.WorkflowName;
            v2State.WorkflowVersion = attr.ToVersion;

            var v2Wrapper = (TNew)Activator.CreateInstance(typeof(TNew), v2State, v2Instance)!;

            // ── 3. Phase 1 — MigrateInstance ─────────────────────────────────────
            var migration = new TMigration();
            
            try
            {
                if (migration.Strategy == MigrationStrategy.CancelAndRespawn)
                {
                    Guid? newInstanceId = await migration.OnMigrateAsync(v1Wrapper, v2Wrapper, _orchestrator, ct)
                        ?? await migration.OnMigrateAsync(v1Wrapper, _orchestrator, ct);

                    // Atomically cancel V1 instance with audit link pointing to V2 instance
                    v1State.Status = WorkflowInstanceStatus.Canceled;
                    v1State.CancellationHistory.Add(new CancellationHistoryEntry
                    {
                        Token = "CancelAndRespawn",
                        CancelledAt = DateTime.UtcNow,
                        Reason = $"Replaced and respawned as V2 workflow instance ({attr.WorkflowName} v{attr.ToVersion}).",
                        ReplacementWorkflowInstanceId = newInstanceId?.ToString(),
                        AuditLink = newInstanceId.HasValue ? $"Respawned as V2 Instance ID: {newInstanceId.Value}" : null
                    });

                    await _store.ReplaceMigratedStateAsync(
                        workflowInstanceId,
                        v1State,
                        v1State.Waits,
                        newVersion: v1State.WorkflowVersion,
                        ct);

                    return;
                }

                migration.MigrateInstance(v1Wrapper, v2Wrapper);

                // Resolve target container instance type
                object? containerInstance = null;
                if (global::Workflows.Definition.Registration.WorkflowDefinitionRegistry.Workflows.TryGetValue(attr.WorkflowName, out var tuple))
                {
                    try
                    {
                        containerInstance = Activator.CreateInstance(tuple.WorkflowContainer);
                    }
                    catch { }
                }

                // Populate state object properties on V2 DTO
                v2State.StateObject = new WorkflowStateObject
                {
                    WorkflowType = attr.WorkflowName,
                    Instance = containerInstance,
                    StateIndex = v1State.StateObject.StateIndex,
                    Locals = v1State.StateObject.Locals ?? new Dictionary<string, object>()
                };
                v2State.StateObject.Locals["state"] = v2Instance;

                // ── 4. Phase 2+3 — Walk and migrate all active waits ─────────────────
                var waitsToMigrate = v1State.Waits.Where(w => w.Status == WaitStatus.Waiting);
                var migratedWaits = MigrateWaitsRecursive(waitsToMigrate, migration, v2Wrapper);

                // ── 5. Resolve PlaceholderWait → real StateAfterWait from V2 manifest ─
                var v2Manifest = WorkflowVersionManifest.Load(attr.WorkflowName, attr.ToVersion);
                var resolvedWaits = ResolveStateIndices(migratedWaits, v2Manifest);

                // Remap top-level StateIndex to the primary active wait's StateAfterWait in V2
                var primaryActiveWait = resolvedWaits.FirstOrDefault(w => w.Status == WaitStatus.Waiting);
                if (primaryActiveWait != null && primaryActiveWait.StateAfterWait >= 0)
                {
                    v2State.StateObject.StateIndex = primaryActiveWait.StateAfterWait;
                }

                // ── 6. Atomic DB update (new WorkflowStore method) ───────────────────
                await _store.ReplaceMigratedStateAsync(
                    workflowInstanceId,
                    v2State,
                    resolvedWaits,
                    newVersion: attr.ToVersion,
                    ct);

                // ── 7. Dispatch scheduled commands (POST-commit, fire-and-forget) ─────
                foreach (var cmd in migration._scheduledCommands)
                {
                    await _messageDispatcher.DispatchAsync(cmd);
                }
            }
            catch (WorkflowMigrationCancelledException ex)
            {
                // Intercept WorkflowMigrationCancelledException to transition the instance to WorkflowInstanceStatus.Canceled (400),
                // append a CancellationHistoryEntry, and stop execution.
                v1State.Status = WorkflowInstanceStatus.Canceled;
                v1State.CancellationHistory.Add(new CancellationHistoryEntry
                {
                    Token = "Migration",
                    CancelledAt = DateTime.UtcNow,
                    Reason = ex.Reason
                });

                await _store.ReplaceMigratedStateAsync(
                    workflowInstanceId,
                    v1State,
                    v1State.Waits,
                    newVersion: v1State.WorkflowVersion,
                    ct);
            }
        }

        private List<WaitInfrastructureDto> MigrateWaitsRecursive(
            IEnumerable<WaitInfrastructureDto> waits,
            WorkflowMigration<TOld, TNew> migration,
            TNew _new)
        {
            var result = new List<WaitInfrastructureDto>();

            foreach (var wait in waits)
            {
                if (wait.Status == WaitStatus.Completed)
                {
                    result.Add(wait);
                    continue;
                }

                var migratedWaitObj = migration.MigrateActiveWait(wait, _new);
                var newWait = migratedWaitObj.Wait;
                var targetStateIndex = migratedWaitObj.TargetStateIndex;

                if (wait is GroupWaitDto group)
                {
                    var newGroupDto = _mapper.MapToDto(newWait) as GroupWaitDto
                        ?? new GroupWaitDto { WaitName = newWait.WaitName };

                    if (targetStateIndex.HasValue)
                    {
                        newGroupDto.StateAfterWait = targetStateIndex.Value;
                    }
                    else if (newGroupDto.StateAfterWait == 0)
                    {
                        newGroupDto.StateAfterWait = group.StateAfterWait;
                    }

                    newGroupDto.ChildWaits = MigrateWaitsRecursive(group.ChildWaits, migration, _new);
                    result.Add(newGroupDto);
                    continue;
                }

                if (wait is SubWorkflowWaitDto oldSub)
                {
                    var childMigrated = migration.MigrateSubWorkflowState(oldSub, _new);
                    var newSubDto = new SubWorkflowWaitDto
                    {
                        Id = oldSub.Id,
                        Status = oldSub.Status,
                        WaitName = oldSub.WaitName,
                        MethodFullPath = oldSub.MethodFullPath,
                        StateMachineObjectId = oldSub.StateMachineObjectId,
                        StateAfterWait = targetStateIndex ?? oldSub.StateAfterWait,
                        ChildWaits = MigrateWaitsRecursive(oldSub.ChildWaits, migration, _new)
                    };
                    result.Add(newSubDto);
                    continue;
                }

                var mappedDto = _mapper.MapToDto(newWait);
                if (targetStateIndex.HasValue)
                {
                    mappedDto.StateAfterWait = targetStateIndex.Value;
                }
                else if (mappedDto.StateAfterWait == 0)
                {
                    mappedDto.StateAfterWait = wait.StateAfterWait;
                }
                result.Add(mappedDto);


            }

            return result;
        }

        private List<WaitInfrastructureDto> ResolveStateIndices(
            List<WaitInfrastructureDto> waits, WorkflowVersionManifest manifest, string? parentSubWorkflowMethodFullPath = null)
        {
            var resolved = new List<WaitInfrastructureDto>();

            foreach (var wait in waits)
            {
                var currentWait = wait;

                if (currentWait is PlaceholderWaitDto ph)
                {
                    var block = !string.IsNullOrEmpty(parentSubWorkflowMethodFullPath)
                        ? manifest.FindSubWorkflowBlockByName(parentSubWorkflowMethodFullPath, ph.WaitName)
                        : manifest.FindBlockByName(ph.WaitName);
                    if (block != null)
                    {
                        currentWait = CreateConcreteDtoFromSchema(ph, block);
                    }
                }
                else if (currentWait is PlaceholderSubWorkflowWaitDto phSub)
                {
                    var block = manifest.FindSubWorkflowBlockByName(phSub.MethodFullPath, phSub.WaitName);
                    if (block != null)
                    {
                        currentWait = CreateConcreteDtoFromSchema(phSub, block);
                    }
                }
                else
                {
                    var block = !string.IsNullOrEmpty(parentSubWorkflowMethodFullPath)
                        ? manifest.FindSubWorkflowBlockByName(parentSubWorkflowMethodFullPath, currentWait.WaitName)
                        : manifest.FindBlockByName(currentWait.WaitName);

                    if (block != null)
                    {
                        currentWait.StateAfterWait = block.YieldOrdinal;
                    }
                }

                if (currentWait.ChildWaits?.Count > 0)
                {
                    string? nextSubPath = (currentWait is SubWorkflowWaitDto sub)
                        ? sub.MethodFullPath
                        : parentSubWorkflowMethodFullPath;

                    currentWait.ChildWaits = ResolveStateIndices(currentWait.ChildWaits, manifest, nextSubPath);
                }

                resolved.Add(currentWait);
            }

            return resolved;
        }

        private WaitInfrastructureDto CreateConcreteDtoFromSchema(WaitInfrastructureDto placeholder, BasicBlockSchema block)
        {
            WaitInfrastructureDto concrete;

            switch (block.YieldWaitType)
            {
                case "SignalWait":
                case "ISignalWait":
                    concrete = new SignalWaitDto
                    {
                        SignalIdentifier = block.YieldWaitName ?? ""
                    };
                    break;
                case "TimeWait":
                    concrete = new TimeWaitDto
                    {
                        UniqueMatchId = Guid.NewGuid().ToString()
                    };
                    break;
                case "SubWorkflowWait":
                    concrete = new SubWorkflowWaitDto();
                    break;
                case "GroupWait":
                    concrete = new GroupWaitDto();
                    break;
                default:
                    concrete = new SignalWaitDto
                    {
                        SignalIdentifier = block.YieldWaitName ?? ""
                    };
                    break;
            }

            concrete.Id = placeholder.Id;
            concrete.WaitName = placeholder.WaitName;
            concrete.CallerName = placeholder.CallerName;
            concrete.InCodeLine = placeholder.InCodeLine;
            concrete.Created = placeholder.Created;
            concrete.StateKey = placeholder.StateKey;
            concrete.ParentWaitId = placeholder.ParentWaitId;
            concrete.ChildWaits = placeholder.ChildWaits;
            concrete.CancelTokens = placeholder.CancelTokens;
            concrete.IsPersisted = placeholder.IsPersisted;
            concrete.StateAfterWait = block.YieldOrdinal;

            return concrete;
        }
    }
}
