using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using FluentAssertions;
using Newtonsoft.Json.Schema.Generation;
using Xunit;
using Workflows.Abstraction.DTOs;
using Workflows.Abstraction.DTOs.Registration;
using Workflows.Abstraction.DTOs.Waits;
using Workflows.Abstraction.Enums;
using Workflows.Abstraction.Helpers;
using Workflows.Abstraction.Orchestrator;
using Workflows.Abstraction.Persistence;
using Workflows.Abstraction.Runner;
using Workflows.Definition;
using Workflows.Definition.Registration;
using Workflows.Hosting.InProcess;
using Workflows.Orchestrator;
using Workflows.Storage.EntityFrameworkCore;
using Workflows.Primitives;
using Workflows.Runner;
using Workflows.Runner.Tests.TestData;
using Workflows.Shared;

namespace Workflows.Runner.Tests.ResumptionTests
{
    public class ResumptionOrderReceivedSignal
    {
        public string OrderId { get; set; } = string.Empty;
        public decimal Amount { get; set; }
    }

    public class ResumptionStockConfirmedSignal
    {
        public string OrderId { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
    }

    public class ResumptionCustomerVerifiedSignal
    {
        public string OrderId { get; set; } = string.Empty;
        public string CustomerEmail { get; set; } = string.Empty;
        public bool Verified { get; set; }
    }

    [Workflow("GroupWaitResumptionWorkflow", "1.0")]
    public sealed class GroupWaitResumptionWorkflow : WorkflowContainer
    {
        public string OrderId { get; set; } = string.Empty;
        public bool StockOk { get; set; }
        public bool CustomerOk { get; set; }
        public List<string> ExecutionLog { get; set; } = new();

        public override async IAsyncEnumerable<Wait> Run()
        {
            ExecutionLog.Add("Start");
            yield return WaitSignal<ResumptionOrderReceivedSignal>("OrderReceived", "WaitOrderReceived")
                .AfterMatch(sig => {
                    OrderId = sig.OrderId;
                    ExecutionLog.Add($"OrderReceived:{OrderId}");
                });

            var stockWait = WaitSignal<ResumptionStockConfirmedSignal>("StockConfirmed", "WaitStock")
                .MatchIf(sig => sig.OrderId == OrderId)
                .AfterMatch(sig => {
                    StockOk = true;
                    ExecutionLog.Add("StockOk");
                });

            var customerWait = WaitSignal<ResumptionCustomerVerifiedSignal>("CustomerVerified", "WaitCustomer")
                .MatchIf(sig => sig.OrderId == OrderId)
                .AfterMatch(sig => {
                    CustomerOk = true;
                    ExecutionLog.Add("CustomerOk");
                });

            yield return WaitGroup([ (SignalWait<ResumptionStockConfirmedSignal>)stockWait, (SignalWait<ResumptionCustomerVerifiedSignal>)customerWait ], "GroupWait")
                .MatchAll();

            if (!StockOk || !CustomerOk)
            {
                ExecutionLog.Add($"Aborted:StockOk={StockOk},CustomerOk={CustomerOk}");
                yield break;
            }

            ExecutionLog.Add("CompletedSuccessfully");
        }
    }

    public class GroupWaitResumptionTests
    {
        private class TestSchemaGenerator : JSchemaGenerator
        {
            public override Newtonsoft.Json.Schema.JSchema Generate(Type type) => Newtonsoft.Json.Schema.JSchema.Parse("{}");
            public override Newtonsoft.Json.Schema.JSchema Generate(Type type, bool rootSchemaNullable) => Newtonsoft.Json.Schema.JSchema.Parse("{}");
        }

        private ServiceProvider CreateProvider(string connectionString)
        {
            var services = new ServiceCollection();
            services.AddWorkflowsShared();
            services.AddWorkflowsRunner();
            services.AddSingleton<JSchemaGenerator, TestSchemaGenerator>();

            services.AddWorkflowsInProcessHost(connectionString);

            var provider = services.BuildServiceProvider();
            using (var scope = provider.CreateScope())
            {
                scope.ServiceProvider.GetRequiredService<WorkflowsDbContext>().Database.EnsureCreated();
            }
            return provider;
        }

