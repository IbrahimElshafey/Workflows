using System;
using Workflows.Definition;

namespace Workflows.Runner.Pipeline
{
    /// <summary>
    /// Responsible for instantiating workflow containers and compiling
    /// cached invoker delegates for workflow methods.
    /// Separates reflection/cache concerns from execution context management.
    /// </summary>
    internal interface IWorkflowHydrator
    {
        /// <summary>
        /// Creates a new instance of the specified workflow container type
        /// using the DI service provider.
        /// </summary>
        WorkflowContainer CreateInstance(Type containerType);

        /// <summary>
        /// Returns a compiled, cached delegate that invokes <paramref name="methodName"/>
        /// on an instance of <paramref name="containerType"/>.
        /// </summary>
        /// <exception cref="InvalidOperationException">
        /// Thrown when <paramref name="methodName"/> is not found on <paramref name="containerType"/>.
        /// </exception>
        Func<object, object> GetInvoker(Type containerType, string methodName);
    }
}
