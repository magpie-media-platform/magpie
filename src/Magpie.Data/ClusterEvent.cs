using System;

namespace Magpie.Data;

internal sealed record ClusterEvent
{
    public long Seq { get; set; }

    public required string Payload { get; set; }

    public DateTime PublishedAt { get; set; }
}
