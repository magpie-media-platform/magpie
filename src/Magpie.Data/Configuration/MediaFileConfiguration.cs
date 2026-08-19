using System.Text.Json;

using Magpie.Models.Data;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Magpie.Data.Configuration;

internal sealed class MediaFileConfiguration : IEntityTypeConfiguration<MediaFile>
{
    public void Configure(EntityTypeBuilder<MediaFile> builder)
    {
        builder.ToTable("content_mediaFile");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedOnAdd();
        builder.Property(e => e.MediaInfo).HasConversion(
            v => JsonSerializer.Serialize(v, JsonSerializerOptions.Default),
            v => JsonSerializer.Deserialize<MediaInfo>(v, JsonSerializerOptions.Default) ?? new())
            .UseJsonComparer();
        builder.HasOne<ContentItem>()
            .WithMany(e => e.MediaFiles)
            .HasForeignKey(e => e.ContentItemId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(e => e.ContentItemId);
        builder.HasIndex(e => e.FilePath).IsUnique();
        builder.HasIndex(e => e.PathKey).IsUnique();
    }
}
