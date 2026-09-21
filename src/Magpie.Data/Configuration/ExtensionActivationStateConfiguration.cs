using Magpie.Models.Data;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Magpie.Data.Configuration;

internal sealed class ExtensionActivationStateConfiguration : IEntityTypeConfiguration<ExtensionActivationState>
{
    public void Configure(EntityTypeBuilder<ExtensionActivationState> builder)
    {
        builder.ToTable("extension_activationState");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.Generation).IsRequired();
    }
}
