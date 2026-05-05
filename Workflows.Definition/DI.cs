using Microsoft.Extensions.DependencyInjection;
using System;

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
