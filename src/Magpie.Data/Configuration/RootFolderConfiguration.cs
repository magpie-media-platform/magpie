using System.Collections.Generic;
using System.Text.Json;

using Magpie.Models.Data;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Magpie.Data.Configuration;

internal sealed class RootFolderConfiguration : IEntityTypeConfiguration<RootFolder>
{
    public void Configure(EntityTypeBuilder<RootFolder> builder)
    {
        builder.ToTable("system_rootFolder");
        builder.HasIndex(e => e.ContentType);
        builder.HasIndex(e => new { e.ContentType, e.IsDefault });
        builder.HasIndex(e => e.Path).IsUnique();
        builder.Property(e => e.Defaults).HasConversion(
            value => JsonSerializer.Serialize(value, JsonSerializerOptions.Default),
            value => JsonSerializer.Deserialize<Dictionary<string, RootFolderDefault>>(
                value,
                JsonSerializerOptions.Default) ?? new())
            .HasDefaultValueSql("'{}'")
            .UseJsonComparer();
    }
}
