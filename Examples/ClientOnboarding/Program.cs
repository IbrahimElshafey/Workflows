using ClientOnboarding.Services;
using Workflows.Handler.Core;
using System.Diagnostics;
using Workflows.Handler.Helpers;
using Workflows.MvcUi;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddWorkflowsCore(
    new SqlServerWorkflowsSettings()
        .SetCurrentServiceUrl("https://localhost:7262"));
builder.Services
    .AddControllers()
    .AddWorkflowsMvcUi(
);
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddScoped<IClientOnboardingService, ClientOnboardingService>();
//builder.Services.AddScoped<ClientOnboardingWorkflow>();

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
try
{
    app.Run();
}
catch (Exception ex)
{
    Debug.Write(ex);
    throw;
}
