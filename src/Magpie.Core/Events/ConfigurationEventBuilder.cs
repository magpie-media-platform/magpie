using System;
using System.Collections.Generic;
using System.Globalization;

using Magpie.Models;
using Magpie.Sdk.Events;

namespace Magpie.Core.Events;

internal static class ConfigurationEventBuilder
{
    public static MagpieEvent BuildBlockedFileTypeCreatedEvent(Guid blockedFileTypeId)
    {
        var context = new Dictionary<string, string>
        {
            [MagpieEvents.Keys.BlockedFileTypeId] = blockedFileTypeId.ToString("D", CultureInfo.InvariantCulture),
        };

        return new(Guid.Empty, MagpieEvents.Names.BlockedFileTypeCreated, context, default, default, null);
    }

    public static MagpieEvent BuildBlockedFileTypeDeletedEvent(Guid blockedFileTypeId)
    {
        var context = new Dictionary<string, string>
        {
            [MagpieEvents.Keys.BlockedFileTypeId] = blockedFileTypeId.ToString("D", CultureInfo.InvariantCulture),
        };

        return new(Guid.Empty, MagpieEvents.Names.BlockedFileTypeDeleted, context, default, default, null);
    }

    public static MagpieEvent BuildContentFilterCreatedEvent(Guid contentFilterId)
    {
        var context = new Dictionary<string, string>
        {
            [MagpieEvents.Keys.ContentFilterId] = contentFilterId.ToString("D", CultureInfo.InvariantCulture),
        };

        return new(Guid.Empty, MagpieEvents.Names.ContentFilterCreated, context, default, default, null);
    }

    public static MagpieEvent BuildContentFilterDeletedEvent(Guid contentFilterId)
    {
        var context = new Dictionary<string, string>
        {
            [MagpieEvents.Keys.ContentFilterId] = contentFilterId.ToString("D", CultureInfo.InvariantCulture),
        };

        return new(Guid.Empty, MagpieEvents.Names.ContentFilterDeleted, context, default, default, null);
    }

    public static MagpieEvent BuildContentFilterUpdatedEvent(
        Guid contentFilterId,
        IReadOnlyCollection<string> changedFields)
    {
        var context = new Dictionary<string, string>
        {
            [MagpieEvents.Keys.ContentFilterId] = contentFilterId.ToString("D", CultureInfo.InvariantCulture),
            [MagpieEvents.Keys.ContentFilterChangedFields] = string.Join(",", changedFields ?? []),
        };

        return new(Guid.Empty, MagpieEvents.Names.ContentFilterUpdated, context, default, default, null);
    }

    public static MagpieEvent BuildQualityTierListCreatedEvent(Guid qualityTierListId)
    {
        var context = new Dictionary<string, string>
        {
            [MagpieEvents.Keys.QualityTierListId] = qualityTierListId.ToString("D", CultureInfo.InvariantCulture),
        };

        return new(Guid.Empty, MagpieEvents.Names.QualityTierListCreated, context, default, default, null);
    }

    public static MagpieEvent BuildQualityTierListDeletedEvent(Guid qualityTierListId)
    {
        var context = new Dictionary<string, string>
        {
            [MagpieEvents.Keys.QualityTierListId] = qualityTierListId.ToString("D", CultureInfo.InvariantCulture),
        };

        return new(Guid.Empty, MagpieEvents.Names.QualityTierListDeleted, context, default, default, null);
    }

    public static MagpieEvent BuildQualityTierListUpdatedEvent(
        Guid qualityTierListId,
        IReadOnlyCollection<string> changedFields)
    {
        var context = new Dictionary<string, string>
        {
            [MagpieEvents.Keys.QualityTierListId] = qualityTierListId.ToString("D", CultureInfo.InvariantCulture),
            [MagpieEvents.Keys.QualityTierListChangedFields] = string.Join(",", changedFields ?? []),
        };

        return new(Guid.Empty, MagpieEvents.Names.QualityTierListUpdated, context, default, default, null);
    }

    public static MagpieEvent BuildSettingsGroupUpdatedEvent(
        Guid? extensionId,
        string name,
        IReadOnlyCollection<string> changedFields)
    {
        var context = new Dictionary<string, string>
        {
            [MagpieEvents.Keys.SettingsGroupName] = name ?? string.Empty,
            [MagpieEvents.Keys.SettingsGroupChangedFields] = string.Join(",", changedFields ?? []),
        };

        if (extensionId is { } id)
        {
            context[MagpieEvents.Keys.SettingsGroupExtensionId] = id.ToString("D", CultureInfo.InvariantCulture);
        }

        return new(Guid.Empty, MagpieEvents.Names.SettingsGroupUpdated, context, default, default, null);
    }
}
