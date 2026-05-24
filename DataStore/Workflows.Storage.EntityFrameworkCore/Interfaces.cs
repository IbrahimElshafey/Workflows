using System;

namespace Workflows.Storage.EntityFrameworkCore
{
    public interface IEntity
    {
        DateTime Created { get; set; }
    }

    public interface IEntity<TId> : IEntity
    {
        TId Id { get; set; }
    }

    public interface IEntityWithUpdate
    {
        DateTime? Modified { get; set; }
        string ConcurrencyToken { get; set; }
    }

    public interface IEntityWithDelete
    {
        bool IsDeleted { get; set; }
    }
}
