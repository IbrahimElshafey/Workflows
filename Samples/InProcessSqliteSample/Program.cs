using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Newtonsoft.Json;
using Newtonsoft.Json.Schema.Generation;
using Workflows.Abstraction.DTOs;
using Workflows.Abstraction.DTOs.Registration;
using Workflows.Abstraction.DTOs.Waits;
using Workflows.Abstraction.Enums;
using Workflows.Abstraction.Orchestrator;
using Workflows.Abstraction.Persistence;
using Workflows.Abstraction.Runner;
using Workflows.Communication.Abstraction;
using Workflows.Definition;
using Workflows.Definition.Registration;
using Workflows.Hosting.InProcess;
using Workflows.Primitives;
using Workflows.Storage.EntityFrameworkCore;
using Workflows.Shared;
using Workflows.Runner;
using Workflows.Orchestrator;

namespace InProcessSqliteSample
{
    public class Program
    {
        private static IServiceProvider _serviceProvider = null!;

        public static async Task Main(string[] args)
        {
            Console.Title = "Workflows In-Process SQLite Sample Console";
            var dbPath = ".\\sample_workflows.db";

            Console.WriteLine("==================================================================");
            Console.WriteLine("       Workflows Engine - In-Process SQLite Interactive Sample     ");
            Console.WriteLine("==================================================================");
            Console.WriteLine($"Using database file: '{dbPath}' (persistent)\n");

            // 1. Build and configure DI services
            var services = new ServiceCollection();

            // Standard runner and shared dependencies
            services.AddWorkflowsShared();
            services.AddWorkflowsRunner();
            services.AddSingleton<JSchemaGenerator>();

            // Register deferred command handlers (resolved manually after orchestrator dispatches the notification)
            services.AddTransient<AuthorizePaymentHandler>();
            services.AddTransient<ShipOrderHandler>();

            // Setup in-process host with SQLite provider
            services.AddWorkflowsInProcessHost($"Data Source={dbPath}");

            // Configure Console transport for command routing in addition to defaults
            services.AddSingleton<ConsoleMessageTransport>();
            services.AddSingleton<DummyMessageSubscriber>();

            // We must override the routing to dispatch CommandDispatchNotification to our ConsoleMessageTransport
            services.AddSingleton(sp =>
            {
                var routingBuilder = new TransportRoutingBuilder();
                routingBuilder.UseDefault<InProcessMessageTransport, InProcessMessageSubscriber>();

                // Orchestrator runner requests go to the runner
                routingBuilder.ForMessage<StartWorkflowRequest>()
                    .Use<InProcessMessageTransport, InProcessMessageSubscriber>("in-process-runner");
                routingBuilder.ForMessage<WorkflowExecutionRequest>()
                    .Use<InProcessMessageTransport, InProcessMessageSubscriber>("in-process-runner");

                // Deferred commands are routed to our ConsoleMessageTransport so we can intercept and display them
                routingBuilder.ForMessage<CommandDispatchNotification>()
                    .Use<ConsoleMessageTransport, DummyMessageSubscriber>("console-deferred-dispatcher");

                return routingBuilder;
            });

            _serviceProvider = services.BuildServiceProvider();

            // 2. Initialize DB Schema
            using (var scope = _serviceProvider.CreateScope())
            {
                var dbContext = scope.ServiceProvider.GetRequiredService<WorkflowsDbContext>();
                dbContext.Database.EnsureCreated();
            }

            // 3. Register Workflow Definitions, Signals, and Commands in registry builder
            using (var scope = _serviceProvider.CreateScope())
            {
                var builder = scope.ServiceProvider.GetRequiredService<IWorkflowBuilder>();

                // Register our OrderProcessingWorkflow using WorkflowAttribute
                builder.RegisterWorkflow<OrderProcessingWorkflow>();

                // Register signals used in OrderProcessingWorkflow:
                //   - OrderReceived: GENERIC first wait (no MatchIf — any instance accepts it)
                //   - StockConfirmed / CustomerVerified: STATE-DEPENDENT (matched by OrderId / Email)
                builder.RegisterSignal<OrderReceivedSignal>("OrderReceived");
                builder.RegisterSignal<StockConfirmedSignal>("StockConfirmed");
                builder.RegisterSignal<CustomerVerifiedSignal>("CustomerVerified");

                // Register deferred commands used in OrderProcessingWorkflow
                builder.RegisterCommand<PaymentRequest, PaymentResult>("AuthorizePayment", default, CommandExecutionMode.Deferred);
                builder.RegisterCommand<ShipOrderCommand, ShipOrderResult>("ShipOrder", default, CommandExecutionMode.Deferred);

                // Build and persist definitions to the SQLite database so the orchestrator can validate them
                var definitionRepository = scope.ServiceProvider.GetRequiredService<IDefinitionRepository>();

                // Extract package using reflection since it is private/internal
                var packageField = builder.GetType().GetField("registrationPackage", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (packageField == null)
                {
                    throw new InvalidOperationException("Could not find field 'registrationPackage' on WorkflowBuilder.");
                }
                var package = (BulkRegistrationPackage)packageField.GetValue(builder)!;

                var syncResult = await definitionRepository.SyncDefinitionsAsync(package);
                if (!syncResult.Success)
                {
                    var errorMsgs = new List<string>();
                    foreach (var err in syncResult.Errors)
                    {
                        errorMsgs.Add($"{err.EntityName} ({err.ErrorType}): {err.Message}");
                    }
                    throw new InvalidOperationException($"Definition synchronization failed: {string.Join(", ", errorMsgs)}");
                }
            }

            // 3.5. Restore dispatched deferred commands from DB on startup (for app restarts)
            using (var scope = _serviceProvider.CreateScope())
            {
                var dbContext = scope.ServiceProvider.GetRequiredService<WorkflowsDbContext>();
                var activeInstances = await dbContext.WorkflowInstances.ToListAsync();

                var restoredNotifications = new List<CommandDispatchNotification>();

                void CollectCommandWaitsLocal(WaitInfrastructureDto wait, List<CommandWaitDto> list)
                {
                    if (wait == null) return;
                    if (wait is CommandWaitDto commandWait)
                    {
                        list.Add(commandWait);
                    }
                    if (wait.ChildWaits != null)
                    {
                        foreach (var child in wait.ChildWaits)
                        {
                            CollectCommandWaitsLocal(child, list);
                        }
                    }
                }

                foreach (var inst in activeInstances)
                {
                    var commandWaits = new List<CommandWaitDto>();
                    foreach (var wait in inst.Waits)
                    {
                        CollectCommandWaitsLocal(wait, commandWaits);
                    }

                    foreach (var cw in commandWaits)
                    {
                        if (cw.Status == WaitStatus.Waiting && cw.ExecutionMode == CommandExecutionMode.Deferred)
                        {
                            restoredNotifications.Add(new CommandDispatchNotification
                            {
                                CommandWaitId = cw.Id,
                                HandlerKey = cw.HandlerKey,
                                CommandData = cw.CommandData?.ToString() ?? string.Empty
                            });
                        }
                    }
                }

                lock (ConsoleMessageTransport.DispatchedCommands)
                {
                    ConsoleMessageTransport.DispatchedCommands.Clear();
                    ConsoleMessageTransport.DispatchedCommands.AddRange(restoredNotifications);
                }
            }

            // 4. Start the background scheduler
            var scheduler = _serviceProvider.GetRequiredService<Scheduler>();
            await scheduler.StartAsync(default);

            // 5. Run Interactive Menu loop
            bool running = true;
            while (running)
            {
                PrintMenu();
                var choice = Console.ReadLine();
                switch (choice)
                {
                    case "1":
                        await StartWorkflowOption();
                        break;
                    case "2":
                        await ListWorkflowInstancesOption();
                        break;
                    case "3":
                        await PushSignalOption();
                        break;
                    case "4":
                        await HandleDeferredCommandsOption();
                        break;
                    case "5":
                        await ViewWorkflowDetailsOption();
                        break;
                    case "6":
                        running = false;
                        break;
                    default:
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine("Invalid option. Please try again.");
                        Console.ResetColor();
                        break;
                }
            }

            await scheduler.StopAsync(default);
            Console.WriteLine("Goodbye!");
        }

        private static void PrintMenu()
        {
            Console.WriteLine("\n------------------------------------------------------------------");
            Console.WriteLine("Workflow: Start -> [1] OrderReceived -> [2] Stock+Customer -> [3] Pay -> [4] Ship");
            Console.WriteLine("------------------------------------------------------------------");
            Console.WriteLine("Menu Options:");
            Console.WriteLine("  1. Start a New Workflow Instance (no input needed — waits for OrderReceived)");
            Console.WriteLine("  2. List All Active Workflow Instances & Wait DB Records");
            Console.WriteLine("  3. Push a Signal  (OrderReceived / StockConfirmed / CustomerVerified)");
            Console.WriteLine("  4. View Intercepted Commands & Push Command Results     [Steps 3-4]");
            Console.WriteLine("  5. View Workflow Instance Details & Execution Log");
            Console.WriteLine("  6. Exit");
            Console.Write("Enter selection: ");
        }

        private static async Task StartWorkflowOption()
        {
            Console.WriteLine("\n--- Starting New Order Workflow Instance ---");
            Console.WriteLine("The workflow starts with NO domain data — it will wait for an 'OrderReceived' signal.");
            Console.WriteLine("Use option 3 to push that signal with the order details.");
            Console.Write("Press Enter to create the workflow instance...");
            Console.ReadLine();

            using var scope = _serviceProvider.CreateScope();
            var orchestrator = scope.ServiceProvider.GetRequiredService<IOrchestrator>();

            // Start with empty input — domain state comes from the first generic signal
            var instanceId = await orchestrator.StartWorkflowAsync("OrderWorkflow", 1, new { });

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"Workflow Instance created and is now waiting for 'OrderReceived' signal!");
            Console.WriteLine($"Instance ID: {instanceId}");
            Console.ResetColor();
        }

