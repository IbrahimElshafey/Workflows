using System;
using System.IO;

namespace Workflows.Tools.CLI
{
    public class Program
    {
        public static int Main(string[] args)
        {
            if (args.Length == 0)
            {
                PrintHelp();
                return 0;
            }

            string command = args[0].ToLowerInvariant();
            try
            {
                switch (command)
                {
                    case "schema":
                        string asm = GetArg(args, "--assembly") ?? "./bin/Workflows.dll";
                        string outDir = GetArg(args, "--out") ?? "./_Schemas";
                        string schemaPath = WorkflowSchemaGenerator.GenerateSchemaJson(asm, outDir);
                        Console.WriteLine($"[WF Tools] Schema JSON generated: {schemaPath}");
                        return 0;

                    case "migrate":
                        string wfName = GetArg(args, "--workflow") ?? "OrderWorkflow";
                        int fromVer = int.Parse(GetArg(args, "--from") ?? "1");
                        int toVer = int.Parse(GetArg(args, "--to") ?? "2");
                        string migDir = GetArg(args, "--out") ?? "./Migrations";
                        string migPath = MigrationBoilerplateGenerator.GenerateMigrationClass(wfName, fromVer, toVer, migDir);
                        Console.WriteLine($"[WF Tools] Migration class boilerplate generated: {migPath}");
                        return 0;

                    case "compare":
                    case "verify":
                        string oldAsm = GetArg(args, "--old") ?? "./bin/V1/Workflows.dll";
                        string newAsm = GetArg(args, "--new") ?? "./bin/V2/Workflows.dll";
                        string manifestDir = GetArg(args, "--out") ?? "./";

                        var report = WorkflowDiffAnalyzer.CompareAssemblies(oldAsm, newAsm);
                        string manifestPath = DeploymentManifestGenerator.GenerateManifest(oldAsm, newAsm, manifestDir);

                        Console.WriteLine($"[WF Tools] Comparison complete across {report.Items.Count} workflow(s).");
                        foreach (var item in report.Items)
                        {
                            Console.WriteLine($"  • {item.WorkflowName}: Status={item.Status}, Strategy={item.Strategy}");
                            foreach (var diff in item.Differences)
                            {
                                Console.WriteLine($"    ⚠️ {diff}");
                            }
                        }
                        Console.WriteLine($"[WF Tools] Deployment manifest generated: {manifestPath}");

                        if (command == "verify" && report.HasBreakingChanges)
                        {
                            Console.WriteLine("[WF Tools Alert] Breaking state changes detected. Failing build verification.");
                            return 1;
                        }

                        return 0;

                    default:
                        PrintHelp();
                        return 1;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[WF Tools Error] {ex.Message}");
                return 1;
            }
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

        private static void PrintHelp()
        {
            Console.WriteLine("================================================================================");
            Console.WriteLine("🧙‍♂️ Workflows Tools (dotnet-wf) CLI Suite");
            Console.WriteLine("================================================================================");
            Console.WriteLine("Commands:");
            Console.WriteLine("  verify   --old <path> --new <path>  Run offline Roslyn AST schema verification");
            Console.WriteLine("  schema   --assembly <path> --out <dir>  Extract and emit versioned JSON schema");
            Console.WriteLine("  migrate  --workflow <name> --from <v1> --to <v2> --out <dir>  Generate C# migration boilerplate");
            Console.WriteLine("  compare  --old <path> --new <path> --out <dir>  Run pre-publish comparison & generate deployment-manifest.json");
            Console.WriteLine("================================================================================");
        }
    }
}
