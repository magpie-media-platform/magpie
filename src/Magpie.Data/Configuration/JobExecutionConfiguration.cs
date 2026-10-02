using Magpie.Models.Data;
using Magpie.Models.Enums;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Magpie.Data.Configuration;

internal sealed class JobExecutionConfiguration : IEntityTypeConfiguration<JobExecution>
{
    private static readonly string InFlightJobLockPartitionFilter =
        $"\"Status\" IN ({(int)JobExecutionStatus.Queued}, {(int)JobExecutionStatus.Running}, " +
        $"{(int)JobExecutionStatus.CancellationRequested})";

    private static readonly string RunningJobLockPartitionFilter =
        $"\"Status\" IN ({(int)JobExecutionStatus.Running}, {(int)JobExecutionStatus.CancellationRequested}) " +
        "AND \"LockPartition\" IS NOT NULL";

    public void Configure(EntityTypeBuilder<JobExecution> builder)
    {
        builder.ToTable("job_execution");
        builder.Property(e => e.Parameters).UseStringDictionaryConversion();
        builder.Property(e => e.TriggerContext).UseStringDictionaryConversion();
        builder.HasOne<ScheduleTrigger>()
            .WithMany()
            .HasForeignKey(e => e.ScheduleTriggerId)
            .OnDelete(DeleteBehavior.SetNull);
        builder.HasOne<EventTrigger>()
            .WithMany()
            .HasForeignKey(e => e.EventTriggerId)
            .OnDelete(DeleteBehavior.SetNull);
        builder.Property(e => e.LockPartition).HasMaxLength(256);
        builder.Property(e => e.DedupKey).HasMaxLength(256);
        builder.HasIndex(e => new { e.CreatedAt, e.Id }).IsDescending(true, false);
        builder.HasIndex(e => new { e.JobType, e.CreatedAt, e.Id }).IsDescending(false, true, false);
        builder.HasIndex(e => new { e.Status, e.CreatedAt, e.Id }).IsDescending(false, true, false);
        builder.HasIndex(e => new { e.JobType, e.Status, e.CreatedAt, e.Id })
            .IsDescending(false, false, true, false);
        builder.HasIndex(e => e.StartedAt);
        builder.HasIndex(e => e.LastHeartbeat);
        builder.HasIndex(e => e.NotBeforeAt);
        builder.HasIndex(e => e.CorrelationId);
        builder.HasIndex(e => new { e.ParentExecutionId, e.CreatedAt });
        builder.HasIndex(e => e.LockPartition, "IX_job_execution_LockPartition_All");
        builder.HasIndex(e => e.LockPartition, "IX_job_execution_LockPartition")
            .HasFilter(InFlightJobLockPartitionFilter);
        builder.HasIndex(e => e.LockPartition, "UX_job_execution_LockPartition_Running")
            .IsUnique()
            .HasFilter(RunningJobLockPartitionFilter);
        builder.HasIndex(e => e.DedupKey, "UX_job_execution_DedupKey")
            .IsUnique()
            .HasFilter("\"DedupKey\" IS NOT NULL");

        foreach (var (property, _, _) in JobExecutionReferenceColumns.All)
        {
            builder.Property<string>(property)
                .HasMaxLength(JobExecutionReferenceColumns.MaxLength)
                .ValueGeneratedOnAddOrUpdate();
            builder.HasIndex(property);
        }
    }
}
