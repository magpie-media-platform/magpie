using Magpie.Data.Configuration;
using Magpie.Models.Data;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Magpie.Data.SQLite;

internal sealed class SqliteModelCustomizer(ModelCustomizerDependencies dependencies) :
    RelationalModelCustomizer(dependencies)
{
    public override void Customize(ModelBuilder modelBuilder, DbContext context)
    {
        base.Customize(modelBuilder, context);
        modelBuilder.Entity<ContentHierarchyItem>().HasIndex(e => new { e.ParentId, e.EffectiveMonitoring });
        modelBuilder.Entity<ReleaseIdentityKey>()
            .HasIndex(e => new { e.Authority, e.Value })
            .IsUnique()
            .HasFilter("\"ReleaseId\" IS NOT NULL");
        foreach (var (property, source, key) in JobExecutionReferenceColumns.All)
        {
            modelBuilder.Entity<JobExecution>()
                .Property<string>(property)
                .HasComputedColumnSql(
                    $"lower(substr(json_extract(\"{source}\", '$.\"{key}\"'), 1, " +
                    $"{JobExecutionReferenceColumns.MaxLength}))",
                    stored: false);
        }
    }
}
