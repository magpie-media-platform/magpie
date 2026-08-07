using Magpie.Models.Data;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Magpie.Data.Configuration;

internal sealed class ExtensionNodeStatusConfiguration : IEntityTypeConfiguration<ExtensionNodeStatus>
{
    public void Configure(EntityTypeBuilder<ExtensionNodeStatus> builder)
    {
        builder.ToTable("extension_nodeStatus");
        builder.HasKey(e => e.NodeId);
        builder.Property(e => e.NodeId).IsRequired();
        builder.Property(e => e.AppliedGeneration).IsRequired();
        builder.Property(e => e.ExtensionHealth).IsRequired();
        builder.Property(e => e.SchedulerEligible).IsRequired();
        builder.Property(e => e.LastSeenAt).IsRequired();
        builder.HasIndex(e => e.SchedulerEligible);
        builder.HasIndex(e => e.LastSeenAt);
    }
}
