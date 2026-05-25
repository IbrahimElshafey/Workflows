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
using Workflows.Runner.Tests.TestWorkflows;
using Workflows.Runner.Tests.Infrastructure;
using Workflows.Shared;

namespace Workflows.Runner.Tests
{
    public class FirstWaitRegistrationAndCloningTests
    {
        private ServiceProvider CreateServiceProvider(string dbName, out SqliteConnection connection)
        {
            var services = new ServiceCollection();

            services.AddWorkflowsShared();
            services.AddWorkflowsRunner();
            services.AddSingleton<JSchemaGenerator, MockSchemaGenerator>();
            services.AddSingleton<ICommandHandlerFactory, InMemoryCommandHandlerFactory>();

            var connectionString = $"Data Source={dbName};Mode=Memory;Cache=Shared";
            connection = new SqliteConnection(connectionString);
            connection.Open();

            services.AddWorkflowsInProcessHost(connectionString);

            var provider = services.BuildServiceProvider();

            using (var scope = provider.CreateScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<WorkflowsDbContext>();
                context.Database.EnsureCreated();
            }

            return provider;
        }

        [Fact]
        public async Task SyncDefinitions_ShouldCreateFirstWaitInstance_WithIsFirstWaitTrue()
        {
            // Arrange
            var dbName = $"RegTest_{Guid.NewGuid():N}";
            using var provider = CreateServiceProvider(dbName, out var connection);

            var registry = provider.GetRequiredService<IWorkflowBuilder>();
            registry.RegisterWorkflow<SignalFirstWaitWorkflow>("SignalFirstWaitWorkflow", "1.0");
            registry.RegisterSignal<OrderReceivedSignal>("OrderReceived");

            var packageField = typeof(WorkflowBuilder).GetField("registrationPackage", BindingFlags.NonPublic | BindingFlags.Instance);
            var package = (BulkRegistrationPackage)packageField!.GetValue(registry)!;

            // Act
            using (var scope = provider.CreateScope())
            {
                var defRepo = scope.ServiceProvider.GetRequiredService<IDefinitionRepository>();
                var syncResult = await defRepo.SyncDefinitionsAsync(package);
                syncResult.Success.Should().BeTrue();
            }

            // Assert
            using (var scope = provider.CreateScope())
            {
                var dbContext = scope.ServiceProvider.GetRequiredService<WorkflowsDbContext>();
                
                // Verify that a first wait instance is saved
                var signalWaits = await dbContext.SignalWaits.ToListAsync();
                signalWaits.Should().NotBeEmpty();
                signalWaits.All(sw => sw.IsFirstWait).Should().BeTrue();

                var instances = await dbContext.WorkflowInstances.ToListAsync();
                instances.Should().HaveCount(1);
                var instance = instances.First();
                instance.WorkflowType.Should().Be("SignalFirstWaitWorkflow");

                // Verify IsFirstWait = true inside the JSON waits list as well
                var signalWaitDto = instance.Waits.First() as SignalWaitDto;
                signalWaitDto.Should().NotBeNull();
                signalWaitDto!.IsFirstWait.Should().BeTrue();
            }

            connection.Close();
        }

        [Fact]
        public async Task SyncDefinitions_ShouldThrow_WhenFirstWaitHasNoSignalWait()
        {
            // Arrange
            var dbName = $"RegTest_{Guid.NewGuid():N}";
            using var provider = CreateServiceProvider(dbName, out var connection);

            var registry = provider.GetRequiredService<IWorkflowBuilder>();
            registry.RegisterWorkflow<InvalidFirstWaitWorkflow>("InvalidFirstWaitWorkflow", "1.0");
            registry.RegisterCommand<ProcessPaymentCommand, ProcessPaymentResult>("ProcessPayment", default, CommandExecutionMode.Deferred);

            var packageField = typeof(WorkflowBuilder).GetField("registrationPackage", BindingFlags.NonPublic | BindingFlags.Instance);
            var package = (BulkRegistrationPackage)packageField!.GetValue(registry)!;

            // Act
            using (var scope = provider.CreateScope())
            {
                var defRepo = scope.ServiceProvider.GetRequiredService<IDefinitionRepository>();
                var syncResult = await defRepo.SyncDefinitionsAsync(package);
                
                // Assert
                syncResult.Success.Should().BeFalse();
                syncResult.Errors.Should().NotBeEmpty();
                syncResult.Errors.First().Message.Should().Contain("Workflow first wait must be a SignalWait or a GroupWait containing at least one SignalWait");
            }

            connection.Close();
        }