        private async Task SyncDefinitions(ServiceProvider provider)
        {
            var registry = provider.GetRequiredService<IWorkflowBuilder>();
            registry.RegisterWorkflow<GroupWaitResumptionWorkflow>("GroupWaitResumptionWorkflow", "1.0");
            registry.RegisterSignal<ResumptionOrderReceivedSignal>("OrderReceived");
            registry.RegisterSignal<ResumptionStockConfirmedSignal>("StockConfirmed");
            registry.RegisterSignal<ResumptionCustomerVerifiedSignal>("CustomerVerified");

            var packageField = typeof(WorkflowBuilder).GetField("registrationPackage", BindingFlags.NonPublic | BindingFlags.Instance);
            var package = (BulkRegistrationPackage)packageField!.GetValue(registry)!;

            using (var scope = provider.CreateScope())
            {
                var defRepo = scope.ServiceProvider.GetRequiredService<IDefinitionRepository>();
                await defRepo.SyncDefinitionsAsync(package);
            }
        }

        [Fact]
        public async Task GroupWait_ResumesCorrectly_AcrossProcessRestarts()
        {
            var dbName = Guid.NewGuid().ToString();
            var connectionString = $"Data Source={dbName};Mode=Memory;Cache=Shared";

            Guid instanceId;

            // Keep master connection open so the shared in-memory SQLite database is not cleared when a provider is disposed
            using (var masterConn = new SqliteConnection(connectionString))
            {
                masterConn.Open();

                // --- RUN 1: Start workflow and push first signal ---
                using (var provider1 = CreateProvider(connectionString))
                {
                    await SyncDefinitions(provider1);

                    using (var scope = provider1.CreateScope())
                    {
                        var orchestrator = scope.ServiceProvider.GetRequiredService<IOrchestrator>();
                        instanceId = await orchestrator.StartWorkflowAsync("GroupWaitResumptionWorkflow", "1.0", new { });

                        await orchestrator.ProcessSignalAsync(new SignalDto
                        {
                            SignalIdentifier = "OrderReceived",
                            Data = new ResumptionOrderReceivedSignal { OrderId = "ORD-123", Amount = 100 }
                        });
                    }
                }

                // --- RUN 2: Simulating App Restart + push second signal (StockConfirmed) ---
                using (var provider2 = CreateProvider(connectionString))
                {
                    await SyncDefinitions(provider2);

                    using (var scope = provider2.CreateScope())
                    {
                        var orchestrator = scope.ServiceProvider.GetRequiredService<IOrchestrator>();
                        
                        await orchestrator.ProcessSignalAsync(new SignalDto
                        {
                            SignalIdentifier = "StockConfirmed",
                            Data = new ResumptionStockConfirmedSignal { OrderId = "ORD-123", Status = "Available" }
                        });
                    }
                }

                // --- RUN 3: Simulating another App Restart + push third signal (CustomerVerified) ---
                using (var provider3 = CreateProvider(connectionString))
                {
                    await SyncDefinitions(provider3);

                    using (var scope = provider3.CreateScope())
                    {
                        var orchestrator = scope.ServiceProvider.GetRequiredService<IOrchestrator>();
                        
                        await orchestrator.ProcessSignalAsync(new SignalDto
                        {
                            SignalIdentifier = "CustomerVerified",
                            Data = new ResumptionCustomerVerifiedSignal { OrderId = "ORD-123", CustomerEmail = "ORD-123", Verified = true }
                        });
                    }

                    // --- ASSERTIONS ---
                    using (var scope = provider3.CreateScope())
                    {
                        var store = scope.ServiceProvider.GetRequiredService<IWorkflowStore>();
                        var state = await store.GetInstanceStateAsync(instanceId);

                        state.Status.Should().Be(WorkflowInstanceStatus.Completed);

                        var workflow = (GroupWaitResumptionWorkflow)state.StateObject.Instance;
                        workflow.StockOk.Should().BeTrue();
                        workflow.CustomerOk.Should().BeTrue();
                        workflow.ExecutionLog.Should().Contain("StockOk");
                        workflow.ExecutionLog.Should().Contain("CustomerOk");
                        workflow.ExecutionLog.Should().Contain("CompletedSuccessfully");
                        workflow.ExecutionLog.Should().NotContainMatch("Aborted*");
                    }
                }

                masterConn.Close();
            }
        }
    }
}
