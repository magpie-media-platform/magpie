using System;

namespace Magpie.Models.Content;

/// <summary>
/// Patch for <see cref="ContentItem"/>.
/// </summary>
public sealed record ContentItemPatch
{
    /// <inheritdoc cref="ContentItem.Id" />
    public Guid Id { get; init; }
    /// <inheritdoc cref="ContentItem.QualityTierListId" />
    public FieldPatch<Guid?>? QualityTierListId { get; init; }
    /// <inheritdoc cref="ContentItem.Monitoring" />
    public FieldPatch<MonitoringIntent>? Monitoring { get; init; }
    /// <inheritdoc cref="ContentItem.Fulfillment" />
    public FieldPatch<FulfillmentStatus>? Fulfillment { get; init; }
    /// <inheritdoc cref="ContentItem.Tags" />
    public SetPatch<string, string>? Tags { get; init; }
}
