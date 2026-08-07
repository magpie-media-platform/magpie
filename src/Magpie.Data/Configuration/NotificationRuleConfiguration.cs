using System.Text.Json;

using Magpie.Models.Data;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Magpie.Data.Configuration;

internal sealed class NotificationRuleConfiguration : IEntityTypeConfiguration<NotificationRule>
{
    public void Configure(EntityTypeBuilder<NotificationRule> builder)
    {
        builder.ToTable("notification_rule");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedOnAdd();
        builder.Property(e => e.Settings).UseStringDictionaryConversion();
        builder.Property(e => e.EventFilter).HasConversion(
            v => JsonSerializer.Serialize(v, JsonSerializerOptions.Default),
            v => JsonSerializer.Deserialize<EventFilter>(v, JsonSerializerOptions.Default) ?? new())
            .UseJsonComparer();
        builder.HasIndex(e => e.Enabled);
        builder.HasIndex(e => e.OwnerUserId);
        builder.HasIndex(e => e.ProviderType);
    }
}