        private static async Task ListWorkflowInstancesOption()
        {
            Console.WriteLine("\n--- Active Workflow Instances in Database ---");
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<WorkflowsDbContext>();

            var instances = await db.WorkflowInstances.AsNoTracking().ToListAsync();
            if (instances.Count == 0)
            {
                Console.WriteLine("No workflow instances found in database.");
                return;
            }

            foreach (var inst in instances)
            {
                string statusStr = ((WorkflowInstanceStatus)inst.Status).ToString();
                Console.WriteLine($"- [{inst.WorkflowType}] Instance ID: {inst.Id}");
                Console.WriteLine($"  Status: {statusStr}, Created: {inst.Created:yyyy-MM-dd HH:mm:ss}");

                // Query DB index tables for waits (TPC: each type has its own table)
                var signalWaitsForInst = await db.SignalWaits.AsNoTracking()
                    .Where(w => w.WorkflowInstanceId == inst.Id && w.Status == 0)
                    .ToListAsync<WorkflowWaitEntity>();
                var commandWaitsForInst = await db.CommandWaits.AsNoTracking()
                    .Where(w => w.WorkflowInstanceId == inst.Id && w.Status == 0)
                    .ToListAsync<WorkflowWaitEntity>();
                var timeWaitsForInst = await db.TimeWaits.AsNoTracking()
                    .Where(w => w.WorkflowInstanceId == inst.Id && w.Status == 0)
                    .ToListAsync<WorkflowWaitEntity>();
                var waits = signalWaitsForInst
                    .Concat(commandWaitsForInst)
                    .Concat(timeWaitsForInst)
                    .ToList();

                if (waits.Count > 0)
                {
                    Console.WriteLine("  Active Database Wait Records (Phase 1 Index):");
                    foreach (var wait in waits)
                    {
                        string typeName = wait.GetType().Name;
                        if (wait is SignalWaitEntity sigWait)
                        {
                            Console.WriteLine($"    * [SignalWait] Wait ID: {sigWait.Id}");
                            Console.WriteLine($"      Signal Path: {sigWait.SignalPath}");
                            if (!string.IsNullOrEmpty(sigWait.TemplateHashKey))
                            {
                                Console.WriteLine($"      Template Hash Key: {sigWait.TemplateHashKey}");
                            }
                            if (!string.IsNullOrEmpty(sigWait.SignalExactMatchPaths))
                            {
                                Console.WriteLine($"      Exact Match Paths: {sigWait.SignalExactMatchPaths}");
                                Console.WriteLine($"      Exact Match Filter: {sigWait.ExactMatchFilter}");
                            }
                        }
                        else if (wait is CommandWaitEntity cmdWait)
                        {
                            Console.WriteLine($"    * [CommandWait] Wait ID: {cmdWait.Id}");
                            Console.WriteLine($"      Command Wait ID: {cmdWait.CommandWaitId}");
                        }
                        else if (wait is TimeWaitEntity timeWait)
                        {
                            Console.WriteLine($"    * [TimeWait] Wait ID: {timeWait.Id}");
                            Console.WriteLine($"      Unique Match ID:  {timeWait.UniqueMatchId}");
                            Console.WriteLine($"      Fires At (UTC):   {timeWait.ExecutionTime:yyyy-MM-dd HH:mm:ss}");
                        }
                        else
                        {
                            Console.WriteLine($"    * [{typeName}] Wait ID: {wait.Id}");
                        }
                    }
                }
                else
                {
                    Console.WriteLine("  No active database wait records (workflow is complete or idle).");
                }
                Console.WriteLine();
            }
        }

