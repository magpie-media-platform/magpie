using Magpie.Models.Data;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Magpie.Data.Configuration;

internal sealed class MediaFileCoverageConfiguration : IEntityTypeConfiguration<MediaFileCoverage>
{
    public void Configure(EntityTypeBuilder<MediaFileCoverage> builder)
    {
        builder.ToTable("content_mediaFileCoverage");
        builder.HasKey(e => new { e.MediaFileId, e.ContentItemId });
        builder.HasIndex(e => e.ContentItemId);
    }
}
