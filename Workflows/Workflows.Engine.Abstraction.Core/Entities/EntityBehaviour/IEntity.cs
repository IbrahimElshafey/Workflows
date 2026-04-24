using System;
namespace Workflows.Handler.InOuts.Entities.EntityBehaviour
{
    public interface IEntity
    {
        DateTime Created { get; }
        int? ServiceId { get; }
    }
}
