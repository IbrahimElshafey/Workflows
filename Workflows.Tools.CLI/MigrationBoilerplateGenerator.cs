using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Workflows.Tools.CLI
{
    public static class MigrationBoilerplateGenerator
    {
        public static string GenerateMigrationClass(
            string workflowName,
            int fromVersion,
            int toVersion,
            string outputDir,
            List<string>? waitNames = null,
            List<string>? subWorkflowNames = null)
        {
            waitNames ??= new List<string> { "ApprovalWait", "PaymentCallbackWait", "CustomerNotificationWait" };
            subWorkflowNames ??= new List<string> { "PaymentProcessorSubWorkflow", "InventoryAllocationSubWorkflow" };

            var sb = new StringBuilder();
            sb.AppendLine("using System;");
            sb.AppendLine("using Workflows.Abstraction.DTOs.Waits;");
            sb.AppendLine("using Workflows.Definition;");
            sb.AppendLine("using Workflows.Runner.Migration;");
            sb.AppendLine();
            sb.AppendLine($"namespace Workflows.Migrations");
            sb.AppendLine("{");
            sb.AppendLine($"    /// <summary>");
            sb.AppendLine($"    /// Auto-generated migration script for {workflowName} from V{fromVersion} to V{toVersion}.");
            sb.AppendLine($"    /// </summary>");
            sb.AppendLine($"    [WorkflowMigration(\"{workflowName}\", fromVersion: {fromVersion}, toVersion: {toVersion})]");
            sb.AppendLine($"    public class {workflowName}Migration_V{fromVersion}_To_V{toVersion} : WorkflowMigration");
            sb.AppendLine("    {");
            sb.AppendLine("        // =========================================================================");
            sb.AppendLine("        // 1. STATE MIGRATION (Phase 1)");
            sb.AppendLine("        // =========================================================================");
            sb.AppendLine("        /// <summary>");
            sb.AppendLine("        /// Migrates top-level workflow state properties from V1 to V2.");
            sb.AppendLine("        /// </summary>");
            sb.AppendLine("        public override void MigrateState(dynamic old, dynamic _new)");
            sb.AppendLine("        {");
            sb.AppendLine("            // Fail-Loud AutoMapFrom Scaffolding:");
            sb.AppendLine("            // Copies matching properties automatically between V1 and V2 state.");
            sb.AppendLine("            _new.AutoMapFrom(old);");
            sb.AppendLine();
            sb.AppendLine("            // Example custom property transformations:");
            sb.AppendLine("            // _new.State.AmountInCents = (int)(old.State.Amount * 100);");
            sb.AppendLine("            // _new.State.ShippingAddress = old.State.Address;");
            sb.AppendLine("        }");
            sb.AppendLine();
            sb.AppendLine("        // =========================================================================");
            sb.AppendLine("        // 2. ACTIVE WAIT STATE MIGRATION & RECREATION (Phase 2)");
            sb.AppendLine("        // =========================================================================");
            sb.AppendLine("        /// <summary>");
            sb.AppendLine("        /// Recreates active suspended waits for in-flight instances.");
            sb.AppendLine("        /// Switch based on oldWait.WaitName to customize payload or wait types.");
            sb.AppendLine("        /// </summary>");
            sb.AppendLine("        public override Wait MigrateActiveWait(WaitInfrastructureDto oldWait, dynamic _new)");
            sb.AppendLine("        {");
            sb.AppendLine("            switch (oldWait.WaitName)");
            sb.AppendLine("            {");

            foreach (var wait in waitNames)
            {
                sb.AppendLine($"                case \"{wait}\":");
                sb.AppendLine($"                    // Custom wait payload or signal identifier remapping:");
                sb.AppendLine($"                    // return WaitSignal<Updated{wait}Payload>(\"{wait}Signal\", oldWait.WaitName).Build();");
                sb.AppendLine($"                    return RecreateWait(oldWait.WaitName);");
                sb.AppendLine();
            }

            sb.AppendLine("                default:");
            sb.AppendLine("                    // Default auto-recreation of wait by name using V2's compiled CFG manifest.");
            sb.AppendLine("                    return RecreateWait(oldWait.WaitName);");
            sb.AppendLine("            }");
            sb.AppendLine("        }");
            sb.AppendLine();
            sb.AppendLine("        // =========================================================================");
            sb.AppendLine("        // 3. SUB-WORKFLOW STATE MIGRATION (Phase 3)");
            sb.AppendLine("        // =========================================================================");
            sb.AppendLine("        /// <summary>");
            sb.AppendLine("        /// Remaps child sub-workflow state pointers and frozen state indices.");
            sb.AppendLine("        /// </summary>");
            sb.AppendLine("        public override Wait MigrateSubWorkflowState(SubWorkflowWaitDto oldSubWait, dynamic _new)");
            sb.AppendLine("        {");
            sb.AppendLine("            switch (oldSubWait.MethodFullPath)");
            sb.AppendLine("            {");

            foreach (var sub in subWorkflowNames)
            {
                sb.AppendLine($"                case \"{sub}\":");
                sb.AppendLine($"                    // Recreates sub-workflow wait and remaps child CFG pointers:");
                sb.AppendLine($"                    return SubWorkflow_RecreateWait(oldSubWait.MethodFullPath, oldSubWait.WaitName);");
                sb.AppendLine();
            }

            sb.AppendLine("                default:");
            sb.AppendLine("                    return SubWorkflow_RecreateWait(oldSubWait.MethodFullPath, oldSubWait.WaitName);");
            sb.AppendLine("            }");
            sb.AppendLine("        }");
            sb.AppendLine("    }");
            sb.AppendLine();
            sb.AppendLine("    // =========================================================================");
            sb.AppendLine("    // 4. STRONGLY-TYPED POCO CONTRACT SNAPSHOTS");
            sb.AppendLine("    // =========================================================================");
            sb.AppendLine($"    public class {workflowName}State_V{fromVersion}");
            sb.AppendLine("    {");
            sb.AppendLine("        public Guid OrderId { get; set; }");
            sb.AppendLine("        public decimal Amount { get; set; }");
            sb.AppendLine("        public string CustomerId { get; set; } = \"\";");
            sb.AppendLine("    }");
            sb.AppendLine();
            sb.AppendLine($"    public class {workflowName}State_V{toVersion}");
            sb.AppendLine("    {");
            sb.AppendLine("        public Guid OrderId { get; set; }");
            sb.AppendLine("        public double Amount { get; set; }");
            sb.AppendLine("        public string CustomerId { get; set; } = \"\";");
            sb.AppendLine("        public int AmountInCents { get; set; }");
            sb.AppendLine("    }");

            foreach (var sub in subWorkflowNames)
            {
                sb.AppendLine();
                sb.AppendLine($"    public class {sub}State_V{fromVersion}");
                sb.AppendLine("    {");
                sb.AppendLine("        public string TransactionId { get; set; } = \"\";");
                sb.AppendLine("        public string Status { get; set; } = \"\";");
                sb.AppendLine("    }");
                sb.AppendLine();
                sb.AppendLine($"    public class {sub}State_V{toVersion}");
                sb.AppendLine("    {");
                sb.AppendLine("        public string TransactionId { get; set; } = \"\";");
                sb.AppendLine("        public string Status { get; set; } = \"\";");
                sb.AppendLine("        public DateTime ProcessedUtc { get; set; }");
                sb.AppendLine("    }");
            }

            sb.AppendLine("}");

            Directory.CreateDirectory(outputDir);
            string filePath = Path.Combine(outputDir, $"{workflowName}Migration_V{fromVersion}_To_V{toVersion}.cs");
            File.WriteAllText(filePath, sb.ToString());
            return filePath;
        }
    }
}