        private static async Task PushSignalOption()
        {
            Console.WriteLine("\n--- Push Signal ---");
            Console.WriteLine("Available Signals:");
            Console.WriteLine("  1. OrderReceived     [Step 1 — GENERIC, no filtering, bootstraps workflow state]");
            Console.WriteLine("  2. StockConfirmed    [Step 2a — matches by OrderId]");
            Console.WriteLine("  3. CustomerVerified  [Step 2b — matches by CustomerEmail]");
            Console.Write("Choose signal (1/2/3): ");
            var choice = Console.ReadLine();

            string signalName;
            object payload;

            if (choice == "1")
            {
                signalName = "OrderReceived";
                Console.Write("Enter Order ID (e.g. ORD-100): ");
                string orderId = Console.ReadLine() ?? string.Empty;
                Console.Write("Enter Customer Email: ");
                string email = Console.ReadLine() ?? string.Empty;
                Console.Write("Enter Amount ($): ");
                decimal.TryParse(Console.ReadLine(), out decimal amount);
                Console.Write("Enter Shipping Address: ");
                string address = Console.ReadLine() ?? "123 Main St";

                payload = new OrderReceivedSignal
                {
                    OrderId = orderId,
                    CustomerEmail = email,
                    Amount = amount == 0 ? 99.99m : amount,
                    ShippingAddress = address
                };
            }
            else if (choice == "2")
            {
                signalName = "StockConfirmed";
                Console.Write("Enter Order ID: ");
                string orderId = Console.ReadLine() ?? string.Empty;
                Console.Write("Enter Status (Available / OutOfStock): ");
                string status = Console.ReadLine() ?? "Available";

                payload = new StockConfirmedSignal { OrderId = orderId, Status = status };
            }
            else if (choice == "3")
            {
                signalName = "CustomerVerified";
                Console.Write("Enter Customer Email: ");
                string email = Console.ReadLine() ?? string.Empty;
                Console.Write("Verified successfully? (y/n): ");
                bool verified = Console.ReadLine()?.Trim().ToLower() == "y";

                payload = new CustomerVerifiedSignal { CustomerEmail = email, Verified = verified };
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("Invalid selection.");
                Console.ResetColor();
                return;
            }

            using var scope = _serviceProvider.CreateScope();
            var orchestrator = scope.ServiceProvider.GetRequiredService<IOrchestrator>();

            Console.WriteLine($"Pushing signal '{signalName}'...");
            await orchestrator.ProcessSignalAsync(new SignalDto
            {
                SignalIdentifier = signalName,
                Data = payload
            });

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("Signal pushed successfully.");
            Console.ResetColor();
            if (choice == "1")
            {
                Console.WriteLine("  -> Workflow now has state. Push StockConfirmed (2) and CustomerVerified (3) next.");
            }
            else
            {
                Console.WriteLine("  -> Once BOTH StockConfirmed AND CustomerVerified are received,");
                Console.WriteLine("     the workflow proceeds to payment (option 4).");
            }
        }

