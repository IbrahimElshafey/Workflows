using Workflows.Handler.Attributes;
using Workflows.Handler.BaseUse;
namespace TestApi1.Examples;

public class WaitManyWorkflowsExample : ProjectApprovalExample
{
    public async IAsyncEnumerable<Wait> WaitManyWorkflows()
    {
        await Task.Delay(10);
        WriteMessage("SubWorkflowTest WaitManyWorkflows");
        yield return
            WaitMethod<Project, bool>(ProjectSubmitted, "Project Submitted")
                .MatchIf((input, output) => output == true)
                .AfterMatch((input, output) => CurrentProject = input);
        WriteMessage("After project submitted.");
        yield return WaitGroup(new[] {

            WaitSubWorkflow(WaitManagerOneAndTwoSubWorkflow()),
            WaitSubWorkflow(ManagerThreeSubWorkflow()),
        }, "Wait multiple resumable workflows");
        Success(nameof(WaitManyWorkflows));
    }

    public async IAsyncEnumerable<Wait> WaitSubWorkflowTwoLevels()
    {
        await Task.Delay(10);
        WriteMessage("SubWorkflowTest WaitSubWorkflowTwoLevels");
        yield return
            WaitMethod<Project, bool>(ProjectSubmitted, "Project Submitted")
                .MatchIf((input, output) => output == true)
                .AfterMatch((input, output) => CurrentProject = input);
        WriteMessage("After project submitted.");
        yield return WaitGroup(new[]
        {
            WaitSubWorkflow(ManagerThreeSubWorkflow()),
            WaitSubWorkflow(ManagerOneCallSubManagerTwo()),
        }, "Wait multiple resumable workflows");
        WriteMessage("{3}After wait multiple resumable workflows");
        Success(nameof(WaitSubWorkflowTwoLevels));
    }


    public async IAsyncEnumerable<Wait> WaitFirstWorkflow()
    {
        await Task.Delay(10);
        WriteMessage("SubWorkflowTest WaitManyWorkflows");
        yield return
            WaitMethod<Project, bool>(ProjectSubmitted, "Project Submitted")
                .MatchIf((input, output) => output == true)
                .AfterMatch((input, output) => CurrentProject = input);
        WriteMessage("After project submitted.");
        yield return
            WaitGroup(new[]
            {
                WaitSubWorkflow(WaitManagerOneAndTwoSubWorkflow()),
                WaitSubWorkflow(ManagerThreeSubWorkflow()),
            }, "Wait multiple resumable workflows")
                .MatchAny();
        WriteMessage("After wait two workflows.");
        Success(nameof(WaitFirstWorkflow));
    }

    [SubWorkflow("WaitManyWorkflowsExample.WaitManagerOneAndTwoSubWorkflow")]
    internal async IAsyncEnumerable<Wait> WaitManagerOneAndTwoSubWorkflow()
    {
        await Task.Delay(10);
        WriteMessage("WaitTwoManagers started");
        yield return WaitGroup(
            new Wait[]
            {
                WaitMethod<ApprovalDecision, bool>(ManagerOneApproveProject)
                    .MatchIf((input, output) => input.ProjectId == CurrentProject.Id)
                    .AfterMatch((input, output) => ManagerOneApproval = output),
                WaitMethod<ApprovalDecision, bool>(ManagerTwoApproveProject)
                    .MatchIf((input, output) => input.ProjectId == CurrentProject.Id)
                    .AfterMatch((input, output) => ManagerTwoApproval = output)
            }
,
            "Wait two methods").MatchAll();
        WriteMessage("Two waits matched");
    }

    [SubWorkflow("WaitManyWorkflowsExample.ManagerThreeSubWorkflow")]
    internal async IAsyncEnumerable<Wait> ManagerThreeSubWorkflow()
    {
        WriteMessage("Start ManagerThreeSubWorkflow");
        await Task.Delay(10);
        yield return
            WaitMethod<ApprovalDecision, bool>(ManagerThreeApproveProject, "Manager Three Approve Project")
                .MatchIf((input, output) => input.ProjectId == CurrentProject.Id)
                .AfterMatch((input, output) => ManagerThreeApproval = output);
        WriteMessage("{2}End ManagerThreeSubWorkflow");
    }

    [SubWorkflow("WaitManyWorkflowsExample.ManagerOneCallSubManagerTwo")]
    internal async IAsyncEnumerable<Wait> ManagerOneCallSubManagerTwo()
    {
        WriteMessage("Start ManagerOneCallSubManagerTwo");
        await Task.Delay(10);
        yield return
            WaitMethod<ApprovalDecision, bool>(ManagerOneApproveProject, "Manager One Approve Project")
                .MatchIf((input, output) => input.ProjectId == CurrentProject.Id)
                .AfterMatch((input, output) => ManagerOneApproval = output);
        yield return WaitSubWorkflow(ManagerTwoSub("123456"), "Wait Sub Workflow ManagerTwoSub");
        WriteMessage("{1}End ManagerOneCallSubManagerTwo");
    }

    [SubWorkflow("WaitManyWorkflowsExample.ManagerTwoSub")]
    internal async IAsyncEnumerable<Wait> ManagerTwoSub(string workflowInput)
    {
        WriteMessage("Start ManagerTwoSub");
        await Task.Delay(10);
        yield return
            WaitMethod<ApprovalDecision, bool>(ManagerTwoApproveProject, "Manager Two Approve Project1")
                .MatchIf((input, output) => input.ProjectId == CurrentProject.Id)
                .AfterMatch((input, output) =>
                {
                    ManagerTwoApproval = output;
                    if (workflowInput != "123456")
                        throw new Exception("Workflow input must be 123456");
                    workflowInput = "789";
                });
        yield return
            WaitMethod<ApprovalDecision, bool>(ManagerTwoApproveProject, "Manager Two Approve Project2")
                .MatchIf((input, output) => input.ProjectId == CurrentProject.Id)
                .AfterMatch((input, output) => ManagerTwoApproval = output);
        if (workflowInput != "789")
            throw new Exception("Workflow input must be 789");
        WriteMessage("{0}End ManagerTwoSub");
    }
}