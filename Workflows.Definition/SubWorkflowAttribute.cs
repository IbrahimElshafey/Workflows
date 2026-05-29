using System;

namespace Workflows.Definition
{
    /// <summary>
    /// Declarative metadata marking a method inside a WorkflowContainer as a sub-workflow.
    /// Sub-workflows must be private and cannot be used outside their parent workflow container.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
    public sealed class SubWorkflowAttribute : Attribute
    {
    }
}