        private static async Task HandleDeferredCommandsOption()
        {
            Console.WriteLine("\n--- Intercepted Deferred Commands Queue ---");
            List<CommandDispatchNotification> commandQueue;
            lock (ConsoleMessageTransport.DispatchedCommands)
            {
                commandQueue = ConsoleMessageTransport.DispatchedCommands.ToList();
            }

            if (commandQueue.Count == 0)
            {
                Console.WriteLine("No pending dispatched commands intercepted.");
                return;
            }

            for (int i = 0; i < commandQueue.Count; i++)
            {
                var cmd = commandQueue[i];
                Console.WriteLine($"[{i + 1}] CommandWaitId: {cmd.CommandWaitId}");
                Console.WriteLine($"    HandlerKey (CommandName): {cmd.HandlerKey}");
                Console.WriteLine($"    Command Data: {cmd.CommandData}");
                Console.WriteLine();
            }

            Console.Write("Select command to execute/simulate (Enter index or 0 to cancel): ");
            if (!int.TryParse(Console.ReadLine(), out int selIndex) || selIndex < 1 || selIndex > commandQueue.Count)
            {
                return;
            }

            var selectedCommand = commandQueue[selIndex - 1];

            Console.WriteLine($"Simulating results for command: {selectedCommand.HandlerKey}");
            object resultObj;

            // Remove command from queue before executing to avoid double-processing
            lock (ConsoleMessageTransport.DispatchedCommands)
            {
                ConsoleMessageTransport.DispatchedCommands.Remove(selectedCommand);
            }

            using var scope = _serviceProvider.CreateScope();
            string commandJson = selectedCommand.CommandData is string s ? s : JsonConvert.SerializeObject(selectedCommand.CommandData);

            if (selectedCommand.HandlerKey == "AuthorizePayment")
            {
                var handler = scope.ServiceProvider.GetRequiredService<AuthorizePaymentHandler>();
                var request = JsonConvert.DeserializeObject<PaymentRequest>(commandJson) ?? new();
                resultObj = await handler.HandleAsync(request);
            }
            else if (selectedCommand.HandlerKey == "ShipOrder")
            {
                var handler = scope.ServiceProvider.GetRequiredService<ShipOrderHandler>();
                var command = JsonConvert.DeserializeObject<ShipOrderCommand>(commandJson) ?? new();
                resultObj = await handler.HandleAsync(command);
            }
            else
            {
                Console.WriteLine($"Unknown handler key '{selectedCommand.HandlerKey}'. Cannot auto-generate result.");
                return;
            }
            var orchestrator = scope.ServiceProvider.GetRequiredService<IOrchestrator>();

            Console.WriteLine("Pushing command result to orchestrator...");
            await orchestrator.ProcessCommandResultAsync(new CommandResultDto
            {
                CommandWaitId = selectedCommand.CommandWaitId,
                Result = resultObj,
                ClientSentTime = DateTime.UtcNow,
                OrchestratorReceiveTime = DateTime.UtcNow
            });

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("Command result submitted successfully.");
            Console.ResetColor();
        }

