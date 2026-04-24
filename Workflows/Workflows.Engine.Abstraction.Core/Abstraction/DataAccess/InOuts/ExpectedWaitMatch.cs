using System.Linq.Expressions;
namespace Workflows.Handler.Abstraction.DataAccess.InOuts
{
    public class ExpectedWaitMatch
    {
        public ExpectedWaitMatch(
            long WaitId,
            int TemplateId,
            int RequestedByWorkflowId,
            int WorkflowInstanceId,
            int? MethodToWaitId,
            long? ClosureDataId,
            long? LocalsId,
            LambdaExpression CallMandatoryPartExpression,
            string MandatoryPart,
            bool IsFirst)
        {
            this.WaitId = WaitId;
            this.TemplateId = TemplateId;
            this.RequestedByWorkflowId = RequestedByWorkflowId;
            this.WorkflowInstanceId = WorkflowInstanceId;
            this.MethodToWaitId = MethodToWaitId;
            this.ClosureDataId = ClosureDataId;
            this.LocalsId = LocalsId;
            this.CallMandatoryPartExpression = CallMandatoryPartExpression;
            this.MandatoryPart = MandatoryPart;
            this.IsFirst = IsFirst;
        }

        public long WaitId { get; }
        public int TemplateId { get; }
        public int RequestedByWorkflowId { get; }
        public int WorkflowInstanceId { get; }
        public int? MethodToWaitId { get; }
        public long? ClosureDataId { get; }
        public long? LocalsId { get; }
        public LambdaExpression CallMandatoryPartExpression { get; }
        public string MandatoryPart { get; }
        public bool IsFirst { get; }    }
}
