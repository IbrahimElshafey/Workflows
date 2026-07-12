using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
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
        public DbSet<CompensationWaitEntity> CompensationWaits { get; set; }
        public DbSet<ExternalChildWaitEntity> ExternalChildWaits { get; set; }
        public DbSet<WorkflowDefinitionEntity> WorkflowDefinitions { get; set; }
        public DbSet<SignalDefinitionEntity> SignalDefinitions { get; set; }
        public DbSet<CommandDefinitionEntity> CommandDefinitions { get; set; }
        public DbSet<TemplateCacheEntity> TemplateCache { get; set; }
        public DbSet<OutboxMessageEntity> OutboxMessages { get; set; }
        public DbSet<CommandResultEntity> CommandResults { get; set; }
        public DbSet<SignalInboxEntity> SignalInbox { get; set; }


        public WorkflowsDbContext(DbContextOptions<WorkflowsDbContext> options) : this(options, null)
        {
        }

        public WorkflowsDbContext(DbContextOptions<WorkflowsDbContext> options, IServiceProvider? serviceProvider) : base(options)
        {
        }

        internal static readonly JsonSerializerSettings PolymorphicSerializerSettings = new JsonSerializerSettings
        {
            TypeNameHandling = TypeNameHandling.None,
            NullValueHandling = NullValueHandling.Ignore,
            Formatting = Formatting.None,
            ConstructorHandling = ConstructorHandling.AllowNonPublicDefaultConstructor,
            ObjectCreationHandling = ObjectCreationHandling.Replace,
            ContractResolver = new PrivateSetterContractResolver(),
            PreserveReferencesHandling = PreserveReferencesHandling.Objects,
            Converters = { 
                new Newtonsoft.Json.Converters.StringEnumConverter(), 
                new ObjectIntConverter(),
                new Workflows.Shared.Serialization.WaitDtoJsonConverter(),
                new Workflows.Shared.Serialization.WorkflowStateObjectJsonConverter(),
                new Workflows.Shared.Serialization.DefinitionWaitJsonConverter()
            }
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

                prop.DefaultValueHandling = DefaultValueHandling.Ignore;

                if (prop.PropertyType != typeof(string) && typeof(System.Collections.IEnumerable).IsAssignableFrom(prop.PropertyType))
                {
                    var originalShouldSerialize = prop.ShouldSerialize;
                    prop.ShouldSerialize = instance =>
                    {
                        if (originalShouldSerialize != null && !originalShouldSerialize(instance))
                        {
                            return false;
                        }

                        var value = prop.ValueProvider?.GetValue(instance);
                        if (value == null)
                        {
                            return false;
                        }

                        if (value is System.Collections.IEnumerable enumerable)
                        {
                            var enumerator = enumerable.GetEnumerator();
                            try
                            {
                                if (!enumerator.MoveNext())
                                {
                                    return false;
                                }
                            }
                            finally
                            {
                                if (enumerator is IDisposable disposable)
                                {
                                    disposable.Dispose();
                                }
                            }
                        }

                        return true;
                    };
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

            var stateObjectComparer = new ValueComparer<WorkflowStateObject>(
                (c1, c2) => JsonConvert.SerializeObject(c1, PolymorphicSerializerSettings) == JsonConvert.SerializeObject(c2, PolymorphicSerializerSettings),
                c => c == null ? 0 : JsonConvert.SerializeObject(c, PolymorphicSerializerSettings).GetHashCode(),
                c => JsonConvert.DeserializeObject<WorkflowStateObject>(JsonConvert.SerializeObject(c, PolymorphicSerializerSettings), PolymorphicSerializerSettings) ?? new WorkflowStateObject()
            );

            // WorkflowInstance configuration
            modelBuilder.Entity<WorkflowInstance>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.ConcurrencyToken).IsConcurrencyToken();

                // Indexes for admin UI queries
                entity.HasIndex(e => e.Status);
                entity.HasIndex(e => new { e.WorkflowType, e.Status });
                entity.HasIndex(e => e.Created);
                entity.HasIndex(e => e.CompletedAt);

                entity.Property(e => e.StateObject)
                      .HasConversion(
                          v => JsonConvert.SerializeObject(v, PolymorphicSerializerSettings),
                          v => JsonConvert.DeserializeObject<WorkflowStateObject>(v, PolymorphicSerializerSettings) ?? new WorkflowStateObject(),
                          stateObjectComparer
                      );

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

            modelBuilder.Entity<CompensationWaitEntity>(entity =>
            {
                entity.ToTable("CompensationWaits");
                entity.HasIndex(e => e.WorkflowInstanceId);
                entity.HasIndex(e => e.Token);

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

            modelBuilder.Entity<ExternalChildWaitEntity>(entity =>
            {
                entity.ToTable("ExternalChildWaits");
                entity.HasKey(e => e.Id);
                entity.HasIndex(e => e.WorkflowInstanceId);
                entity.HasIndex(e => e.ParentWaitId);
                entity.HasIndex(e => new { e.SignalPath, e.Status, e.SignalExactMatchPaths, e.ExactMatchFilter });
                entity.HasIndex(e => e.CommandWaitId);
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

            modelBuilder.Entity<OutboxMessageEntity>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.HasIndex(e => e.GlobalId).IsUnique();
                entity.HasIndex(e => new { e.Status, e.CreatedAt });
            });

            modelBuilder.Entity<CommandResultEntity>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.HasIndex(e => e.GlobalId).IsUnique();
                entity.HasIndex(e => e.CommandWaitId).IsUnique();
                entity.HasIndex(e => new { e.Status, e.ReceivedAt });
            });

            modelBuilder.Entity<SignalInboxEntity>(entity =>
            {
                entity.ToTable("SignalInbox");
                entity.HasKey(e => e.MessageId);
                entity.HasIndex(e => e.WorkflowInstanceId);
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

    public class ObjectIntConverter : JsonConverter
    {
        public override bool CanConvert(Type objectType)
        {
            return objectType == typeof(object);
        }

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.Integer)
            {
                var value = reader.Value;
                if (value is long l && l >= int.MinValue && l <= int.MaxValue)
                {
                    return (int)l;
                }
                return value;
            }

            var token = JToken.Load(reader);
            return ConvertToken(token, serializer);
        }

        private object ConvertToken(JToken token, JsonSerializer serializer)
        {
            if (token == null) return null;

            switch (token.Type)
            {
                case JTokenType.Integer:
                    var val = ((JValue)token).Value;
                    if (val is long l && l >= int.MinValue && l <= int.MaxValue)
                    {
                        return (int)l;
                    }
                    return val;

                case JTokenType.Float:
                case JTokenType.String:
                case JTokenType.Boolean:
                case JTokenType.Null:
                case JTokenType.Date:
                case JTokenType.Bytes:
                case JTokenType.Guid:
                case JTokenType.Uri:
                case JTokenType.TimeSpan:
                    return ((JValue)token).Value;

                case JTokenType.Array:
                    var list = new List<object>();
                    foreach (var child in token.Children())
                    {
                        list.Add(ConvertToken(child, serializer));
                    }
                    return list;

                case JTokenType.Object:
                    var jobj = (JObject)token;
                    if (jobj.Property("$type") != null)
                    {
                        using (var subReader = jobj.CreateReader())
                        {
                            return serializer.Deserialize(subReader);
                        }
                    }
                    var dict = new Dictionary<string, object>();
                    foreach (var prop in jobj.Properties())
                    {
                        dict[prop.Name] = ConvertToken(prop.Value, serializer);
                    }
                    return dict;

                default:
                    return token.ToObject<object>(serializer);
            }
        }

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            throw new NotImplementedException();
        }

        public override bool CanWrite => false;
    }
}
