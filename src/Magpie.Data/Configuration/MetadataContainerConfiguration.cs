using Magpie.Models.Data;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Magpie.Data.Configuration;

internal sealed class MetadataContainerConfiguration : IEntityTypeConfiguration<MetadataContainer>
{
    public void Configure(EntityTypeBuilder<MetadataContainer> builder)
    {
        builder.ToTable("metadata_container");
        builder.HasMany<Tag>()
            .WithMany(e => e.MetadataContainers)
            .UsingEntity(j => j.ToTable("metadata_containerTag"));
    }
}
