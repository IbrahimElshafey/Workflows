using System;

namespace Workflows.Storage.EntityFrameworkCore
{
    public interface IEntity<TId> : IEntity
    {
        TId Id { get; set; }
    }
}
