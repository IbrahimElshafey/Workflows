using ClientOnboarding.InOuts;
using ClientOnboarding.Services;
using ClientOnboarding.Workflow;
using Microsoft.Extensions.DependencyInjection;
using Workflows.Handler.InOuts;
using Workflows.Handler.Testing;

namespace Tests
{
    public partial class ClientOnboardingTest
    {
        [Fact]
        public async Task ClientOnBoarding_NoSimulate_Test()
        {
            using var test = new TestShell(
                nameof(ClientOnBoarding_NoSimulate_Test),
                typeof(ClientOnboardingService),
                typeof(ClientOnboardingWorkflowPublic));
            test.RegisteredServices.AddScoped<IClientOnboardingService, ClientOnboardingService>();
            await test.ScanTypes();

            var service = test.CurrentApp.Services.GetService<IClientOnboardingService>();
            var registration = service.ClientFillsForm(new RegistrationForm { UserId = 2000, FormData = "Form Data" });
            var currentInstance = await RoundCheck(test, 1);

            var ownerApprove = service.OwnerApproveClient(new OwnerApproveClientInput { Decision = true, TaskId = currentInstance.OwnerTaskId });
            currentInstance = await RoundCheck(test, 2);


            var meetingResult = service.SendMeetingResult(currentInstance.ClientMeetingId);
            currentInstance = await RoundCheck(test, 3, true);
        }

        private async Task<ClientOnboardingWorkflowPublic> RoundCheck(TestShell test, int round, bool finished = false)
        {
            var signals = await test.GetSignals();
            Assert.Equal(round, signals.Count);
            var waits = await test.GetWaits();
            Assert.Equal(finished ? round : round + 1, waits.Count);
            var instances = await test.GetInstances<ClientOnboardingWorkflowPublic>();
            Assert.Single(instances);
            if (finished)
                Assert.Equal(WorkflowInstanceStatus.Completed, instances[0].Status);
            return instances[0].StateObject as ClientOnboardingWorkflowPublic;
        }
    }
}