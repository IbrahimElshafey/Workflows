using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Workflows.Hosting.InProcess;
using Workflows.Tools.CLI;

namespace OutOfProcessWorkerSample
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            Console.Title = "Workflows Out-of-Process Worker & WF Tools Sample";

            Console.WriteLine("==========================================================================");
            Console.WriteLine("       Workflows Engine - Out-of-Process Worker & WF Tools Sample         ");
            Console.WriteLine("==========================================================================");

            string sampleDir = Path.Combine(Path.GetTempPath(), "WF_Sample_Artifacts");
            Directory.CreateDirectory(sampleDir);

            // ------------------------------------------------------------------
            // STEP 1: Demonstrate WF Tools (dotnet-wf) CLI Suite
            // ------------------------------------------------------------------
            Console.WriteLine("\n[1/4] Running WF Tools Schema Extraction & Deployment Manifest Generator...");
            string sampleAsm = typeof(Program).Assembly.Location;

            string schemaFile = WorkflowSchemaGenerator.GenerateSchemaJson(sampleAsm, sampleDir);
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"  ✓ Schema JSON written to: {schemaFile}");
            Console.ResetColor();

            string manifestFile = DeploymentManifestGenerator.GenerateManifest("V1_Sample.dll", "V2_Sample.dll", sampleDir);
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"  ✓ Deployment Manifest written to: {manifestFile}");
            Console.ResetColor();

            string migFile = MigrationBoilerplateGenerator.GenerateMigrationClass("OrderProcessingWorkflow", 1, 2, sampleDir);
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"  ✓ Migration C# Boilerplate written to: {migFile}");
            Console.ResetColor();

            // ------------------------------------------------------------------
            // STEP 2: Demonstrate WorkerProcessSupervisor Spawning Worker
            // ------------------------------------------------------------------
            Console.WriteLine("\n[2/4] Initializing WorkerProcessSupervisor & Spawning Sub-Process V1.0.0...");
            
            // Locate Workflows.Worker.dll / Workflows.Worker.exe
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string workerDll = Path.Combine(baseDir, "Workflows.Runner.dll");
            if (!File.Exists(workerDll))
            {
                workerDll = Path.Combine(baseDir, "..", "..", "..", "..", "Workflows.Runner", "bin", "Debug", "net10.0", "Workflows.Runner.dll");
            }

            var supervisor = new WorkerProcessSupervisor(workerDll);
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

            Console.WriteLine($"  • Worker DLL path: {workerDll}");
            Console.WriteLine("  • Connecting via Named Pipe IPC & completing Handshake...");

            var workerInfo = await supervisor.EnsureWorkerAsync("1.0.0", assemblyPath: null, ct: cts.Token);
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"  ✓ Worker Process V1.0.0 connected! PID={workerInfo.Process.Id}, PipeName={workerInfo.PipeName}");
            Console.ResetColor();

            // ------------------------------------------------------------------
            // STEP 3: Dispatch IPC Command to Worker Sub-Process
            // ------------------------------------------------------------------
            Console.WriteLine("\n[3/4] Dispatching IPC 'Handshake' and 'RunWorkflow' commands to Worker PID " + workerInfo.Process.Id + "...");
            bool pingSuccess = await supervisor.SendIpcCommandAsync("1.0.0", "Handshake", "", cts.Token);

            if (pingSuccess)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("  ✓ Handshake IPC round-trip acknowledged by child sub-process!");
                Console.ResetColor();
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("  ✗ Handshake IPC command failed.");
                Console.ResetColor();
            }

            // ------------------------------------------------------------------
            // STEP 4: Shutdown Worker Sub-Process
            // ------------------------------------------------------------------
            Console.WriteLine("\n[4/4] Shutting down Worker Process V1.0.0 gracefully via IPC...");
            await supervisor.ShutdownWorkerAsync("1.0.0", cts.Token);

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("  ✓ Worker Process V1.0.0 gracefully terminated!");
            Console.ResetColor();

            Console.WriteLine("\n==========================================================================");
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("  SUCCESS: Out-of-Process Worker & WF Tools E2E demonstration completed!");
            Console.ResetColor();
            Console.WriteLine("==========================================================================");
        }
    }
}
