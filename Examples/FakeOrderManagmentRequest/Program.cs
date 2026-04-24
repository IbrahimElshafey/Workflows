using FakeOrderManagmentRequest.Services;
using Workflows.Handler.Core;
using Workflows.Handler.Helpers;
using Workflows.MvcUi;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.


builder.Services.AddWorkflowsCore(
    new SqlServerWorkflowsSettings(null, "FakeOrderManagmentRequest4")
    .SetCurrentServiceUrl("https://localhost:7003"));
builder.Services.AddControllers()
    .AddWorkflowsMvcUi();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddScoped<IOrderProcessingService, OrderProcessingService>();
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
