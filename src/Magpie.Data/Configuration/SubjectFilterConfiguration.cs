using System.Text.Json;

using Magpie.Models.Data;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Magpie.Data.Configuration;

internal sealed class SubjectFilterConfiguration : IEntityTypeConfiguration<SubjectFilter>
{
    public void Configure(EntityTypeBuilder<SubjectFilter> builder)
    {
        builder.ToTable("filter_subject");
        builder.Property(e => e.Criteria).HasConversion(
            v => JsonSerializer.Serialize(v, JsonSerializerOptions.Default),
            v => JsonSerializer.Deserialize<SubjectFilterCriteria>(v, JsonSerializerOptions.Default) ?? new())
            .UseJsonComparer();
        builder.HasMany<Tag>()
            .WithMany(e => e.SubjectFilters)
            .UsingEntity(j => j.ToTable("filter_subjectTag"));
    }
}
