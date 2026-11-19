using System;

namespace Workflows.Storage.EntityFrameworkCore
{
    public interface IEntityWithUpdate
    {
        DateTime? Modified { get; set; }
        string ConcurrencyToken { get; set; }
    }
}
