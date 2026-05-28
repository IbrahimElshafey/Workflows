using System;

namespace Workflows.Definition
{
    /// <summary>
    /// Declarative metadata for a workflow container.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
    public sealed class WorkflowAttribute : Attribute
    {
        public string Name { get; }
        public string Version { get; }

        public WorkflowAttribute(string name, string version = "1.0")
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException("Workflow name cannot be null or empty.", nameof(name));
            }

            Name = name;
            Version = version ?? "1.0";
        }
    }
}
