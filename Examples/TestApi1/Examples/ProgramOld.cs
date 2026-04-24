//// See https://aka.ms/new-console-template for more information

//using Workflows.Handler.Core;
//using TestApi1.Examples;

//namespace Test;

//public class ProgramOld
//{
//    private static Scanner _scanner;
//    private static async Task MainOld()
//    {



//        await TestWaitMany();

//        await TestSubWorkflowCall();
//        await TestReplayGoBackAfter();
//        await TestReplayGoBackBeforeNewMatch();
//        await TestReplayGoBackTo();
//        await TestWaitManyWorkflows();
//        await TestLoops();
//        await TestManyWaitsTypeInGroup();
//        await TestTimeWait();
//        await TestSameEventAgain();
//        await TestWaitInterfaceMethod();
//        await TestReplayGoBackToWithNewMatch();


//        //await Task.WhenAll(
//        //    TestWaitMany(),
//        //    TestSubWorkflowCall(),
//        //    TestReplayGoBackAfter(),
//        //    TestReplayGoBackBeforeNewMatch(),
//        //    TestReplayGoBackTo(),
//        //    TestWaitManyWorkflows(),
//        //    TestLoops(),
//        //    TestManyWaitsTypeInGroup(),
//        //    TestTimeWait(),
//        //    TestSameEventAgain());
//        Console.ReadLine();
//    }

//    private static async Task TestWaitInterfaceMethod()
//    {
//        await RegisterWorkflow(typeof(ProjectApprovalExample), nameof(ProjectApprovalExample.ExternalMethod));
//        //await RegisterWorkflow(typeof(ProjectApprovalExample), nameof(ProjectApprovalExample.InterfaceMethod));
//        var example = new ProjectApprovalExample();
//        var currentProject = ProjectApprovalExample.GetCurrentProject();
//        await example.ProjectSubmitted(currentProject);
//        example.ManagerFiveApproveProject(new ApprovalDecision(currentProject.Id, false));
//    }

//    private static async Task TestSameEventAgain()
//    {

//        await RegisterWorkflow(typeof(WaitSameEventAgain), nameof(WaitSameEventAgain.Test_WaitSameEventAgain));
//        var example = new WaitSameEventAgain();
//        var currentProject = ProjectApprovalExample.GetCurrentProject();
//        await example.ProjectSubmitted(currentProject);
//        example.ManagerOneApproveProject(new ApprovalDecision(currentProject.Id, false));
//        example.ManagerOneApproveProject(new ApprovalDecision(currentProject.Id, true));
//    }
//    private static async Task TestTimeWait()
//    {
//        await RegisterWorkflow(typeof(TestTimeExample), nameof(TestTimeExample.TimeWaitTest));
//        var example = new TestTimeExample();
//        var currentProject = ProjectApprovalExample.GetCurrentProject();
//        await example.ProjectSubmitted(currentProject);
//        await Task.Delay(TimeSpan.FromSeconds(30));
//        example.ManagerOneApproveProject(new ApprovalDecision(currentProject.Id, true));
//    }

//    private static async Task TestManyWaitsTypeInGroup()
//    {
//        await RegisterWorkflow(typeof(ManyWaitsTypeInGroupExample), nameof(ManyWaitsTypeInGroupExample.ManyWaitsTypeInGroup));
//        var example = new ManyWaitsTypeInGroupExample();
//        var currentProject = ProjectApprovalExample.GetCurrentProject();
//        await example.ProjectSubmitted(currentProject);
//        example.ManagerOneApproveProject(new ApprovalDecision(currentProject.Id, true));
//        example.ManagerTwoApproveProject(new ApprovalDecision(currentProject.Id, true));
//        //example.ManagerTwoApproveProject(new ApprovalDecision(currentProject.Id, true));
//        example.ManagerThreeApproveProject(new ApprovalDecision(currentProject.Id, true));
//        example.ManagerThreeApproveProject(new ApprovalDecision(currentProject.Id, true));
//        example.ManagerFourApproveProject(new ApprovalDecision(currentProject.Id, true));
//    }



