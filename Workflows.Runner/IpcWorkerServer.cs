using System;
using System.IO;
using System.IO.Pipes;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using Workflows.Abstraction.DTOs;
using Workflows.Abstraction.Runner;

namespace Workflows.Runner
{
    public class IpcMessage
    {
        public string Command { get; set; } = "";
        public string Payload { get; set; } = "";
    }

    public class IpcResponse
    {
        public bool Success { get; set; }
        public string Payload { get; set; } = "";
        public string Error { get; set; } = "";
    }

    public class IpcWorkerServer
    {
        private readonly string _pipeName;
        private readonly string _version;
        private readonly string? _assemblyPath;
        private IServiceProvider? _serviceProvider;

        public IpcWorkerServer(string pipeName, string version, string? assemblyPath)
        {
            _pipeName = pipeName;
            _version = version;
            _assemblyPath = assemblyPath;
        }

        public async Task StartAsync(CancellationToken ct)
        {
            var services = new ServiceCollection();
            services.AddWorkflowsRunner();
            _serviceProvider = services.BuildServiceProvider();

            if (!string.IsNullOrEmpty(_assemblyPath) && File.Exists(_assemblyPath))
            {
                try
                {
                    Assembly.LoadFrom(_assemblyPath);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Runner Worker] Failed to load assembly from {_assemblyPath}: {ex.Message}");
                }
            }

            while (!ct.IsCancellationRequested)
            {
                try
                {
                    using var server = new NamedPipeServerStream(
                        _pipeName,
                        PipeDirection.InOut,
                        1,
                        PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous);

                    await server.WaitForConnectionAsync(ct);

                    using var reader = new StreamReader(server, Encoding.UTF8, leaveOpen: true);
                    using var writer = new StreamWriter(server, Encoding.UTF8, leaveOpen: true) { AutoFlush = true };

                    string? line = await reader.ReadLineAsync(ct);
                    if (string.IsNullOrEmpty(line)) continue;

                    var msg = JsonConvert.DeserializeObject<IpcMessage>(line);
                    if (msg == null) continue;

                    var response = await ProcessCommandAsync(msg);
                    await writer.WriteLineAsync(JsonConvert.SerializeObject(response));

                    if (msg.Command == "Shutdown")
                    {
                        Console.WriteLine($"[Runner Worker {_version}] Shutdown command received. Exiting.");
                        break;
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Runner Worker] IPC Error: {ex.Message}");
                }
            }
        }

        private async Task<IpcResponse> ProcessCommandAsync(IpcMessage msg)
        {
            try
            {
                switch (msg.Command)
                {
                    case "Handshake":
                        return new IpcResponse
                        {
                            Success = true,
                            Payload = JsonConvert.SerializeObject(new { Status = "Ready", Version = _version, ProcessId = Environment.ProcessId })
                        };

                    case "RunWorkflow":
                        var req = JsonConvert.DeserializeObject<WorkflowExecutionRequest>(msg.Payload);
                        if (req == null)
                        {
                            return new IpcResponse { Success = false, Error = "Invalid WorkflowExecutionRequest payload" };
                        }

                        using (var scope = _serviceProvider!.CreateScope())
                        {
                            var runner = scope.ServiceProvider.GetRequiredService<IWorkflowRunner>();
                            var result = await runner.RunWorkflowAsync(req);
                            return new IpcResponse
                            {
                                Success = true,
                                Payload = JsonConvert.SerializeObject(result)
                            };
                        }

                    case "Shutdown":
                        return new IpcResponse
                        {
                            Success = true,
                            Payload = "Shutdown Initiated"
                        };

                    default:
                        return new IpcResponse
                        {
                            Success = false,
                            Error = $"Unknown command '{msg.Command}'"
                        };
                }
            }
            catch (Exception ex)
            {
                return new IpcResponse
                {
                    Success = false,
                    Error = ex.ToString()
                };
            }
        }
    }
}
