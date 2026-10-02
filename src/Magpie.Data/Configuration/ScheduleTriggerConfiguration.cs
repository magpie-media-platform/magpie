using Magpie.Models.Data;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Magpie.Data.Configuration;

internal sealed class ScheduleTriggerConfiguration : IEntityTypeConfiguration<ScheduleTrigger>
{
    public void Configure(EntityTypeBuilder<ScheduleTrigger> builder)
    {
        builder.ToTable("job_scheduleTrigger");
        builder.Property(e => e.Parameters).UseStringDictionaryConversion();
        builder.HasIndex(e => e.Enabled);
        builder.HasIndex(e => e.NextRunAt);
    }
}
