using Hangfire;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Workflows.Handler.Core.Abstraction;
using Workflows.Handler.Helpers;
namespace Workflows.MvcUi
{
    public static class Extensions
    {
        public static IMvcBuilder AddWorkflowsMvcUi(this IMvcBuilder mvcBuilder)
        {
            mvcBuilder
                .AddApplicationPart(typeof(WorkflowsController).Assembly)
                .AddControllersAsServices();
            mvcBuilder.Services.AddRazorPages();
            return mvcBuilder;
        }


        public static void UseWorkflowsUi(this WebApplication app)
        {
            app.UseHangfireDashboard();
            app.MapRazorPages();
            app.UseStaticFiles();


            app.UseRouting();

            app.MapControllerRoute(
                name: "MyArea",
                pattern: "{area:exists}/{controller=Home}/{action=Index}/{id?}");

            app.MapControllerRoute(
                name: "default",
                pattern: "{controller=Home}/{action=Index}/{id?}");

        }
    }
}