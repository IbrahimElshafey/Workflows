using Workflows.Handler.Attributes;

namespace TestApi1.Examples
{
    internal interface IManagerFiveApproval
    {
        [EmitSignal("IManagerFiveApproval.ManagerFiveApproveProject", FromExternal = true)]
        bool FiveApproveProject(ApprovalDecision args);
    }
}