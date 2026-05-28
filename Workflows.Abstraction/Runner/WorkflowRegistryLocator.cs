using System.Threading;

namespace Workflows.Abstraction.Runner
{
    public static class WorkflowRegistryLocator
    {
        private static readonly AsyncLocal<IWorkflowRegistry?> _current = new();

        public static IWorkflowRegistry? Current
        {
            get => _current.Value;
            set => _current.Value = value;
        }
    }
}
