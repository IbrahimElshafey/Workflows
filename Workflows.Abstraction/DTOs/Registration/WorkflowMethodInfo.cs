namespace Workflows.Abstraction.DTOs.Registration
{
    public class WorkflowMethodInfo
    {
        public string MethodName { get; set; }
        public string ClassFullName { get; set; }
        public string AssemblyName { get; set; }
        public string AssemblyPath { get; set; }
        public string RunnerClass { get; set; }
    }
}