using Microsoft.EntityFrameworkCore;
using Workflows.Orchestrator.Data.EF.Entities;

namespace Workflows.Orchestrator.Data.EF
{
    public class WorkflowsDbContext : DbContext
    {
        public WorkflowsDbContext(DbContextOptions<WorkflowsDbContext> options) : base(options)
        {
        }

        public DbSet<WorkflowStateEntity> WorkflowStates { get; set; } = null!;
        public DbSet<WaitEntity> Waits { get; set; } = null!;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Explicitly set EF constraints if needed
            modelBuilder.Entity<WaitEntity>()
                .HasIndex(w => w.SignalIdentifier);
        }
    }
}
