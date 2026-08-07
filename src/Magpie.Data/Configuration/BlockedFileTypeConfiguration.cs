using Magpie.Models.Data;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Magpie.Data.Configuration;

internal sealed class BlockedFileTypeConfiguration : IEntityTypeConfiguration<BlockedFileType>
{
    public void Configure(EntityTypeBuilder<BlockedFileType> builder)
    {
        builder.ToTable("system_blockedFileType");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedOnAdd();
        builder.Property(e => e.Pattern).IsRequired().HasMaxLength(255);
        builder.HasIndex(e => e.Pattern);
        builder.HasIndex(e => new { e.Pattern }).IsUnique();
    }
}
