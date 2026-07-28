using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Workflows.Runner
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            string version = GetArg(args, "--version") ?? "1.0.0";
            string pipeName = GetArg(args, "--ipc-pipe") ?? $"wf-worker-{Guid.NewGuid():N}";
            string? assemblyPath = GetArg(args, "--assembly-path");

            Console.WriteLine($"[Workflows.Runner Worker] Starting sub-process PID={Environment.ProcessId}, Version={version}, Pipe={pipeName}");

            var server = new IpcWorkerServer(pipeName, version, assemblyPath);
            using var cts = new CancellationTokenSource();

            Console.CancelKeyPress += (s, e) =>
            {
                e.Cancel = true;
                cts.Cancel();
            };

            await server.StartAsync(cts.Token);
        }

        private static string? GetArg(string[] args, string flag)
        {
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i].Equals(flag, StringComparison.OrdinalIgnoreCase))
                    return args[i + 1];
            }
            return null;
        }
    }
}
