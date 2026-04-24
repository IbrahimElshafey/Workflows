using Workflows.Handler;
using Workflows.Handler.Attributes;
using Workflows.Handler.BaseUse;

namespace TestApi1.Examples;

public class ProjectApprovalExample : WorkflowContainer, IManagerFiveApproval
{
    public Project CurrentProject { get; set; }
    public bool ManagerOneApproval { get; set; }
    public bool ManagerTwoApproval { get; set; }
    public bool ManagerThreeApproval { get; set; }
    public bool ManagerFourApproval { get; set; }
    public bool ManagerFiveApproval { get; set; }
    public string ExternalMethodStatus { get; set; } = "Not matched yet.";

    [Workflow("ProjectApprovalExample.ProjectApprovalFlow", isActive: true)]//Point 1
    public async IAsyncEnumerable<Wait> ProjectApprovalFlow()
    {
        var x = Random.Shared.Next();
    Project_Submitted:
        yield return
          WaitMethod<Project, bool>(ProjectSubmitted, "Project Submitted")//Point 2
             .MatchIf(CurrentProject == null, (project, output) => output && !project.IsResubmit)//Point 3
             .MatchIf(CurrentProject != null, (project, output) => output && !project.IsResubmit)
             .AfterMatch((project, output) => CurrentProject = project);//Point 4
        AddInfoLog("###After Project Submitted");
        //throw new NotImplementedException("Exception after first wait match.");
        await AskManagerToApprove("Manager One", CurrentProject.Id);
        //throw new Exception("Critical exception aftrer AskManagerToApprove");
        yield return
               WaitMethod<ApprovalDecision, bool>(ManagerOneApproveProject, "Manager One Approve Project")
                   .MatchIf((approvalDecision, output) => approvalDecision.ProjectId == CurrentProject.Id)
                   .AfterMatch((approvalDecision, approvalResult) => ManagerOneApproval = approvalResult);

        if (ManagerOneApproval is false)
        {
            AddInfoLog("Go back and ask applicant to resubmitt project.");
            await AskApplicantToResubmittProject(CurrentProject.Id);
            goto Project_Submitted;
        }
        else
        {
            WriteMessage("Project approved");
            await InfromApplicantAboutApproval(CurrentProject.Id);
        }
        Success(nameof(ProjectApprovalFlow));

    }

    private Task InfromApplicantAboutApproval(int id)
    {
        return Task.CompletedTask;
    }

    private Task AskApplicantToResubmittProject(int id)
    {
        return Task.CompletedTask;
    }

    [Workflow("ProjectApprovalExample.ExternalMethod")]
    public async IAsyncEnumerable<Wait> ExternalMethod()
    {
        var x = Random.Shared.Next();
        await Task.Delay(1);
        yield return WaitMethod<string, string>
                (new ExternalServiceClass().SayHelloExport, "Wait say hello external")
                .MatchIf((userName, helloMsg) => userName.StartsWith("M"))
                .AfterMatch((userName, helloMsg) => ExternalMethodStatus = $"Say hello called and user name is: {userName}");

        yield return
              WaitMethod<object, int>(new ExternalServiceClass().ExternalMethodTest, "Wait external method 1")
                  .MatchIf((input, output) => output % 2 == 0)
                  .AfterMatch((input, output) => ExternalMethodStatus = "ExternalMethodTest Matched.");

        yield return
          WaitMethod<string, int>(new ExternalServiceClass().ExternalMethodTest2, "Wait external method 2")
              .MatchIf((input, output) => input == "Ibrahim")
              .AfterMatch((input, output) => ExternalMethodStatus = "ExternalMethodTest2 Matched.");

        Success(nameof(ExternalMethod));
    }

    [Workflow("ProjectApprovalExample.ExternalMethodWaitGoodby")]
    public async IAsyncEnumerable<Wait> ExternalMethodWaitGoodby()
    {
        var x = Random.Shared.Next();
        await Task.Delay(1);
        yield return WaitMethod<string, string>
                (new ExternalServiceClass().SayGoodby, "Wait good by external")
                .MatchIf((userName, helloMsg) => userName[0] == 'M')
                .AfterMatch((userName, helloMsg) => ExternalMethodStatus = $"Say goodby called and user name is: {userName}");
        Success(nameof(ExternalMethodWaitGoodby));
    }
    //any method with attribute [WorkflowEntryPoint] that takes no argument
    //and return IAsyncEnumerable<Wait> is a resumbale workflow
    [Workflow("PAE.InterfaceMethod")]
    public async IAsyncEnumerable<Wait> InterfaceMethod()
    {
        var x = Random.Shared.Next();
        yield return
         WaitMethod<Project, bool>(ProjectSubmitted, "Project Submitted")
             .MatchIf((input, output) => output == true)
             .AfterMatch((input, output) => CurrentProject = input);

        yield return
               WaitMethod<ApprovalDecision, bool>(FiveApproveProject, "Manager Five Approve Project")
                   .MatchIf((input, output) => input.ProjectId == CurrentProject.Id)
                   .AfterMatch((input, output) => ManagerFiveApproval = output);
        Success(nameof(InterfaceMethod));
    }
    public async IAsyncEnumerable<Wait> SubWorkflowTest()
    {
        yield return
            WaitMethod<Project, bool>(ProjectSubmitted, "Project Submitted")
                .MatchIf((input, output) => output == true)
                .AfterMatch((input, output) => CurrentProject = input);

        await AskManagerToApprove("Manager 1", CurrentProject.Id);
        WriteMessage("Wait sub workflow");
        yield return WaitSubWorkflow(WaitTwoManagers(), "Wait sub workflow that waits two manager approval.");
        WriteMessage("After sub workflow ended");
        if (ManagerOneApproval && ManagerTwoApproval)
        {
            WriteMessage("Manager 1 & 2 approved the project");
            yield return
                WaitMethod<ApprovalDecision, bool>(ManagerThreeApproveProject, "Manager Three Approve Project")
                    .MatchIf((input, output) => input.ProjectId == CurrentProject.Id)
                    .AfterMatch((input, output) => ManagerThreeApproval = output);

            WriteMessage(ManagerThreeApproval ? "Project Approved" : "Project Rejected");
        }
        else
        {
            WriteMessage("Project rejected by one of managers 1 & 2");
        }
        Success(nameof(SubWorkflowTest));
    }