        private static async Task ViewWorkflowDetailsOption()
        {
            Console.WriteLine("\n--- View Workflow Instance Details ---");
            Console.Write("Enter Workflow Instance ID Guid: ");
            if (!Guid.TryParse(Console.ReadLine(), out Guid instanceId))
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("Invalid GUID format.");
                Console.ResetColor();
                return;
            }

            using var scope = _serviceProvider.CreateScope();
            var store = scope.ServiceProvider.GetRequiredService<IWorkflowStore>();
            var state = await store.GetInstanceStateAsync(instanceId);

            if (state == null)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("Workflow instance not found in database.");
                Console.ResetColor();
                return;
            }

            Console.WriteLine();
            Console.WriteLine($"Workflow Type: {state.WorkflowType}");
            Console.WriteLine($"Instance ID:   {state.Id}");
            Console.WriteLine($"Status:        {state.Status}");
            Console.WriteLine($"Created:       {state.Created:yyyy-MM-dd HH:mm:ss}");

            if (state.StateObject?.Instance is OrderProcessingWorkflow orderWorkflow)
            {
                Console.WriteLine("\n--- Workflow Domain Properties ---");
                Console.WriteLine($"OrderId:           {orderWorkflow.OrderId}");
                Console.WriteLine($"CustomerEmail:     {orderWorkflow.CustomerEmail}");
                Console.WriteLine($"Amount:            {orderWorkflow.Amount:C}");
                Console.WriteLine($"PaymentAuthorized: {orderWorkflow.PaymentAuthorized}");
                Console.WriteLine($"StockOk:           {orderWorkflow.StockOk}");
                Console.WriteLine($"CustomerOk:        {orderWorkflow.CustomerOk}");
                Console.WriteLine($"OrderShipped:      {orderWorkflow.OrderShipped}");
                Console.WriteLine($"TrackingCode:      {orderWorkflow.TrackingCode}");
                Console.WriteLine($"ErrorReason:       {orderWorkflow.ErrorReason}");

                Console.WriteLine("\n--- Workflow Execution Log ---");
                foreach (var log in orderWorkflow.ExecutionLog)
                {
                    Console.WriteLine($"  * {log}");
                }
            }
            else
            {
                Console.WriteLine("\nCould not cast state object to OrderProcessingWorkflow.");
            }
            Console.WriteLine();
        }
    }



    // Loopback transport to capture deferred command dispatches
    public class ConsoleMessageTransport : IMessageTransport
    {
        public static readonly List<CommandDispatchNotification> DispatchedCommands = new();

        public Task SendAsync<T>(string destination, T message)
        {
            if (message is CommandDispatchNotification notification)
            {
                lock (DispatchedCommands)
                {
                    DispatchedCommands.Add(notification);
                }

                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"\n[Notification] Intercepted Deferred Command!");
                Console.WriteLine($"  HandlerKey: {notification.HandlerKey}");
                Console.WriteLine($"  Wait ID:    {notification.CommandWaitId}");
                Console.WriteLine($"  Payload:    {notification.CommandData}");
                Console.Write("\nPress enter key to continue...");
                Console.ResetColor();
            }
            return Task.CompletedTask;
        }

        public Task<TResponse> SendAndReceiveAsync<TRequest, TResponse>(string destination, TRequest message)
        {
            throw new NotImplementedException();
        }
    }

    public class DummyMessageSubscriber : IMessageSubscriber
    {
        public void Subscribe<T>(Func<T, Task> handler) { }
        public void Dispose() { }
    }
}
