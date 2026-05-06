using Microsoft.Extensions.DependencyInjection;
using System;
using Workflows.Definition.Registration;

namespace Workflows.Definition
{
    public static class DI
    {
        public static IServiceCollection RegisterWorkflows(this IServiceCollection services, Action<IWorkflowBuilder> register)
        {
            throw new NotImplementedException();
        }
    }
}
