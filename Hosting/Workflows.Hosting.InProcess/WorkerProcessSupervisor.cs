using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace Workflows.Hosting.InProcess
{
    public class WorkerProcessInfo
    {
        public string Version { get; set; } = "";
        public string PipeName { get; set; } = "";
        public Process Process { get; set; } = null!;
    }

    public class WorkerProcessSupervisor
    {
        private readonly ConcurrentDictionary<string, WorkerProcessInfo> _workers = new();
        private readonly ConcurrentDictionary<(string Type, int Version), HashSet<string>> _capabilityMap = new();
        private readonly string _workerExecutablePath;

        public IReadOnlyDictionary<string, WorkerProcessInfo> ActiveWorkers => _workers;

        public WorkerProcessSupervisor(string? workerExecutablePath = null)
        {
            _workerExecutablePath = workerExecutablePath ?? 
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Workflows.Runner.exe");
            
            if (!File.Exists(_workerExecutablePath))
            {
                _workerExecutablePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Workflows.Runner.dll");
            }
        }

        public void RegisterCapabilities(string version, System.Collections.Generic.IEnumerable<(string workflowType, int workflowVersion)> capabilities)
        {
            foreach (var (workflowType, workflowVersion) in capabilities)
            {
                var key = (workflowType, workflowVersion);
                _capabilityMap.AddOrUpdate(key, 
                    _ => new HashSet<string> { version }, 
                    (_, set) => { lock (set) set.Add(version); return set; });
            }
        }

        public string? ResolveWorkerVersionForWorkflow(string workflowType, int workflowVersion)
        {
            var key = (workflowType, workflowVersion);
            if (_capabilityMap.TryGetValue(key, out var versions))
            {
                lock (versions)
                {
                    foreach (var ver in versions)
                    {
                        if (_workers.TryGetValue(ver, out var info) && !info.Process.HasExited)
                        {
                            return ver;
                        }
                    }
                }
            }
            return null;
        }

        public async Task<WorkerProcessInfo> EnsureWorkerAsync(string version, string? assemblyPath = null, CancellationToken ct = default)
        {
            if (_workers.TryGetValue(version, out var existing) && !existing.Process.HasExited)
            {
                return existing;
            }

            string pipeName = $"wf-worker-{version.Replace('.', '-')}-{Guid.NewGuid():N}";
            string args = $"\"{_workerExecutablePath}\" --version \"{version}\" --ipc-pipe \"{pipeName}\"";
            if (!string.IsNullOrEmpty(assemblyPath))
            {
                args += $" --assembly-path \"{assemblyPath}\"";
            }

            var psi = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = args,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            var process = Process.Start(psi);
            if (process == null)
            {
                throw new InvalidOperationException($"Failed to spawn Worker sub-process for Version {version}");
            }

            var info = new WorkerProcessInfo
            {
                Version = version,
                PipeName = pipeName,
                Process = process
            };

            _workers[version] = info;

            // Wait for handshake
            bool connected = await TryHandshakeAsync(pipeName, 5, ct);
            if (!connected)
            {
                process.Kill();
                _workers.TryRemove(version, out _);
                throw new TimeoutException($"Worker process V{version} failed IPC handshake");
            }

            return info;
        }

        public async Task<bool> SendIpcCommandAsync(string version, string command, string payload, CancellationToken ct = default)
        {
            if (!_workers.TryGetValue(version, out var info) || info.Process.HasExited)
            {
                return false;
            }

            try
            {
                using var client = new NamedPipeClientStream(".", info.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
                await client.ConnectAsync(3000, ct);

                using var writer = new StreamWriter(client, Encoding.UTF8, leaveOpen: true) { AutoFlush = true };
                using var reader = new StreamReader(client, Encoding.UTF8, leaveOpen: true);

                var msg = new { Command = command, Payload = payload };
                await writer.WriteLineAsync(JsonConvert.SerializeObject(msg));

                string? line = await reader.ReadLineAsync(ct);
                return !string.IsNullOrEmpty(line);
            }
            catch
            {
                return false;
            }
        }

        public async Task ShutdownWorkerAsync(string version, CancellationToken ct = default)
        {
            if (_workers.TryRemove(version, out var info))
            {
                try
                {
                    await SendIpcCommandAsync(version, "Shutdown", "", ct);
                    if (!info.Process.HasExited)
                    {
                        info.Process.WaitForExit(3000);
                        if (!info.Process.HasExited)
                        {
                            info.Process.Kill();
                        }
                    }
                }
                catch
                {
                    if (!info.Process.HasExited)
                        info.Process.Kill();
                }
            }
        }

        private async Task<bool> TryHandshakeAsync(string pipeName, int maxRetries, CancellationToken ct)
        {
            for (int i = 0; i < maxRetries; i++)
            {
                try
                {
                    using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
                    await client.ConnectAsync(1000, ct);

                    using var writer = new StreamWriter(client, Encoding.UTF8, leaveOpen: true) { AutoFlush = true };
                    using var reader = new StreamReader(client, Encoding.UTF8, leaveOpen: true);

                    var msg = new { Command = "Handshake", Payload = "" };
                    await writer.WriteLineAsync(JsonConvert.SerializeObject(msg));

                    string? line = await reader.ReadLineAsync(ct);
                    if (!string.IsNullOrEmpty(line)) return true;
                }
                catch
                {
                    await Task.Delay(200, ct);
                }
            }
            return false;
        }
    }
}
