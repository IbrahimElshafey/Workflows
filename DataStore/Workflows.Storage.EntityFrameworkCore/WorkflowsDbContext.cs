using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using Workflows.Abstraction.DTOs;

namespace Workflows.Storage.EntityFrameworkCore
{
    public class WorkflowsDbContext : DbContext
    {
        public DbSet<WorkflowInstance> WorkflowInstances { get; set; }
        public DbSet<WorkflowWait> WorkflowWaits { get; set; }
        public DbSet<SignalWait> SignalWaits { get; set; }
        public DbSet<CommandWait> CommandWaits { get; set; }
        public DbSet<WorkflowDefinitionEntity> WorkflowDefinitions { get; set; }
        public DbSet<SignalDefinitionEntity> SignalDefinitions { get; set; }
        public DbSet<CommandDefinitionEntity> CommandDefinitions { get; set; }
        public DbSet<TemplateCacheEntity> TemplateCache { get; set; }

        public WorkflowsDbContext(DbContextOptions<WorkflowsDbContext> options) : base(options)
        {
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // WorkflowInstance configuration
            modelBuilder.Entity<WorkflowInstance>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.ConcurrencyToken).IsConcurrencyToken();

                entity.OwnsOne(x => x.StateObject, cb =>
                {
                    cb.ToJson();

                    cb.Property(p => p.Instance)
                      .HasConversion(
                          v => JsonConvert.SerializeObject(v, new JsonSerializerSettings { TypeNameHandling = TypeNameHandling.All }),
                          v => JsonConvert.DeserializeObject(v, new JsonSerializerSettings { TypeNameHandling = TypeNameHandling.All })
                      );

                    cb.Property(p => p.StateMachinesObjects)
                      .HasConversion(
                          v => JsonConvert.SerializeObject(v, new JsonSerializerSettings { TypeNameHandling = TypeNameHandling.All }),
                          v => JsonConvert.DeserializeObject<Dictionary<string, object>>(v, new JsonSerializerSettings { TypeNameHandling = TypeNameHandling.All }) ?? new Dictionary<string, object>()
                      );

                    cb.Property(p => p.WaitStatesObjects)
                      .HasConversion(
                          v => JsonConvert.SerializeObject(v, new JsonSerializerSettings { TypeNameHandling = TypeNameHandling.All }),
                          v => JsonConvert.DeserializeObject<Dictionary<Guid, object>>(v, new JsonSerializerSettings { TypeNameHandling = TypeNameHandling.All }) ?? new Dictionary<Guid, object>()
                      );
                });

                entity.Property(e => e.CancellationHistory)
                      .HasConversion(
                          v => JsonConvert.SerializeObject(v, Formatting.None),
                          v => JsonConvert.DeserializeObject<List<CancellationHistoryEntry>>(v) ?? new List<CancellationHistoryEntry>()
                      );
            });

            // WorkflowWait inheritance and configurations
            modelBuilder.Entity<WorkflowWait>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.HasIndex(e => e.WorkflowInstanceId);

                // Setup TPH discriminator mapping
                entity.HasDiscriminator<int>("WaitDiscriminator")
                      .HasValue<WorkflowWait>(0)
                      .HasValue<SignalWait>(1)
                      .HasValue<CommandWait>(2);

                // Foreign key to WorkflowInstance
                entity.HasOne<WorkflowInstance>()
                      .WithMany()
                      .HasForeignKey(e => e.WorkflowInstanceId)
                      .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<SignalWait>(entity =>
            {
                entity.HasIndex(e => e.SignalPath);
            });

            modelBuilder.Entity<CommandWait>(entity =>
            {
                entity.HasIndex(e => e.CommandWaitId);
            });

            // Setup Global Query Filter for soft delete on the root type
            modelBuilder.Entity<WorkflowWait>()
                        .HasQueryFilter(w => !w.IsDeleted);

            // Definitions configurations
            modelBuilder.Entity<WorkflowDefinitionEntity>(entity =>
            {
                entity.HasKey(e => new { e.WorkflowName, e.Version });
            });

            modelBuilder.Entity<SignalDefinitionEntity>(entity =>
            {
                entity.HasKey(e => e.SignalIdentifier);
            });

            modelBuilder.Entity<CommandDefinitionEntity>(entity =>
            {
                entity.HasKey(e => e.CommandName);
            });

            modelBuilder.Entity<TemplateCacheEntity>(entity =>
            {
                entity.HasKey(e => e.TemplateHashKey);
            });
        }

        protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
        {
            configurationBuilder.Properties<DateTime>()
                .HaveConversion<DateTimeUtcConverter>();

            configurationBuilder.Properties<DateTime?>()
                .HaveConversion<NullableDateTimeUtcConverter>();
        }
    }

    public class DateTimeUtcConverter : ValueConverter<DateTime, DateTime>
    {
        public DateTimeUtcConverter() : base(
            v => DateTime.SpecifyKind(v, DateTimeKind.Utc),
            v => DateTime.SpecifyKind(v, DateTimeKind.Utc))
        {
        }
    }

    public class NullableDateTimeUtcConverter : ValueConverter<DateTime?, DateTime?>
    {
        public NullableDateTimeUtcConverter() : base(
            v => v.HasValue ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) : null,
            v => v.HasValue ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) : null)
        {
        }
    }
}
