using Magpie.Models.Data;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.ValueGeneration;

namespace Magpie.Data.Configuration;

internal sealed class AuditRecordConfiguration : IEntityTypeConfiguration<AuditRecord>
{
    public void Configure(EntityTypeBuilder<AuditRecord> builder)
    {
        builder.ToTable("system_audit");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasValueGenerator<SequentialGuidValueGenerator>();
        builder.HasIndex(e => e.Timestamp);
        builder.HasIndex(e => e.Category);
        builder.HasIndex(e => e.EntityType);
        builder.HasIndex(e => e.EntityId);
        builder.HasIndex(e => e.UserId);
        builder.HasIndex(e => e.CorrelationId);
        builder.HasIndex(e => e.JobExecutionId);
    }
}