    [SubWorkflow("ProjectApprovalExample.WaitTwoManagers")]
    public async IAsyncEnumerable<Wait> WaitTwoManagers()
    {
        WriteMessage("WaitTwoManagers started");
        yield return WaitGroup(
            new[]
            {
            WaitMethod<ApprovalDecision, bool>(ManagerOneApproveProject, "Manager One Approve Project")
                .MatchIf((input, output) => input.ProjectId == CurrentProject.Id)
                .AfterMatch((input, output) => ManagerOneApproval = output),
            WaitMethod<ApprovalDecision, bool>(ManagerTwoApproveProject, "Manager Two Approve Project")
                .MatchIf((input, output) => input.ProjectId == CurrentProject.Id)
                .AfterMatch((input, output) => ManagerTwoApproval = output)
            }
,
            "Wait two methods").MatchAll();
        WriteMessage("Two waits matched");
    }


    //[WorkflowEntryPoint]
    public async IAsyncEnumerable<Wait> WaitFirst()
    {
        var x = Random.Shared.Next();
        WriteMessage("First started");
        yield return WaitGroup(
            new Wait[]
            {
                WaitMethod<Project, bool>(ProjectSubmitted, "Project Submitted")
                    .MatchIf((input, output) => output == true)
                    .AfterMatch((input, output) => CurrentProject = input),
                WaitMethod<ApprovalDecision, bool>(ManagerOneApproveProject, "Manager One Approve Project")
                    .MatchIf((input, output) => input.ProjectId == CurrentProject.Id)
                    .AfterMatch((input, output) => ManagerOneApproval = output)
            }
,
            "Wait first in two").MatchAny();
        WriteMessage("One of two waits matched");
    }

    [EmitSignal("ProjectApprovalExample.PrivateMethod")]
    internal bool PrivateMethod(Project project)
    {
        WriteMessage("Project Submitted");
        return true;
    }

    [EmitSignal("ProjectApprovalExample.ProjectSubmitted")]
    internal async Task<bool> ProjectSubmitted(Project project)
    {
        //await Task.Delay(100);
        WriteAction($"Project {project} Submitted ");
        return true;
    }

    [EmitSignal("ProjectApprovalExample.ManagerOneApproveProject")]
    public bool ManagerOneApproveProject(ApprovalDecision args)
    {
        WriteAction($"Manager One Approve Project with decision ({args.Decision})");
        return args.Decision;
    }

    [EmitSignal("ProjectApprovalExample.ManagerTwoApproveProject")]
    public bool ManagerTwoApproveProject(ApprovalDecision args)
    {
        WriteAction($"Manager Two Approve Project with decision ({args.Decision})");
        return args.Decision;
    }

    [EmitSignal("ProjectApprovalExample.ManagerThreeApproveProject")]
    public bool ManagerThreeApproveProject(ApprovalDecision args)
    {
        WriteAction($"Manager Three Approve Project with decision ({args.Decision})");
        return args.Decision;
    }


    [EmitSignal("ProjectApprovalExample.ManagerFourApproveProject")]
    public bool ManagerFourApproveProject(ApprovalDecision args)
    {
        WriteAction($"Manager Four Approve Project with decision ({args.Decision})");
        return args.Decision;
    }

    public async Task<bool> AskManagerToApprove(string manager, int projectId)
    {
        await Task.Delay(10);
        WriteAction($"Ask Manager [{manager}] to Approve Project that has id [{projectId}]");
        return true;
    }

    public static Project GetCurrentProject()
    {
        return new Project { Id = Random.Shared.Next(1, int.MaxValue), Name = "Project Name", Description = "Description" };
    }
    protected void Success(string msg)
    {
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"^^^Success for [{msg}]^^^^");
        Console.ResetColor();
    }
    protected void WriteMessage(string msg)
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"{msg} -{CurrentProject?.Id}");
        Console.ForegroundColor = ConsoleColor.White;
    }

    protected void WriteAction(string msg)
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"{msg} -{CurrentProject?.Id}");
        Console.ForegroundColor = ConsoleColor.White;
    }

    [EmitSignal("IManagerFiveApproval.ManagerFiveApproveProject", FromExternal = true)]
    public bool FiveApproveProject(ApprovalDecision args)
    {
        WriteAction($"Manager Four Approve Project with decision ({args.Decision})");
        return args.Decision;
    }
}

