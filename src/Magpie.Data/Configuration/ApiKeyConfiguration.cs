using Magpie.Models.Data;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Magpie.Data.Configuration;

internal sealed class ApiKeyConfiguration : IEntityTypeConfiguration<ApiKey>
{
    public void Configure(EntityTypeBuilder<ApiKey> builder)
    {
        builder.ToTable("auth_apiKey");
        builder.HasOne<User>()
            .WithMany(e => e.ApiKeys)
            .HasForeignKey(e => e.UserId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(e => e.KeyPrefix);
        builder.HasIndex(e => e.Enabled);
    }
}
