using System.Linq;
using System.Threading.Tasks;
using Workflows.Abstraction.DTOs;

namespace Workflows.Definition
{
    public interface IWorkflowRegister
    {
        // Generic registration relies on the Type name or attributes for metadata
        IWorkflowRegister RegisterWorkflow<TWorkflow>(string version = "1.0.0") where TWorkflow : WorkflowContainer;

        IWorkflowRegister RegisterSignal<TSignal>(string explicitName = null);

        IWorkflowRegister RegisterCommand<TCommand, TResult>();

        // Physical runner check-in
        IWorkflowRegister RegisterRunner(string runnerId, params string[] listeningQueues);

        // Finalize and push to the Orchestrator
        Task<RegistrationResult> CommitAsync();
    }
    public static class WorkflowRegisterExtensions
    {
        public static IWorkflowRegister RegisterFromAssemblyContaining<T>(
            this IWorkflowRegister register,
            string version)
        {
            var assembly = typeof(T).Assembly;

            // 1. Find all workflows
            var workflowTypes = assembly.GetTypes()
                .Where(t => t.IsSubclassOf(typeof(WorkflowContainer)) && !t.IsAbstract);

            foreach (var type in workflowTypes)
            {
                // Use reflection to call the generic RegisterWorkflow<T> method
                var method = typeof(IWorkflowRegister).GetMethod(nameof(IWorkflowRegister.RegisterWorkflow))
                                                      .MakeGenericMethod(type);
                method.Invoke(register, new object[] { version });
            }

            // 2. Do the same for Signals and Commands based on their marker interfaces...

            return register;
        }
        /*
         *
            // In the user's Program.cs or Startup.cs
            builder.Services.AddWorkflows(setup => 
            {
                // Smart: Automatically find and register everything in this assembly as v1.2.0
                setup.RegisterFromAssemblyContaining<OrderProcessingWorkflow>(version: "1.2.0");

                // Easy: Or explicitly register specific ones fluently
                setup.RegisterWorkflow<PaymentWorkflow>("2.0.0")
                     .RegisterSignal<PaymentCompletedSignal>()
                     .RegisterCommand<CancelOrderCommand, CancelResult>();
         
                // Bind this specific runner instance
                setup.RegisterRunner(runnerId: "Runner-Node-01", listeningQueues: new[] { "workflows.v1", "workflows.v2" });
            });
         * */
    }
}