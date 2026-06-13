using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;
using FluentAssertions;
using Grpc.Net.Client;
using Workflows.Abstraction.DTOs;
using Workflows.Abstraction.DTOs.Waits;
using Workflows.Abstraction.DTOs.Registration;
using Workflows.Abstraction.Enums;
using Workflows.Abstraction.Helpers;
using Workflows.Abstraction.Orchestrator;
using Workflows.Abstraction.Persistence;
using Workflows.Abstraction.Runner;
using Workflows.Client;
using Workflows.Client.WebApi;
using Workflows.Client.gRPC;
using Workflows.Communication.Abstraction;
using Workflows.Definition;
using Workflows.Hosting.InProcess;
using Workflows.Runner.Tests.Infrastructure;
using Workflows.Shared;
using Workflows.Primitives;
using Microsoft.Data.Sqlite;
using Workflows.Storage.EntityFrameworkCore;


namespace Workflows.Runner.Tests
{
    public class TestCommand 
    { 
        public string Message { get; set; } = string.Empty;
    }

    public class TestResult 
    { 
        public string Response { get; set; } = string.Empty;
    }

    public class TestExecutor : ICommandExecutor<TestCommand, TestResult>
    {
        public Task<TestResult> ExecuteAsync(TestCommand command, CancellationToken cancellationToken)
        {
            return Task.FromResult(new TestResult { Response = command.Message + "-echo" });
        }
    }

    [Workflow("ClientIntegrationWorkflow", 1)]
    public sealed class ClientIntegrationWorkflow : WorkflowContainer
    {
        public string CommandResponse { get; set; } = string.Empty;

        public async IAsyncEnumerable<Wait> Run()
        {
            yield return WaitGroup(new Wait[]
            {
                ExecuteCommand<TestCommand, TestResult>(
                    "test-handler",
                    new TestCommand { Message = "hello" }
                )
                .WithExecutionMode(CommandExecutionMode.Deferred)
                .OnResult(result =>
                {
                    CommandResponse = result.Response;
                }),
                WaitSignal<string>("DummyStartSignal", "Dummy")
            }, "FirstGroup").MatchAny();

            yield return WaitSignal<string>("Finished", "Finish signal")
                .MatchIf(msg => msg == CommandResponse);

            await Task.CompletedTask;
        }
    }

    public class ClientIntegrationTests
    {

        [Fact]
        public async Task Test_Web_API_Client_Server_Integration_Loop()
        {
            // --- 1. SET UP DATABASE ---
            var connectionString = "Data Source=InMemoryWebApiTest;Mode=Memory;Cache=Shared";
            using var dbConnection = new SqliteConnection(connectionString);
            await dbConnection.OpenAsync();

            // --- 2. SET UP TESTSERVERS ---
            TestServer? serverHost = null;
            TestServer? clientHost = null;

            // Define the server TestServer
            var serverBuilder = new WebHostBuilder()
                .ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddWorkflowsShared();
                    services.AddWorkflowsRunner();
                    services.AddSingleton<Newtonsoft.Json.Schema.Generation.JSchemaGenerator, MockSchemaGenerator>();
                    services.AddSingleton<ICommandHandlerFactory, InMemoryCommandHandlerFactory>();
                    
                    // Add orchestrator services in memory
                    services.AddWorkflowsInProcessHost(connectionString);
                    services.AddWorkflowsHttpTransport();

                    // Register custom transport that sends dispatch requests to the client host
                    services.AddTransient<HttpWorkflowMessageTransport>(sp =>
                    {
                        var httpClient = clientHost!.CreateClient();
                        return new HttpWorkflowMessageTransport(httpClient, sp.GetRequiredService<IObjectSerializer>());
                    });

                    // Define routing: server dispatches command wait to the client
                    var routing = new TransportRoutingBuilder();
                    routing.UseDefault<InProcessMessageTransport, InProcessMessageSubscriber>();
                    routing.ForMessage<StartWorkflowRequest>()
                        .Use<InProcessMessageTransport, InProcessMessageSubscriber>("in-process-runner");
                    routing.ForMessage<WorkflowExecutionRequest>()
                        .Use<InProcessMessageTransport, InProcessMessageSubscriber>("in-process-runner");
                    routing.ForMessage<CommandDispatchNotification>()
                        .Use<HttpWorkflowMessageTransport, HttpWorkflowMessageSubscriber>("http://localhost:6000/api/workflows/command-dispatch");
                    services.AddSingleton(routing);
                })
                .Configure(app =>
                {
                    using (var scope = app.ApplicationServices.CreateScope())
                    {
                        var context = scope.ServiceProvider.GetRequiredService<WorkflowsDbContext>();
                        context.Database.EnsureCreated();
                    }
                    app.UseRouting();
                    app.UseEndpoints(endpoints => endpoints.MapWorkflowOrchestratorEndpoints());
                });

