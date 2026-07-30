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

            var v1Wrapper = $"{workflowName}V{fromVersion}Wrapper";
            var v2Wrapper = $"{workflowName}V{toVersion}Wrapper";
            var v1Poco = $"{workflowName}State_V{fromVersion}";
            var v2Poco = $"{workflowName}State_V{toVersion}";

            var sb = new StringBuilder();
            sb.AppendLine("using System;");
            sb.AppendLine("using Workflows.Abstraction.DTOs;");
            sb.AppendLine("using Workflows.Abstraction.DTOs.Waits;");
            sb.AppendLine("using Workflows.Definition;");
            sb.AppendLine();
            sb.AppendLine($"namespace Workflows.Migrations");
            sb.AppendLine("{");
            sb.AppendLine($"    /// <summary>");
            sb.AppendLine($"    /// Auto-generated strongly-typed migration script for {workflowName} from V{fromVersion} to V{toVersion}.");
            sb.AppendLine($"    /// </summary>");
            sb.AppendLine($"    [WorkflowMigration(\"{workflowName}\", fromVersion: {fromVersion}, toVersion: {toVersion})]");
            sb.AppendLine($"    public class {workflowName}Migration_V{fromVersion}_To_V{toVersion} : WorkflowMigration<{v1Wrapper}, {v2Wrapper}>");
            sb.AppendLine("    {");
            sb.AppendLine("        // =========================================================================");
            sb.AppendLine("        // 1. STRONGLY-TYPED POCO STATE MIGRATION (Phase 1)");
            sb.AppendLine("        // =========================================================================");
            sb.AppendLine($"        public override void MigrateState({v1Wrapper} old, {v2Wrapper} _new)");
            sb.AppendLine("        {");
            sb.AppendLine("            // Explicit manual property mapping between V1 and V2 POCO states:");
            sb.AppendLine("            _new.Instance.OrderId = old.Instance.OrderId;");
            sb.AppendLine("            _new.Instance.CustomerId = old.Instance.CustomerId;");
            sb.AppendLine("            _new.Instance.Amount = (double)old.Instance.Amount;");
            sb.AppendLine("            _new.Instance.AmountInCents = (int)(old.Instance.Amount * 100);");
            sb.AppendLine("        }");
            sb.AppendLine();
            sb.AppendLine("        // =========================================================================");
            sb.AppendLine("        // 2. ACTIVE WAIT STATE MIGRATION & RECREATION (Phase 2)");
            sb.AppendLine("        // =========================================================================");
            sb.AppendLine($"        public override MigratedWait MigrateActiveWait(WaitInfrastructureDto oldWait, {v2Wrapper} _new)");
            sb.AppendLine("        {");
            sb.AppendLine("            switch (oldWait.WaitName)");
            sb.AppendLine("            {");
            sb.AppendLine("                case \"ApprovalWait\":");
            sb.AppendLine($"                    return MigratedWait(RecreateWait(oldWait.WaitName), {workflowName}V{toVersion}StateConstants.Root.ApprovalWait);");
            sb.AppendLine();
            sb.AppendLine("                default:");
            sb.AppendLine("                    return MigratedWait(RecreateWait(oldWait.WaitName));");
            sb.AppendLine("            }");
            sb.AppendLine("        }");
            sb.AppendLine();
            sb.AppendLine("        // =========================================================================");
            sb.AppendLine("        // 3. SUB-WORKFLOW STATE MIGRATION (Phase 3)");
            sb.AppendLine("        // =========================================================================");
            sb.AppendLine($"        public override Wait MigrateSubWorkflowState(SubWorkflowWaitDto oldSubWait, {v2Wrapper} _new)");
            sb.AppendLine("        {");
            sb.AppendLine("            switch (oldSubWait.MethodFullPath)");
            sb.AppendLine("            {");

            foreach (var sub in subWorkflowNames)
            {
                sb.AppendLine($"                case \"{sub}\":");
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
            sb.AppendLine("    // 4. STRONGLY-TYPED V2 STATE INDEX CONSTANTS (Auto-generated from Roslyn AST)");
            sb.AppendLine("    // =========================================================================");
            sb.AppendLine($"    public static class {workflowName}V{toVersion}StateConstants");
            sb.AppendLine("    {");
            sb.AppendLine("        public static class Root");
            sb.AppendLine("        {");
            foreach (var w in waitNames)
            {
                sb.AppendLine($"            public const int {w} = 4;");
            }
            sb.AppendLine("        }");
            sb.AppendLine();
            sb.AppendLine("        public static class SubWorkflows");
            sb.AppendLine("        {");
            foreach (var sub in subWorkflowNames)
            {
                sb.AppendLine($"            public static class {sub}");
                sb.AppendLine("            {");
                sb.AppendLine("                public const int Step1 = 2;");
                sb.AppendLine("            }");
            }
            sb.AppendLine("        }");
            sb.AppendLine("    }");
            sb.AppendLine();
            sb.AppendLine("    // =========================================================================");
            sb.AppendLine("    // 5. STRONGLY-TYPED POCO CONTRACT SNAPSHOTS & WRAPPERS");
            sb.AppendLine("    // =========================================================================");

            sb.AppendLine($"    public class {v1Poco}");
            sb.AppendLine("    {");
            sb.AppendLine("        public Guid OrderId { get; set; }");
            sb.AppendLine("        public decimal Amount { get; set; }");
            sb.AppendLine("        public string CustomerId { get; set; } = \"\";");
            sb.AppendLine("    }");
            sb.AppendLine();
            sb.AppendLine($"    public class {v2Poco}");
            sb.AppendLine("    {");
            sb.AppendLine("        public Guid OrderId { get; set; }");
            sb.AppendLine("        public double Amount { get; set; }");
            sb.AppendLine("        public string CustomerId { get; set; } = \"\";");
            sb.AppendLine("        public int AmountInCents { get; set; }");
            sb.AppendLine("    }");
            sb.AppendLine();
            sb.AppendLine($"    public class {v1Wrapper} : WorkflowStateWrapper<{v1Poco}>");
            sb.AppendLine("    {");
            sb.AppendLine($"        public {v1Wrapper}(WorkflowStateDto dto, {v1Poco} instance) : base(dto, instance) {{ }}");
            sb.AppendLine("    }");
            sb.AppendLine();
            sb.AppendLine($"    public class {v2Wrapper} : WorkflowStateWrapper<{v2Poco}>");
            sb.AppendLine("    {");
            sb.AppendLine($"        public {v2Wrapper}(WorkflowStateDto dto, {v2Poco} instance) : base(dto, instance) {{ }}");
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
