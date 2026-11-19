using System;
using System.IO;
using System.Diagnostics;
using System.Runtime.Loader;
using System.Collections.Concurrent;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Workflows.Abstraction.DTOs;
using Workflows.Abstraction.Helpers;
using Workflows.Abstraction.Persistence;
using Workflows.Abstraction.Runner;
using Workflows.Communication.Abstraction;
using Workflows.Definition;
using Workflows.Definition.Registration;
using Workflows.Runner;
using Workflows.Runner.Migration;

namespace Workflows.Hosting.InProcess
{
    public class WorkflowVersionRouter
    {
        private readonly IServiceProvider _sp;
        private static readonly ConcurrentDictionary<string, Assembly> _loadedAssemblies = new();

        public WorkflowVersionRouter(IServiceProvider sp)
        {
            _sp = sp ?? throw new ArgumentNullException(nameof(sp));
        }

        public async Task<AsyncResult?> RouteAsync(WorkflowExecutionRequest request, CancellationToken ct)
        {
            if (request == null || request.WorkflowState == null) return null;

            var workflowName = request.WorkflowState.WorkflowType;
            var instanceVersion = request.WorkflowState.WorkflowVersion;

            // Check if the instance's exact version is registered in memory (SxS without disk archive)
            if (WorkflowDefinitionRegistry.TryGetWorkflow(workflowName, instanceVersion, out _))
            {
                // The exact version is already in memory — no routing needed, runner will use it
                return null;
            }

            // Get the latest registered version for comparison
            if (!WorkflowDefinitionRegistry.TryGetLatestWorkflow(workflowName, out var tuple))
            {
                return null;
            }

            var currentVersion = tuple.WorkflowContainer.GetCustomAttribute<WorkflowAttribute>()?.Version ?? 1;

            if (instanceVersion == currentVersion)
            {
                return null;
            }

            var migrationKey = $"{workflowName}:{instanceVersion}:{currentVersion}";
            var executor = _sp.GetKeyedService<IWorkflowMigrationExecutor>(migrationKey);

            if (executor != null)
            {
                await executor.MigrateAsync(request.WorkflowState.Id, ct);

                var store = _sp.GetRequiredService<IWorkflowStore>();
                var migratedState = await store.GetInstanceStateAsync(request.WorkflowState.Id);
                if (migratedState != null)
                {
                    request.WorkflowState = migratedState;
                }
                return null;
            }
            else
            {
                return await ExecuteSxSAsync(workflowName, instanceVersion, request, ct);
            }
        }

        private async Task<AsyncResult> ExecuteSxSAsync(
            string workflowName,
            int version,
            WorkflowExecutionRequest request,
            CancellationToken ct)
        {
            // 1. Get or compile assembly
            var assembly = GetOrCompileArchivedAssembly(workflowName, version);

            // 2. Scan for workflow type in ALC assembly
            Type? workflowType = null;
            foreach (var type in assembly.GetTypes())
            {
                if (typeof(WorkflowContainer).IsAssignableFrom(type) && !type.IsAbstract)
                {
                    workflowType = type;
                    break;
                }
            }

            if (workflowType == null)
            {
                throw new InvalidOperationException($"No workflow container class found in archived assembly for {workflowName} V{version}.");
            }

            // 3. Create isolated ServiceProvider inside ALC
            var services = new ServiceCollection();
            
            services.AddSingleton<IObjectSerializer>(_sp.GetRequiredService<IObjectSerializer>());
            services.AddSingleton<IExpressionSerializer>(_sp.GetRequiredService<IExpressionSerializer>());
            services.AddSingleton<IMessageDispatcher>(_sp.GetRequiredService<IMessageDispatcher>());
            services.AddScoped<IWorkflowStore>(_ => _sp.GetRequiredService<IWorkflowStore>());
            services.AddScoped<IWorkflowRunnerClient>(_ => _sp.GetRequiredService<IWorkflowRunnerClient>());

            services.AddWorkflowsRunner();

            var alcSp = services.BuildServiceProvider();
            var builder = alcSp.GetRequiredService<IWorkflowBuilder>();

            // Register workflow class via reflection:
            var registerMethod = builder.GetType().GetMethod("RegisterWorkflow", new[] { typeof(string), typeof(int) });
            if (registerMethod == null)
            {
                throw new InvalidOperationException("Could not find RegisterWorkflow(string, int) on WorkflowBuilder.");
            }
            var genericRegister = registerMethod.MakeGenericMethod(workflowType);
            genericRegister.Invoke(builder, new object[] { workflowName, version });

            using (var scope = alcSp.CreateScope())
            {
                var session = scope.ServiceProvider.GetRequiredService<WorkflowExecutionSession>();
                // Bind completion source if executing context has one
                var runner = scope.ServiceProvider.GetRequiredService<WorkflowRunner>();
                return await runner.RunWorkflowAsync(request);
            }
        }

        private Assembly GetOrCompileArchivedAssembly(string workflowName, int version)
        {
            string key = $"{workflowName}_V{version}";
            return _loadedAssemblies.GetOrAdd(key, _ =>
            {
                string currentDir = AppDomain.CurrentDomain.BaseDirectory;
                string? archiveDir = null;
                while (currentDir != null)
                {
                    var potential = Path.Combine(currentDir, "Archive", workflowName, $"V{version}");
                    if (Directory.Exists(potential))
                    {
                        archiveDir = potential;
                        break;
                    }
                    var parent = Path.GetDirectoryName(currentDir);
                    if (parent == currentDir) break;
                    currentDir = parent!;
                }

                if (archiveDir == null)
                {
                    throw new FileNotFoundException($"Archive directory for {workflowName} V{version} not found starting from base directory {AppDomain.CurrentDomain.BaseDirectory}.");
                }

                string csprojFile = Path.Combine(archiveDir, $"{workflowName}_V{version}.csproj");
                string dllPath = Path.Combine(archiveDir, "bin", "Debug", "net10.0", $"{workflowName}_V{version}.dll");

                if (!File.Exists(dllPath))
                {
                    var startInfo = new ProcessStartInfo
                    {
                        FileName = "dotnet",
                        Arguments = $"build \"{csprojFile}\" -c Debug",
                        CreateNoWindow = true,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    };
                    using var process = Process.Start(startInfo);
                    if (process == null)
                    {
                        throw new InvalidOperationException("Failed to start dotnet build process.");
                    }
                    process.WaitForExit();
                    if (process.ExitCode != 0)
                    {
                        var error = process.StandardError.ReadToEnd();
                        var output = process.StandardOutput.ReadToEnd();
                        throw new InvalidOperationException($"Failed to compile archived project at '{csprojFile}': {error}\n{output}");
                    }
                }

                var alc = new AssemblyLoadContext($"SxS_{workflowName}_V{version}", isCollectible: true);
                return alc.LoadFromAssemblyPath(dllPath);
            });
        }
    }
}
