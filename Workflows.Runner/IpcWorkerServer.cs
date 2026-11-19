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

using System.Threading.Channels;

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
        private readonly Channel<(IpcMessage Msg, TaskCompletionSource<IpcResponse> Tcs)> _requestChannel;
        private IServiceProvider? _serviceProvider;

        public IpcWorkerServer(string pipeName, string version, string? assemblyPath, int channelCapacity = 1000)
        {
            _pipeName = pipeName;
            _version = version;
            _assemblyPath = assemblyPath;

            var options = new BoundedChannelOptions(channelCapacity)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = false,
                SingleWriter = false
            };
            _requestChannel = Channel.CreateBounded<(IpcMessage, TaskCompletionSource<IpcResponse>)>(options);
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

            // Start background consumer loop to process requests from bounded channel
            var consumerTask = Task.Run(() => ProcessChannelRequestsAsync(ct), ct);

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

                    var tcs = new TaskCompletionSource<IpcResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
                    await _requestChannel.Writer.WriteAsync((msg, tcs), ct);

                    var response = await tcs.Task;
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

            _requestChannel.Writer.TryComplete();
            try { await consumerTask; } catch { }
        }

        private async Task ProcessChannelRequestsAsync(CancellationToken ct)
        {
            await foreach (var (msg, tcs) in _requestChannel.Reader.ReadAllAsync(ct))
            {
                try
                {
                    var response = await ProcessCommandAsync(msg);
                    tcs.TrySetResult(response);
                }
                catch (Exception ex)
                {
                    tcs.TrySetResult(new IpcResponse { Success = false, Error = ex.ToString() });
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
