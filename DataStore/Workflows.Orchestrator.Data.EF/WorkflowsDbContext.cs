using Microsoft.EntityFrameworkCore;

namespace Workflows.Orchestrator.Data.EF
{
    public class WorkflowsDbContext : DbContext
    {
        public DbSet<DbWorkflowState> WorkflowStates { get; set; }
        public DbSet<DbWaitRecord> WaitRecords { get; set; }
        public DbSet<DbWorkflowDefinition> WorkflowDefinitions { get; set; }
        public DbSet<DbSignalDefinition> SignalDefinitions { get; set; }
        public DbSet<DbCommandDefinition> CommandDefinitions { get; set; }

        public WorkflowsDbContext(DbContextOptions<WorkflowsDbContext> options) : base(options)
        {
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // DbWorkflowState configuration
            modelBuilder.Entity<DbWorkflowState>(entity =>
            {
                entity.HasKey(e => e.Id);
            });

            // DbWaitRecord configuration
            modelBuilder.Entity<DbWaitRecord>(entity =>
            {
                entity.HasKey(e => e.Id);
                
                // Foreign key with Cascade Delete
                entity.HasOne<DbWorkflowState>()
                    .WithMany()
                    .HasForeignKey(e => e.WorkflowInstanceId)
                    .OnDelete(DeleteBehavior.Cascade);

                // Indexes for fast lookup
                entity.HasIndex(e => e.SignalPath);
                entity.HasIndex(e => e.CommandWaitId);
                entity.HasIndex(e => e.WorkflowInstanceId);
            });

            // DbWorkflowDefinition configuration
            modelBuilder.Entity<DbWorkflowDefinition>(entity =>
            {
                entity.HasKey(e => new { e.WorkflowName, e.Version });
            });

            // DbSignalDefinition configuration
            modelBuilder.Entity<DbSignalDefinition>(entity =>
            {
                entity.HasKey(e => e.SignalIdentifier);
            });

            // DbCommandDefinition configuration
            modelBuilder.Entity<DbCommandDefinition>(entity =>
            {
                entity.HasKey(e => e.CommandName);
            });
        }
    }
}
