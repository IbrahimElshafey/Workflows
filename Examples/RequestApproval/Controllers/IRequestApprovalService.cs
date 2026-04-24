using Workflows.Handler.Attributes;

namespace RequestApproval.Controllers
{
    public interface IRequestApprovalService
    {
        [EmitSignal("RequestApproval.UserSubmitRequest")]
        bool UserSubmitRequest(Request request);
        int AskManagerApproval(int requestId);

        [EmitSignal("RequestApproval.ManagerApproval")]
        int ManagerApproval(ApproveRequestArgs input);
        void InformUserAboutAccept(int id);
        void InformUserAboutReject(int id);
        void AskUserForMoreInfo(int id, string message);
    }
}