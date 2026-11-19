using System;

namespace Workflows.Storage.EntityFrameworkCore
{
    public interface IEntity
    {
        DateTime Created { get; set; }
    }
}
