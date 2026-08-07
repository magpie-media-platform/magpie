using System.Text.Json;

using Magpie.Models.Data;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Magpie.Data.Configuration;

internal sealed class ContentFilterConfiguration : IEntityTypeConfiguration<ContentFilter>
{
    public void Configure(EntityTypeBuilder<ContentFilter> builder)
    {
        builder.ToTable("filter_content");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedOnAdd();
        builder.Property(e => e.Criteria).HasConversion(
            v => JsonSerializer.Serialize(v, JsonSerializerOptions.Default),
            v => JsonSerializer.Deserialize<ContentFilterCriteria>(v, JsonSerializerOptions.Default) ?? new())
            .UseJsonComparer();
        builder.HasMany<Tag>()
            .WithMany(e => e.ContentFilters)
            .UsingEntity(j => j.ToTable("filter_contentTag"));
    }
}
