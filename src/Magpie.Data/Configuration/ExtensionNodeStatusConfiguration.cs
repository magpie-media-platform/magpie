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
        builder.HasIndex(e => e.SchedulerEligible);
        builder.HasIndex(e => e.LastSeenAt);
    }
}
