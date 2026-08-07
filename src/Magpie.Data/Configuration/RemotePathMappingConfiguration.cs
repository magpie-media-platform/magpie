using Magpie.Models.Data;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Magpie.Data.Configuration;

internal sealed class RemotePathMappingConfiguration : IEntityTypeConfiguration<RemotePathMapping>
{
    public void Configure(EntityTypeBuilder<RemotePathMapping> builder)
    {
        builder.ToTable("download_remotePathMapping");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedOnAdd();
        builder.HasIndex(e => e.Host);
    }
}
