using Workflows.Handler.Core;
using Workflows.Handler.Helpers;
using Workflows.MvcUi;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
var settings =
    new SqlServerWorkflowsSettings(null, "CallerSameNameGroup_Test")
    .SetCurrentServiceUrl("https://localhost:7220/");
//settings.CleanDbSettings.CompletedInstanceRetention = TimeSpan.FromSeconds(3);
//settings.CleanDbSettings.DeactivatedWaitTemplateRetention = TimeSpan.FromSeconds(3);
//settings.CleanDbSettings.SignalRetention = TimeSpan.FromSeconds(3);
builder.Services.AddWorkflowsCore(settings);
builder.Services
    .AddControllers()
    .AddWorkflowsMvcUi();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();
app.Services.UseWorkflows();
app.UseWorkflowsUi();
// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();
