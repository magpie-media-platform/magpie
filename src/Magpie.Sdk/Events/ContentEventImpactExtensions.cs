using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Magpie.Sdk.Events;

/// <summary>
/// Extension methods that convert a content event impact to event context.
/// </summary>
public static class ContentEventImpactExtensions
{
    /// <summary>
    /// Writes a content event impact into an event context.
    /// </summary>
    /// <param name="impact">The content event impact to write</param>
    /// <param name="context">The event context that receives the impact values</param>
    public static void WriteTo(this ContentEventImpact impact, IDictionary<string, string> context)
    {
        if (impact is null)
        {
            throw new ArgumentNullException(nameof(impact));
        }

        if (context is null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        WriteId(context, MagpieEvents.Keys.ContentHierarchyItemId, impact.HierarchyItemId);
        WriteId(context, MagpieEvents.Keys.ContentItemId, impact.ItemId);
        WriteId(context, MagpieEvents.Keys.ContentImpactMetadataContainerId, impact.MetadataContainerId);
        WriteId(context, MagpieEvents.Keys.ContentImpactParentId, impact.ParentId);
        WriteIds(
            context,
            MagpieEvents.Keys.ContentImpactAncestorIds,
            impact.AncestorIds);
        WriteId(context, MagpieEvents.Keys.ContentImpactRootId, impact.RootId);
        WriteText(context, MagpieEvents.Keys.ContentImpactContentType, impact.ContentType);
        WriteText(context, MagpieEvents.Keys.ContentImpactLevel, impact.Level);
        WriteFlag(context, MagpieEvents.Keys.ContentImpactTopLevel, impact.TopLevel);
        WriteId(context, MagpieEvents.Keys.ContentImpactRootFolderId, impact.RootFolderId);
        WriteId(context, MagpieEvents.Keys.ContentImpactQualityTierListId, impact.QualityTierListId);
        WriteFlag(context, MagpieEvents.Keys.ContentImpactMonitoring, impact.Monitoring);
        WriteText(context, MagpieEvents.Keys.ContentImpactFulfillment, impact.Fulfillment);
        WriteId(context, MagpieEvents.Keys.ContentImpactWishlistId, impact.WishlistId);
        WriteIds(
            context,
            MagpieEvents.Keys.ContentImpactSubtreeIds,
            impact.SubtreeIds);
        WriteIds(
            context,
            MagpieEvents.Keys.ContentImpactBatchIds,
            impact.BatchIds);
        WriteId(
            context,
            MagpieEvents.Keys.ContentImpactMetadataSourceContainerId,
            impact.MetadataSourceContainerId);
        WriteIds(
            context,
            MagpieEvents.Keys.ContentImpactMetadataAddedContainerIds,
            impact.MetadataAddedContainerIds);
        WriteIds(
            context,
            MagpieEvents.Keys.ContentImpactMetadataRemovedContainerIds,
            impact.MetadataRemovedContainerIds);

        if (impact.Previous is not { } previous)
        {
            return;
        }

        WriteId(context, MagpieEvents.Keys.ContentImpactPreviousParentId, previous.ParentId);
        WriteIds(
            context,
            MagpieEvents.Keys.ContentImpactPreviousAncestorIds,
            previous.AncestorIds);
        WriteId(context, MagpieEvents.Keys.ContentImpactPreviousRootId, previous.RootId);
        WriteText(context, MagpieEvents.Keys.ContentImpactPreviousContentType, previous.ContentType);
        WriteText(context, MagpieEvents.Keys.ContentImpactPreviousLevel, previous.Level);
        WriteFlag(context, MagpieEvents.Keys.ContentImpactPreviousTopLevel, previous.TopLevel);
        WriteId(context, MagpieEvents.Keys.ContentImpactPreviousRootFolderId, previous.RootFolderId);
        WriteId(
            context,
            MagpieEvents.Keys.ContentImpactPreviousQualityTierListId,
            previous.QualityTierListId);
        WriteFlag(context, MagpieEvents.Keys.ContentImpactPreviousMonitoring, previous.Monitoring);
        WriteText(context, MagpieEvents.Keys.ContentImpactPreviousFulfillment, previous.Fulfillment);
    }

    /// <summary>
    /// Creates an event context that holds a content event impact.
    /// </summary>
    /// <param name="impact">The content event impact to convert</param>
    /// <returns>The event context.</returns>
    public static Dictionary<string, string> ToContext(this ContentEventImpact impact)
    {
        if (impact is null)
        {
            throw new ArgumentNullException(nameof(impact));
        }

        var context = new Dictionary<string, string>(StringComparer.Ordinal);
        impact.WriteTo(context);
        return context;
    }

    private static void WriteId(IDictionary<string, string> context, string key, Guid? value)
    {
        if (value is { } id && id != Guid.Empty)
        {
            context[key] = id.ToString("D", CultureInfo.InvariantCulture);
        }
    }

    private static void WriteText(IDictionary<string, string> context, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            context[key] = value;
        }
    }

    private static void WriteFlag(IDictionary<string, string> context, string key, bool? value)
    {
        if (value is { } flag)
        {
            context[key] = flag ? "true" : "false";
        }
    }

    private static void WriteIds(IDictionary<string, string> context, string key, IReadOnlyList<Guid> values)
    {
        var ids = Distinct(values);
        if (ids.Length > 0)
        {
            context[key] = string.Join(",", ids);
        }
    }

    private static string[] Distinct(IReadOnlyList<Guid> values)
    {
        return [.. values
            .Where(id => id != Guid.Empty)
            .Distinct()
            .Select(id => id.ToString("D", CultureInfo.InvariantCulture))];
    }
}
