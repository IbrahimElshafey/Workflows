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
        public int Version { get; }
        public Type? StateType { get; set; }
        public string StartMethod { get; set; } = "Run";

        public WorkflowAttribute(string name, int version = 1)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException("Workflow name cannot be null or empty.", nameof(name));
            }

            Name = name;
            Version = version;
        }
    }
}
