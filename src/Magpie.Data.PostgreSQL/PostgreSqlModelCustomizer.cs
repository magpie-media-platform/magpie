using Magpie.Data.Configuration;
using Magpie.Models.Data;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Magpie.Data.PostgreSQL;

internal sealed class PostgreSqlModelCustomizer(ModelCustomizerDependencies dependencies) :
    RelationalModelCustomizer(dependencies)
{
    public override void Customize(ModelBuilder modelBuilder, DbContext context)
    {
        base.Customize(modelBuilder, context);
        modelBuilder.Entity<ClusterEvent>(builder =>
        {
            builder.ToTable("system_clusterEvent");
            builder.HasKey(e => e.Seq);
            builder.Property(e => e.Seq).ValueGeneratedOnAdd();
            builder.Property(e => e.Payload).IsRequired();
            builder.HasIndex(e => e.PublishedAt);
        });
        modelBuilder.Entity<ReleaseIdentityKey>()
            .HasIndex(e => new { e.Authority, e.Value })
            .IsUnique()
            .HasFilter("\"ReleaseId\" IS NOT NULL");
        foreach (var (property, source, key) in JobExecutionReferenceColumns.All)
        {
            modelBuilder.Entity<JobExecution>()
                .Property<string>(property)
                .HasComputedColumnSql(
                    $"lower(left((\"{source}\"::jsonb ->> '{key}'), " +
                    $"{JobExecutionReferenceColumns.MaxLength}))",
                    stored: true);
        }
    }
}
