using System.Collections.Generic;
using System.Text.Json;

using Magpie.Models.Data;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Magpie.Data.Configuration;

internal sealed class QualityTierConfiguration : IEntityTypeConfiguration<QualityTier>
{
    public void Configure(EntityTypeBuilder<QualityTier> builder)
    {
        builder.ToTable("quality_tier");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedOnAdd();
        builder.Property(e => e.Criteria).HasConversion(
            v => JsonSerializer.Serialize(v, JsonSerializerOptions.Default),
            v => JsonSerializer.Deserialize<QualityTierCriteria>(v, JsonSerializerOptions.Default) ?? new())
            .UseJsonComparer();
        builder.Property(e => e.Rules).HasConversion(
            v => JsonSerializer.Serialize(v, JsonSerializerOptions.Default),
            v => JsonSerializer.Deserialize<List<QualityTierRule>>(v, JsonSerializerOptions.Default) ?? new())
            .HasDefaultValueSql("'[]'")
            .UseListComparer();
    }
}
