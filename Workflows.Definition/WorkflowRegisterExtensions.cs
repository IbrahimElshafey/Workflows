using System;
using System.Linq;
using System.Reflection;
using Workflows.Definition.Registration;

namespace Workflows.Definition
{
    public static class WorkflowRegisterExtensions
    {
        public static IWorkflowBuilder RegisterFromAssemblyContaining<T>(
            this IWorkflowBuilder register)
        {
            var assembly = typeof(T).Assembly;

            // 1. Find all workflows
            var workflowTypes = assembly.GetTypes()
                .Where(t => t.IsSubclassOf(typeof(WorkflowContainer)) && !t.IsAbstract && t.IsSealed && t.DeclaringType == null);

            foreach (var type in workflowTypes)
            {
                var attribute = type.GetCustomAttribute<WorkflowAttribute>();
                if (attribute == null)
                {
                    throw new InvalidOperationException($"Workflow '{type.Name}' in assembly '{assembly.FullName}' is missing [WorkflowAttribute]. Automatically registering workflows without a version requires all workflows to have the attribute.");
                }

                var method = typeof(IWorkflowBuilder)
                    .GetMethods()
                    .First(m => m.Name == nameof(IWorkflowBuilder.RegisterWorkflow) && m.GetGenericArguments().Length == 1 && m.GetParameters().Length == 0)
                    .MakeGenericMethod(type);

                method.Invoke(register, null);
            }

            return register;
        }

        public static IWorkflowBuilder RegisterFromAssemblyContaining<T>(
            this IWorkflowBuilder register,
            int version)
        {
            var assembly = typeof(T).Assembly;

            // 1. Find all workflows
            var workflowTypes = assembly.GetTypes()
                .Where(t => t.IsSubclassOf(typeof(WorkflowContainer)) && !t.IsAbstract && t.IsSealed && t.DeclaringType == null);

            foreach (var type in workflowTypes)
            {
                var attribute = type.GetCustomAttribute<WorkflowAttribute>();
                if (attribute == null)
                {
                    throw new InvalidOperationException($"Workflow '{type.Name}' in assembly '{assembly.FullName}' is missing [WorkflowAttribute]. All workflows must be decorated with [WorkflowAttribute].");
                }
                string name = attribute.Name;
                int v = version;

                // Use reflection to call the generic RegisterWorkflow<T>(string name, int version) method
                var method = typeof(IWorkflowBuilder)
                    .GetMethods()
                    .First(m => m.Name == nameof(IWorkflowBuilder.RegisterWorkflow) && m.GetGenericArguments().Length == 1 && m.GetParameters().Length == 2)
                    .MakeGenericMethod(type);

                method.Invoke(register, new object[] { name, v });
            }

            return register;
        }
    }
}