            serverHost = new TestServer(serverBuilder);



            // Define the client TestServer
            var clientBuilder = new WebHostBuilder()
                .ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddWorkflowsShared();
                    services.AddWorkflowsClient()
                        .AddCommandExecutor<TestCommand, TestResult, TestExecutor>("test-handler");
                    services.AddWorkflowsHttpTransport();

                    // Register custom transport that sends signal/command results to the server host
                    services.AddTransient<HttpWorkflowMessageTransport>(sp =>
                    {
                        var httpClient = serverHost.CreateClient();
                        return new HttpWorkflowMessageTransport(httpClient, sp.GetRequiredService<IObjectSerializer>());
                    });

                    // Define routing: client dispatches signals and results back to the server
                    var routing = new TransportRoutingBuilder();
                    routing.ForMessage<SignalDto>()
                        .Use<HttpWorkflowMessageTransport, HttpWorkflowMessageSubscriber>("http://localhost:5000/api/workflows/signal");
                    routing.ForMessage<CommandResultDto>()
                        .Use<HttpWorkflowMessageTransport, HttpWorkflowMessageSubscriber>("http://localhost:5000/api/workflows/command-result");
                    services.AddSingleton(routing);
                })
                .Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(endpoints => endpoints.MapWorkflowClientEndpoints());
                });

            clientHost = new TestServer(clientBuilder);

            // --- 2. REGISTER WORKFLOW ---
            using (var scope = serverHost.Services.CreateScope())
            {
                var definitionRepo = scope.ServiceProvider.GetRequiredService<IDefinitionRepository>();
                var builder = (WorkflowBuilder)scope.ServiceProvider.GetRequiredService<IWorkflowRegistry>();
                
                builder.RegisterWorkflow<ClientIntegrationWorkflow>("ClientIntegrationWorkflow", 1);
                builder.RegisterCommand<TestCommand, TestResult>("test-handler", default!, CommandExecutionMode.Deferred);

                // Extract package using reflection
                var packageField = typeof(WorkflowBuilder).GetField("registrationPackage", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                var package = (BulkRegistrationPackage)packageField!.GetValue(builder)!;

                var syncResult = await definitionRepo.SyncDefinitionsAsync(package);
                if (!syncResult.Success)
                {
                    var errorMsgs = new List<string>();
                    foreach (var err in syncResult.Errors)
                    {
                        errorMsgs.Add($"{err.EntityName} ({err.ErrorType}): {err.Message}");
                    }
                    throw new InvalidOperationException($"Sync failed: {string.Join(", ", errorMsgs)}");
                }
            }



            // --- 3. EXECUTE INTEGRATION WORKFLOW ---
            Guid workflowId;
            using (var scope = serverHost.Services.CreateScope())
            {
                var orchestrator = scope.ServiceProvider.GetRequiredService<IOrchestrator>();
                workflowId = await orchestrator.StartWorkflowAsync("ClientIntegrationWorkflow", 1, new object());
            }

            // Give background executor thread time to execute the command loop
            WorkflowStateDto? state = null;
            for (int i = 0; i < 100; i++)
            {
                using (var scope = serverHost.Services.CreateScope())
                {
                    var workflowStore = scope.ServiceProvider.GetRequiredService<IWorkflowStore>();
                    state = await workflowStore.GetInstanceStateAsync(workflowId);
                }

                if (state != null && state.Waits.Any(w => w.WaitType == WaitType.SignalWait && w.Status == WaitStatus.Waiting))
                {
                    break;
                }
                await Task.Delay(100);
            }
            if (state != null)
            {
                var waitsList = System.Linq.Enumerable.ToList(System.Linq.Enumerable.Select(state.Waits, w => new { w.WaitType, w.Status, w.WaitName, ChildrenCount = w.ChildWaits?.Count ?? 0 }));
                Console.WriteLine($"[TEST DEBUG] Waits in state: {string.Join(", ", waitsList)}");
                var childWaits = System.Linq.Enumerable.ToList(System.Linq.Enumerable.SelectMany(System.Linq.Enumerable.Where(state.Waits, w => w.ChildWaits != null), w => w.ChildWaits).Select(cw => new { cw.WaitType, cw.Status, cw.WaitName }));
                Console.WriteLine($"[TEST DEBUG] Child waits: {string.Join(", ", childWaits)}");
            }
            using (var scope = serverHost.Services.CreateScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<WorkflowsDbContext>();
                var outboxCount = System.Linq.Queryable.Count(context.OutboxMessages);
                var outboxList = System.Linq.Enumerable.ToList(System.Linq.Queryable.Select(context.OutboxMessages, m => new { m.Status, m.CommandWaitId, m.GlobalId }));
                var inboxCount = System.Linq.Queryable.Count(context.CommandResults);
                var inboxList = System.Linq.Enumerable.ToList(System.Linq.Queryable.Select(context.CommandResults, r => new { r.Status, r.CommandWaitId, r.IsSuccess }));
                Console.WriteLine($"[TEST DEBUG] Outbox count: {outboxCount}, Outbox: {string.Join(", ", outboxList)}");
                Console.WriteLine($"[TEST DEBUG] Inbox count: {inboxCount}, Inbox: {string.Join(", ", inboxList)}");
            }

            // Verify that the command executed and result was posted back
            state.Should().NotBeNull();
            // Since the command completed and returned "hello-echo", it should now be waiting for the Finished signal with that value
            state!.Waits.Should().ContainSingle(w => w.WaitType == WaitType.SignalWait && w.Status == WaitStatus.Waiting);
            var signalWait = (SignalWaitDto)state.Waits.First(w => w.WaitType == WaitType.SignalWait && w.Status == WaitStatus.Waiting);
            signalWait.SignalIdentifier.Should().Be("Finished");

            clientHost.Dispose();
            serverHost.Dispose();
        }

        [Fact]
        public async Task Test_gRPC_Client_Server_Integration_Loop()
        {
            // --- 1. SET UP DATABASE ---
            var connectionString = "Data Source=InMemoryGrpcTest;Mode=Memory;Cache=Shared";
            using var dbConnection = new SqliteConnection(connectionString);
            await dbConnection.OpenAsync();

            // --- 2. SET UP TESTSERVERS ---
            TestServer? serverHost = null;
            TestServer? clientHost = null;

            // Define the server TestServer
            var serverBuilder = new WebHostBuilder()
                .ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddGrpc();
                    services.AddWorkflowsShared();
                    services.AddWorkflowsRunner();
                    services.AddSingleton<Newtonsoft.Json.Schema.Generation.JSchemaGenerator, MockSchemaGenerator>();
                    services.AddSingleton<ICommandHandlerFactory, InMemoryCommandHandlerFactory>();
                    
                    // Add orchestrator services in memory
                    services.AddWorkflowsInProcessHost(connectionString);
                    services.AddWorkflowsGrpcTransport();

                    // Register custom gRPC service
                    services.AddScoped<GrpcWorkflowOrchestratorService>();

                    // Register custom transport that sends dispatch requests to the client host
                    services.AddTransient<GrpcWorkflowMessageTransport>(sp =>
                    {
                        var handler = clientHost!.CreateHandler();
                        var channel = GrpcChannel.ForAddress("http://localhost", new GrpcChannelOptions { HttpHandler = handler });
                        // Inject channel directly or via custom factory (we use a subclass/override approach)
                        return new CustomGrpcMessageTransport(channel, sp.GetRequiredService<IObjectSerializer>());
                    });

                    // Define routing: server dispatches command wait to the client
                    var routing = new TransportRoutingBuilder();
                    routing.UseDefault<InProcessMessageTransport, InProcessMessageSubscriber>();
                    routing.ForMessage<StartWorkflowRequest>()
                        .Use<InProcessMessageTransport, InProcessMessageSubscriber>("in-process-runner");
                    routing.ForMessage<WorkflowExecutionRequest>()
                        .Use<InProcessMessageTransport, InProcessMessageSubscriber>("in-process-runner");
                    routing.ForMessage<CommandDispatchNotification>()
                        .Use<GrpcWorkflowMessageTransport, GrpcWorkflowMessageSubscriber>("http://localhost:6001");
                    services.AddSingleton(routing);
                })
                .Configure(app =>
                {
                    using (var scope = app.ApplicationServices.CreateScope())
                    {
                        var context = scope.ServiceProvider.GetRequiredService<WorkflowsDbContext>();
                        context.Database.EnsureCreated();
                    }
                    app.UseRouting();
                    app.UseEndpoints(endpoints => endpoints.MapWorkflowGrpcServices());
                });

            serverHost = new TestServer(serverBuilder);



            // Define the client TestServer
            var clientBuilder = new WebHostBuilder()
                .ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddGrpc();
                    services.AddWorkflowsShared();
                    services.AddWorkflowsClient()
                        .AddCommandExecutor<TestCommand, TestResult, TestExecutor>("test-handler");
                    services.AddWorkflowsGrpcTransport();

                    // Register custom gRPC service
                    services.AddScoped<GrpcWorkflowExecutorService>();

                    // Register custom transport that sends signal/command results to the server host
                    services.AddTransient<GrpcWorkflowMessageTransport>(sp =>
                    {
                        var handler = serverHost.CreateHandler();
                        var channel = GrpcChannel.ForAddress("http://localhost", new GrpcChannelOptions { HttpHandler = handler });
                        return new CustomGrpcMessageTransport(channel, sp.GetRequiredService<IObjectSerializer>());
                    });

                    // Define routing: client dispatches signals and results back to the server
                    var routing = new TransportRoutingBuilder();
                    routing.ForMessage<SignalDto>()
                        .Use<GrpcWorkflowMessageTransport, GrpcWorkflowMessageSubscriber>("http://localhost:5001");
                    routing.ForMessage<CommandResultDto>()
                        .Use<GrpcWorkflowMessageTransport, GrpcWorkflowMessageSubscriber>("http://localhost:5001");
                    services.AddSingleton(routing);
                })
                .Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(endpoints => endpoints.MapWorkflowGrpcServices());
                });

            clientHost = new TestServer(clientBuilder);

            // --- 2. REGISTER WORKFLOW ---
            using (var scope = serverHost.Services.CreateScope())
            {
                var definitionRepo = scope.ServiceProvider.GetRequiredService<IDefinitionRepository>();
                var builder = (WorkflowBuilder)scope.ServiceProvider.GetRequiredService<IWorkflowRegistry>();
                
                builder.RegisterWorkflow<ClientIntegrationWorkflow>("ClientIntegrationWorkflow", 1);
                builder.RegisterCommand<TestCommand, TestResult>("test-handler", default!, CommandExecutionMode.Deferred);

                // Extract package using reflection
                var packageField = typeof(WorkflowBuilder).GetField("registrationPackage", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                var package = (BulkRegistrationPackage)packageField!.GetValue(builder)!;

                var syncResult = await definitionRepo.SyncDefinitionsAsync(package);
                if (!syncResult.Success)
                {
                    var errorMsgs = new List<string>();
                    foreach (var err in syncResult.Errors)
                    {
                        errorMsgs.Add($"{err.EntityName} ({err.ErrorType}): {err.Message}");
                    }
                    throw new InvalidOperationException($"Sync failed: {string.Join(", ", errorMsgs)}");
                }
            }



            // --- 3. EXECUTE INTEGRATION WORKFLOW ---
            Guid workflowId;
            using (var scope = serverHost.Services.CreateScope())
            {
                var orchestrator = scope.ServiceProvider.GetRequiredService<IOrchestrator>();
                workflowId = await orchestrator.StartWorkflowAsync("ClientIntegrationWorkflow", 1, new object());
            }

            // Give background executor thread time to execute the command loop
            WorkflowStateDto? state = null;
            for (int i = 0; i < 100; i++)
            {
                using (var scope = serverHost.Services.CreateScope())
                {
                    var workflowStore = scope.ServiceProvider.GetRequiredService<IWorkflowStore>();
                    state = await workflowStore.GetInstanceStateAsync(workflowId);
                }

                if (state != null && state.Waits.Any(w => w.WaitType == WaitType.SignalWait && w.Status == WaitStatus.Waiting))
                {
                    break;
                }
                await Task.Delay(100);
            }

            // Verify that the command executed and result was posted back
            state.Should().NotBeNull();
            // Since the gRPC executor completed and returned "hello-echo", it should now be waiting for the Finished signal
            state!.Waits.Should().ContainSingle(w => w.WaitType == WaitType.SignalWait && w.Status == WaitStatus.Waiting);
            var signalWait = (SignalWaitDto)state.Waits.First(w => w.WaitType == WaitType.SignalWait && w.Status == WaitStatus.Waiting);
            signalWait.SignalIdentifier.Should().Be("Finished");

            clientHost.Dispose();
            serverHost.Dispose();
        }

        /// <summary>
        /// A custom implementation of GrpcWorkflowMessageTransport that uses a fixed in-memory GrpcChannel
        /// to redirect requests directly to an in-memory TestServer.
        /// </summary>
        private class CustomGrpcMessageTransport : GrpcWorkflowMessageTransport
        {
            private readonly GrpcChannel _channel;

            public CustomGrpcMessageTransport(GrpcChannel channel, IObjectSerializer serializer) 
                : base(serializer)
            {
                _channel = channel;
            }

            // Override GetChannel to bypass dictionary and return the fixed test channel
            protected override GrpcChannel GetChannel(string destination) => _channel;
        }
    }
}

