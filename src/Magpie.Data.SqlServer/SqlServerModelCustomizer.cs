using Magpie.Data.Configuration;
using Magpie.Models.Data;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Magpie.Data.SqlServer;

internal sealed class SqlServerModelCustomizer(ModelCustomizerDependencies dependencies) :
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
        var mediaFiles = modelBuilder.Entity<MediaFile>();
        mediaFiles.Property(e => e.FilePath)
            .HasColumnType("nvarchar(max)")
            .UseCollation("Latin1_General_100_BIN2");
        mediaFiles.Property(e => e.PathKey)
            .HasColumnType("nvarchar(max)")
            .UseCollation("Latin1_General_100_BIN2");
        mediaFiles.Metadata.RemoveIndex(mediaFiles.HasIndex(e => e.FilePath).Metadata);
        mediaFiles.Metadata.RemoveIndex(mediaFiles.HasIndex(e => e.PathKey).Metadata);
        mediaFiles.Property<byte[]>("FilePathHash")
            .HasComputedColumnSql("CONVERT(varbinary(32), HASHBYTES('SHA2_256', [FilePath]))", stored: true)
            .IsRequired();
        mediaFiles.Property<byte[]>("PathKeyHash")
            .HasComputedColumnSql("CONVERT(varbinary(32), HASHBYTES('SHA2_256', [PathKey]))", stored: true)
            .IsRequired();
        mediaFiles.HasIndex("FilePathHash").IsUnique();
        mediaFiles.HasIndex("PathKeyHash").IsUnique();
        var candidates = modelBuilder.Entity<LibraryImportCandidate>();

        candidates.Metadata.RemoveIndex(candidates.HasIndex(e => e.GroupKey).Metadata);
        candidates.Property(e => e.GroupKey)
            .HasColumnType("nvarchar(max)")
            .UseCollation("Latin1_General_100_BIN2");
        modelBuilder.Entity<Download>()
            .HasOne(e => e.ReleaseSource)
            .WithMany(e => e.Downloads)
            .HasForeignKey(e => e.ReleaseSourceId)
            .OnDelete(DeleteBehavior.NoAction);
        modelBuilder.Entity<ReleaseIdentityKey>()
            .HasIndex(e => new { e.Authority, e.Value })
            .IsUnique()
            .HasFilter("[ReleaseId] IS NOT NULL");
        foreach (var (property, source, key) in JobExecutionReferenceColumns.All)
        {
            modelBuilder.Entity<JobExecution>()
                .Property<string>(property)
                .HasComputedColumnSql(
                    $"LOWER(CONVERT(nvarchar({JobExecutionReferenceColumns.MaxLength}), " +
                    $"JSON_VALUE([{source}], '$.\"{key}\"')))",
                    stored: true);
        }
    }
}