        [Fact]
        public async Task ProcessSignal_OnFirstWait_ShouldCloneInstance_AndRemapAllIds()
        {
            // Arrange
            var dbName = $"RegTest_{Guid.NewGuid():N}";
            using var provider = CreateServiceProvider(dbName, out var connection);

            var registry = provider.GetRequiredService<IWorkflowBuilder>();
            registry.RegisterWorkflow<SignalFirstWaitWorkflow>("SignalFirstWaitWorkflow", "1.0");
            registry.RegisterSignal<OrderReceivedSignal>("OrderReceived");

            var packageField = typeof(WorkflowBuilder).GetField("registrationPackage", BindingFlags.NonPublic | BindingFlags.Instance);
            var package = (BulkRegistrationPackage)packageField!.GetValue(registry)!;

            using (var scope = provider.CreateScope())
            {
                var defRepo = scope.ServiceProvider.GetRequiredService<IDefinitionRepository>();
                var syncResult = await defRepo.SyncDefinitionsAsync(package);
                syncResult.Success.Should().BeTrue();
            }

            using (var scope = provider.CreateScope())
            {
                var orchestrator = scope.ServiceProvider.GetRequiredService<IOrchestrator>();
                var dbContext = scope.ServiceProvider.GetRequiredService<WorkflowsDbContext>();
                var workflowStore = scope.ServiceProvider.GetRequiredService<IWorkflowStore>();

                // Get original first wait instance ID
                var firstWaitInstance = await dbContext.WorkflowInstances.FirstAsync();
                var originalInstanceId = firstWaitInstance.Id;

                // Act - Send the signal matching the first wait
                await orchestrator.ProcessSignalAsync(new SignalDto
                {
                    SignalIdentifier = "OrderReceived",
                    Data = new OrderReceivedSignal { OrderId = "ORD-CLONE-123", Amount = 100 }
                });

                // Verify that a brand new instance was created and completed
                var allInstances = await dbContext.WorkflowInstances.ToListAsync();
                allInstances.Should().HaveCount(2);

                var clonedInstance = allInstances.First(wi => wi.Id != originalInstanceId);
                clonedInstance.Status.Should().Be((int)WorkflowInstanceStatus.Completed);
                clonedInstance.Waits.Should().BeEmpty();

                // Assert instance property state of the cloned instance
                var state = await workflowStore.GetInstanceStateAsync(clonedInstance.Id);
                var workflow = state!.StateObject.Instance as SignalFirstWaitWorkflow;
                workflow.Should().NotBeNull();
                workflow!.Completed.Should().BeTrue();
                workflow.ReceivedOrderId.Should().Be("ORD-CLONE-123");

                // Verify the original instance is completely unchanged (still waiting on first wait)
                var originalDbState = await dbContext.WorkflowInstances.FindAsync(originalInstanceId);
                originalDbState.Should().NotBeNull();
                originalDbState!.Status.Should().Be((int)WorkflowInstanceStatus.Running);

                var originalSignalWaits = await dbContext.SignalWaits.Where(sw => sw.WorkflowInstanceId == originalInstanceId).ToListAsync();
                originalSignalWaits.Should().NotBeEmpty();
                originalSignalWaits.All(sw => sw.IsFirstWait).Should().BeTrue();

                var clonedSignalWaits = await dbContext.SignalWaits.Where(sw => sw.WorkflowInstanceId == clonedInstance.Id).ToListAsync();
                clonedSignalWaits.Should().BeEmpty(); // Since it completed, waits are pruned
            }

            connection.Close();
        }
    }

    public sealed class InvalidFirstWaitWorkflow : WorkflowContainer
    {
        public override async IAsyncEnumerable<Wait> Run()
        {
            yield return ExecuteCommand<ProcessPaymentCommand, ProcessPaymentResult>(
                "ProcessPayment",
                new ProcessPaymentCommand { OrderId = "ORD-001", Amount = 100 })
                .WithExecutionMode(CommandExecutionMode.Deferred);
        }
    }

    public sealed class SignalFirstWaitWorkflow : WorkflowContainer
    {
        public bool Completed { get; set; }
        public string ReceivedOrderId { get; set; }

        public override async IAsyncEnumerable<Wait> Run()
        {
            yield return WaitSignal<OrderReceivedSignal>("OrderReceived", "First")
                .AfterMatch(signal => {
                    this.ReceivedOrderId = signal.OrderId;
                    this.Completed = true;
                });
        }
    }
}
