using Magpie.Models.Data;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Magpie.Data.Configuration;

internal sealed class DiagnosticFindingConfiguration : IEntityTypeConfiguration<DiagnosticFinding>
{
    public void Configure(EntityTypeBuilder<DiagnosticFinding> builder)
    {
        builder.ToTable("diagnostic_finding");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.DiagnosticCheckId).IsRequired().HasMaxLength(255);
        builder.Property(e => e.Title).IsRequired().HasMaxLength(512);
        builder.Property(e => e.AffectedEntity).HasMaxLength(512);
        builder.Property(e => e.RepairId).HasMaxLength(255);
        builder.HasIndex(e => e.DiagnosticCheckId);
        builder.HasIndex(e => e.Severity);
    }
}
