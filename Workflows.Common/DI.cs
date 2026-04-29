using Microsoft.Extensions.DependencyInjection;
using Workflows.Abstraction.Common;

namespace Workflows.Common
{
    public static class  DI
    {
        public static IServiceCollection AddWorkflowsCommon(this IServiceCollection services)
        {
            return services.AddSingleton<IObjectSerializer, JsonSerializer>();
        }
    }
}
