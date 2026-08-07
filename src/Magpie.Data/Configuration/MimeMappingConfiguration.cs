using Magpie.Models.Data;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Magpie.Data.Configuration;

internal sealed class MimeMappingConfiguration : IEntityTypeConfiguration<MimeMapping>
{
    public void Configure(EntityTypeBuilder<MimeMapping> builder)
    {
        builder.ToTable("system_mimeMapping");
        builder.HasKey(e => e.Extension);
        builder.Property(e => e.Extension).IsRequired();
        builder.Property(e => e.MimeType).IsRequired();
    }
}
