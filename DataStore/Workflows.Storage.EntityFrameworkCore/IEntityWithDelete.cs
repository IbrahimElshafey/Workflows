using System;

namespace Workflows.Storage.EntityFrameworkCore
{
    public interface IEntityWithDelete
    {
        bool IsDeleted { get; set; }
    }
}