//    private static async Task TestWaitManyWorkflows()
//    {
//        //await RegisterWorkflow(typeof(WaitManyWorkflowsExample), nameof(WaitManyWorkflowsExample.WaitFirstWorkflow));
//        //await RegisterWorkflow(typeof(WaitManyWorkflowsExample), nameof(WaitManyWorkflowsExample.WaitManyWorkflows));
//        await RegisterWorkflow(typeof(WaitManyWorkflowsExample), nameof(WaitManyWorkflowsExample.WaitSubWorkflowTwoLevels));
//        var example = new WaitManyWorkflowsExample();
//        var currentProject = ProjectApprovalExample.GetCurrentProject();
//        await example.ProjectSubmitted(currentProject);
//        example.ManagerOneApproveProject(new ApprovalDecision(currentProject.Id, true));
//        //await Task.Delay(3000);
//        example.ManagerTwoApproveProject(new ApprovalDecision(currentProject.Id, true));
//        example.ManagerTwoApproveProject(new ApprovalDecision(currentProject.Id, true));
//        //await Task.Delay(3000);
//        example.ManagerThreeApproveProject(new ApprovalDecision(currentProject.Id, true));
//        //await Task.Delay(3000);
//        example.ManagerThreeApproveProject(new ApprovalDecision(currentProject.Id, true));
//    }
//    private static async Task TestLoops()
//    {
//        await RegisterWorkflow(typeof(TestLoopsExample), nameof(TestLoopsExample.WaitManagerOneThreeTimeApprovals));
//        var example = new TestLoopsExample();
//        var currentProject = ProjectApprovalExample.GetCurrentProject();
//        await example.ProjectSubmitted(currentProject);
//        example.ManagerOneApproveProject(new ApprovalDecision(currentProject.Id, true));
//        example.ManagerOneApproveProject(new ApprovalDecision(currentProject.Id, true));
//        example.ManagerOneApproveProject(new ApprovalDecision(currentProject.Id, true));
//    }
//    private static async Task TestWaitMany()
//    {
//        await RegisterWorkflow(typeof(TestWaitManyExample), nameof(TestWaitManyExample.WaitThreeMethod));
//        //await RegisterWorkflow(typeof(TestWaitManyExample), nameof(TestWaitManyExample.WaitManyAndCountExpressionDefined));
//        var example = new TestWaitManyExample();
//        var project = ProjectApprovalExample.GetCurrentProject();
//        example.CurrentProject = project;
//        await example.ProjectSubmitted(project);
//        example.ManagerOneApproveProject(new ApprovalDecision(project.Id, true));
//        example.ManagerTwoApproveProject(new ApprovalDecision(project.Id, true));
//        example.ManagerTwoApproveProject(new ApprovalDecision(project.Id, true));
//        example.ManagerThreeApproveProject(new ApprovalDecision(project.Id, true));
//    }



//    private static async Task TestSubWorkflowCall()
//    {
//        await RegisterWorkflow(typeof(ProjectApprovalExample), nameof(ProjectApprovalExample.SubWorkflowTest));
//        var example = new ProjectApprovalExample();
//        var project = ProjectApprovalExample.GetCurrentProject();
//        await example.ProjectSubmitted(project);
//        example.ManagerOneApproveProject(new ApprovalDecision(project.Id, true));
//        example.ManagerTwoApproveProject(new ApprovalDecision(project.Id, true));
//        example.ManagerThreeApproveProject(new ApprovalDecision(project.Id, true));
//    }
//    private static async Task TestReplayGoBackToWithNewMatch()
//    {
//        await RegisterWorkflow(typeof(ReplayGoBackToExample), nameof(ReplayGoBackToExample.TestReplay_GoBackToNewMatch));
//        var example = new ReplayGoBackToExample();
//        var project = ProjectApprovalExample.GetCurrentProject();
//        await example.ProjectSubmitted(project);
//        example.ManagerOneApproveProject(new ApprovalDecision(project.Id, false));
//        project.IsResubmit = true;
//        await example.ProjectSubmitted(project);
//        example.ManagerOneApproveProject(new ApprovalDecision(project.Id, true));
//    }

//    private static async Task TestReplayGoBackTo()
//    {
//        //await RegisterWorkflow(typeof(ReplayGoBackToExample), nameof(ReplayGoBackToExample.TestReplay_GoBackTo));
//        await RegisterWorkflow(typeof(ReplayGoBackToExample), nameof(ReplayGoBackToExample.TestReplay_GoBackToGroup));
//        var example = new ReplayGoBackToExample();
//        var project = ProjectApprovalExample.GetCurrentProject();
//        await example.ProjectSubmitted(project);
//        example.ManagerOneApproveProject(new ApprovalDecision(project.Id, false));
//        example.ManagerOneApproveProject(new ApprovalDecision(project.Id, true));
//    }
//    private static async Task TestReplayGoBackAfter()
//    {
//        await RegisterWorkflow(typeof(ReplayGoBackAfterExample), nameof(ReplayGoBackAfterExample.TestReplay_GoBackAfter));
//        var example = new ReplayGoBackAfterExample();
//        var project = ProjectApprovalExample.GetCurrentProject();
//        await example.ProjectSubmitted(project);
//        example.ManagerOneApproveProject(new ApprovalDecision(project.Id, false));
//        example.ManagerOneApproveProject(new ApprovalDecision(project.Id, true));
//    }

//    private static async Task TestReplayGoBackBeforeNewMatch()
//    {
//        await RegisterWorkflow(typeof(ReplayGoBackBeforeNewMatchExample), nameof(ReplayGoBackBeforeNewMatchExample.TestReplay_GoBackBefore));
//        var example = new ReplayGoBackBeforeNewMatchExample();
//        var project = ProjectApprovalExample.GetCurrentProject();
//        await example.ProjectSubmitted(project);
//        example.ManagerOneApproveProject(new ApprovalDecision(project.Id, false));
//        project.Name += "-Updated";
//        await example.ProjectSubmitted(project);
//        example.ManagerOneApproveProject(new ApprovalDecision(project.Id, true));
//    }

//    private static async Task RegisterWorkflow(Type classType, string methodName)
//    {
//        //var method =
//        //    classType.GetMethod(methodName, BindingFlags.DeclaredOnly | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
//        //if (method == null)
//        //{
//        //    Console.WriteLine($"No method with name [{methodName}] in class [{classType.FullName}].");
//        //    return;
//        //}
//        //await _scanner.RegisterWorkflow(method, MethodType.WorkflowEntryPoint);
//        //await _scanner._context.SaveChangesAsync();
//        //await _scanner.RegisterWorkflowFirstWait(method);
//        //await _scanner._context.SaveChangesAsync();
//    }
//}