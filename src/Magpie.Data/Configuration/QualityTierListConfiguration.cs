using System.Collections.Generic;
using System.Text.Json;

using Magpie.Models.Data;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Magpie.Data.Configuration;

internal sealed class QualityTierListConfiguration : IEntityTypeConfiguration<QualityTierList>
{
    public void Configure(EntityTypeBuilder<QualityTierList> builder)
    {
        builder.ToTable("quality_tierList");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedOnAdd();
        builder.Property(e => e.Rules).HasConversion(
            v => JsonSerializer.Serialize(v, JsonSerializerOptions.Default),
            v => JsonSerializer.Deserialize<List<QualityTierRule>>(v, JsonSerializerOptions.Default) ?? new())
            .HasDefaultValueSql("'[]'")
            .UseListComparer();
        builder.HasMany<Tag>()
            .WithMany(e => e.QualityTierLists)
            .UsingEntity(j => j.ToTable("quality_tierListTag"));
    }
}
