using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Workflows.Storage.EntityFrameworkCore
{
    public class WorkflowAuditingInterceptor : SaveChangesInterceptor
    {
        public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
        {
            AuditEntities(eventData.Context);
            return base.SavingChanges(eventData, result);
        }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            AuditEntities(eventData.Context);
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }

        private void AuditEntities(DbContext? context)
        {
            if (context == null) return;

            foreach (var entry in context.ChangeTracker.Entries())
            {
                // 1. Handle Creation
                if (entry.State == EntityState.Added && entry.Entity is IEntity entity)
                {
                    entry.Property(nameof(IEntity.Created)).CurrentValue = DateTime.UtcNow;
                }

                // 2. Handle Updates & Concurrency
                if (entry.State == EntityState.Modified && entry.Entity is IEntityWithUpdate updateEntity)
                {
                    entry.Property(nameof(IEntityWithUpdate.Modified)).CurrentValue = DateTime.UtcNow;
                    entry.Property(nameof(IEntityWithUpdate.ConcurrencyToken)).CurrentValue = Guid.NewGuid().ToString();
                }

                // 3. Handle Soft Deletes
                if (entry.State == EntityState.Deleted && entry.Entity is IEntityWithDelete deleteEntity)
                {
                    entry.State = EntityState.Modified; // Change state to prevent hard delete
                    entry.Property(nameof(IEntityWithDelete.IsDeleted)).CurrentValue = true;
                }
            }
        }
    }
}
