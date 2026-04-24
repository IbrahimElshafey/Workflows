namespace Workflows.Handler.InOuts.Entities.EntityBehaviour
{
    public interface IEntity<IdType> : IEntity
    {
        IdType Id { get; }
    }
}
