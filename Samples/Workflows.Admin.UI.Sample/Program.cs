using Workflows.Admin.UI.Extensions;
using Workflows.Hosting.InProcess;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("Workflows")
    ?? "Data Source=workflows-admin-sample.db";

// Register the workflow engine (orchestrator + in-process hosting)
builder.Services.AddWorkflowsInProcessHost(connectionString);

// Register the admin UI module (read-only by default; write actions enabled because IOrchestrator is present)
builder.Services.AddWorkflowsAdminUI(connectionString, options =>
{
    options.RoutePrefix = "admin";
    options.Provider = Workflows.Admin.UI.AdminDbProvider.Sqlite;
});

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();

app.UseWorkflowsAdminUI();

app.MapGet("/", () => Results.Redirect("/admin/Dashboard"));

app.Run();
