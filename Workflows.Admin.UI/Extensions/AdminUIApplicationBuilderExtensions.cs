using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Workflows.Admin.UI.Extensions
{
    public static class AdminUIApplicationBuilderExtensions
    {
        /// <summary>
        /// Configures the ASP.NET Core pipeline to serve the Workflows Admin UI.
        /// </summary>
        public static IApplicationBuilder UseWorkflowsAdminUI(this IApplicationBuilder app)
        {
            if (app == null) throw new ArgumentNullException(nameof(app));

            var options = app.ApplicationServices.GetRequiredService<IOptions<WorkflowsAdminUIOptions>>().Value;
            var prefix = options.RoutePrefix.Trim('/');

            app.UseStaticFiles();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllerRoute(
                    name: "workflows_admin_default",
                    pattern: prefix + "/{controller=Dashboard}/{action=Index}/{id?}",
                    defaults: new { area = "WorkflowsAdmin" });
            });

            return app;
        }
    }
}