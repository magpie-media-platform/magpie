using System.Text.Json;

using Magpie.Models.Data;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Magpie.Data.Configuration;

internal sealed class EventTriggerConfiguration : IEntityTypeConfiguration<EventTrigger>
{
    public void Configure(EntityTypeBuilder<EventTrigger> builder)
    {
        builder.ToTable("job_eventTrigger");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedOnAdd();
        builder.Property(e => e.EventFilter).HasConversion(
            v => JsonSerializer.Serialize(v, JsonSerializerOptions.Default),
            v => JsonSerializer.Deserialize<EventFilter>(v, JsonSerializerOptions.Default) ?? new())
            .UseJsonComparer();
        builder.Property(e => e.Parameters).UseStringDictionaryConversion();

        builder.Property(e => e.DelaySeconds).HasDefaultValue(0);

        builder.HasIndex(e => e.Enabled);
    }
}
