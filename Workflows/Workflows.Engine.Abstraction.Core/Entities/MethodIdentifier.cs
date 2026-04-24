namespace Workflows.Handler.InOuts.Entities
{
    public class MethodIdentifier : BasicMethodIdentifier
    {
        public MethodsGroup MethodGroup { get; internal set; }
        public int MethodGroupId { get; internal set; }
        //public List<MethodWaitEntity> Waits { get; internal set; }
    }

}
