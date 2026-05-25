using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using Workflows.Abstraction.DTOs;
using Workflows.Abstraction.DTOs.Waits;

namespace Workflows.Storage.EntityFrameworkCore
{
    public class WorkflowsDbContext : DbContext
    {
        public DbSet<WorkflowInstance> WorkflowInstances { get; set; }
        // Note: WorkflowWaits (base table) is intentionally omitted — TPC strategy
        // means each concrete type maps to its own complete table.
        public DbSet<SignalWaitEntity> SignalWaits { get; set; }
        public DbSet<CommandWaitEntity> CommandWaits { get; set; }
        public DbSet<TimeWaitEntity> TimeWaits { get; set; }
        public DbSet<WorkflowDefinitionEntity> WorkflowDefinitions { get; set; }
        public DbSet<SignalDefinitionEntity> SignalDefinitions { get; set; }
        public DbSet<CommandDefinitionEntity> CommandDefinitions { get; set; }
        public DbSet<TemplateCacheEntity> TemplateCache { get; set; }

        public WorkflowsDbContext(DbContextOptions<WorkflowsDbContext> options) : base(options)
        {
        }

        private static readonly JsonSerializerSettings PolymorphicSerializerSettings = new JsonSerializerSettings
        {
            TypeNameHandling = TypeNameHandling.All,
            NullValueHandling = NullValueHandling.Ignore,
            Formatting = Formatting.None,
            ConstructorHandling = ConstructorHandling.AllowNonPublicDefaultConstructor,
            ObjectCreationHandling = ObjectCreationHandling.Replace,
            ContractResolver = new PrivateSetterContractResolver(),
            PreserveReferencesHandling = PreserveReferencesHandling.Objects,
            Converters = { new Newtonsoft.Json.Converters.StringEnumConverter() }
        };

        private class PrivateSetterContractResolver : Newtonsoft.Json.Serialization.DefaultContractResolver
        {
            protected override Newtonsoft.Json.Serialization.JsonProperty CreateProperty(
                System.Reflection.MemberInfo member, 
                Newtonsoft.Json.MemberSerialization memberSerialization)
            {
                var prop = base.CreateProperty(member, memberSerialization);
                if (!prop.Writable)
                {
                    var property = member as System.Reflection.PropertyInfo;
                    if (property != null)
                    {
                        var hasPrivateSetter = property.GetSetMethod(true) != null;
                        prop.Writable = hasPrivateSetter;
                    }
                }
                return prop;
            }
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            var cancellationHistoryComparer = new ValueComparer<List<CancellationHistoryEntry>>(
                (c1, c2) => JsonConvert.SerializeObject(c1, Formatting.None) == JsonConvert.SerializeObject(c2, Formatting.None),
                c => c == null ? 0 : JsonConvert.SerializeObject(c, Formatting.None).GetHashCode(),
                c => JsonConvert.DeserializeObject<List<CancellationHistoryEntry>>(JsonConvert.SerializeObject(c, Formatting.None)) ?? new List<CancellationHistoryEntry>()
            );

            var waitsComparer = new ValueComparer<List<WaitInfrastructureDto>>(
                (c1, c2) => JsonConvert.SerializeObject(c1, PolymorphicSerializerSettings) == JsonConvert.SerializeObject(c2, PolymorphicSerializerSettings),
                c => c == null ? 0 : JsonConvert.SerializeObject(c, PolymorphicSerializerSettings).GetHashCode(),
                c => JsonConvert.DeserializeObject<List<WaitInfrastructureDto>>(JsonConvert.SerializeObject(c, PolymorphicSerializerSettings), PolymorphicSerializerSettings) ?? new List<WaitInfrastructureDto>()
            );

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
                          v => JsonConvert.SerializeObject(v, PolymorphicSerializerSettings),
                          v => JsonConvert.DeserializeObject(v, PolymorphicSerializerSettings)
                      );

                    cb.Property(p => p.StateMachinesObjects)
                      .HasConversion(
                          v => JsonConvert.SerializeObject(v, PolymorphicSerializerSettings),
                          v => JsonConvert.DeserializeObject<Dictionary<string, object>>(v, PolymorphicSerializerSettings) ?? new Dictionary<string, object>()
                      );

                    cb.Property(p => p.WaitStatesObjects)
                      .HasConversion(
                          v => JsonConvert.SerializeObject(v, PolymorphicSerializerSettings),
                          v => JsonConvert.DeserializeObject<Dictionary<Guid, object>>(v, PolymorphicSerializerSettings) ?? new Dictionary<Guid, object>()
                      );
                });

                entity.Property(e => e.CancellationHistory)
                      .HasConversion(
                          v => JsonConvert.SerializeObject(v, Formatting.None),
                          v => JsonConvert.DeserializeObject<List<CancellationHistoryEntry>>(v) ?? new List<CancellationHistoryEntry>(),
                          cancellationHistoryComparer
                      );

                entity.Property(e => e.Waits)
                      .HasConversion(
                          v => JsonConvert.SerializeObject(v, PolymorphicSerializerSettings),
                          v => JsonConvert.DeserializeObject<List<WaitInfrastructureDto>>(v, PolymorphicSerializerSettings) ?? new List<WaitInfrastructureDto>(),
                          waitsComparer
                      );
            });

            // WorkflowWait hierarchy — TPC (Table-Per-Concrete-Type):
            // Each concrete table contains ALL columns (Id, WorkflowInstanceId, Status, Created)
            // so Status is always available without a join.
            modelBuilder.Entity<WorkflowWaitEntity>(entity =>
            {
                entity.UseTpcMappingStrategy();
                entity.HasKey(e => e.Id);
            });

            modelBuilder.Entity<SignalWaitEntity>(entity =>
            {
                entity.ToTable("SignalWaits");
                entity.HasIndex(e => e.WorkflowInstanceId);
                // Optimized composite index for Phase-1 exact-match routing
                entity.HasIndex(e => new { e.SignalPath, e.Status, e.SignalExactMatchPaths, e.ExactMatchFilter });

                // FK to WorkflowInstance
                entity.HasOne<WorkflowInstance>()
                      .WithMany()
                      .HasForeignKey(e => e.WorkflowInstanceId)
                      .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<CommandWaitEntity>(entity =>
            {
                entity.ToTable("CommandWaits");
                entity.HasIndex(e => e.WorkflowInstanceId);
                entity.HasIndex(e => e.CommandWaitId);

                entity.HasOne<WorkflowInstance>()
                      .WithMany()
                      .HasForeignKey(e => e.WorkflowInstanceId)
                      .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<TimeWaitEntity>(entity =>
            {
                entity.ToTable("TimeWaits");
                entity.HasIndex(e => e.WorkflowInstanceId);
                // Index for scheduler polling: WHERE ExecutionTime <= NOW() AND Status = Waiting
                entity.HasIndex(e => new { e.Status, e.ExecutionTime });
                entity.HasIndex(e => e.UniqueMatchId);

                entity.HasOne<WorkflowInstance>()
                      .WithMany()
                      .HasForeignKey(e => e.WorkflowInstanceId)
                      .OnDelete(DeleteBehavior.Cascade);
            });

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
}
