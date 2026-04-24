using Workflows.Handler.Core;
using Workflows.Handler.Helpers;
using Workflows.MvcUi;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddWorkflowsCore(
    new SqlServerWorkflowsSettings()
        .SetCurrentServiceUrl("https://localhost:7099/")
        .SetDllsToScan("ReferenceLibrary"));
builder.Services
    .AddControllers()
    .AddWorkflowsMvcUi(
);

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

