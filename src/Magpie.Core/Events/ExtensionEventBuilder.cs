using System;
using System.Collections.Generic;
using System.Globalization;

using Magpie.Models;
using Magpie.Sdk.Events;

namespace Magpie.Core.Events;

internal static class ExtensionEventBuilder
{
    public static MagpieEvent BuildExtensionActivationChangedEvent(long generation)
    {
        var context = new Dictionary<string, string>
        {
            ["extension.generation"] = generation.ToString(CultureInfo.InvariantCulture),
        };

        return new(Guid.Empty, "extension.activation.changed", context, default, default, null);
    }

    public static MagpieEvent BuildExtensionDisabledEvent(Guid extensionId, string reason)
    {
        var context = new Dictionary<string, string>
        {
            [MagpieEvents.Keys.ExtensionId] = extensionId.ToString("D", CultureInfo.InvariantCulture),
            [MagpieEvents.Keys.ExtensionReason] = reason ?? string.Empty,
        };

        return new(Guid.Empty, MagpieEvents.Names.ExtensionDisabled, context, default, default, null);
    }

    public static MagpieEvent BuildExtensionEnabledEvent(Guid extensionId)
    {
        var context = new Dictionary<string, string>
        {
            [MagpieEvents.Keys.ExtensionId] = extensionId.ToString("D", CultureInfo.InvariantCulture),
        };

        return new(Guid.Empty, MagpieEvents.Names.ExtensionEnabled, context, default, default, null);
    }

    public static MagpieEvent BuildExtensionInstalledEvent(Guid extensionId, string version)
    {
        var context = new Dictionary<string, string>
        {
            [MagpieEvents.Keys.ExtensionId] = extensionId.ToString("D", CultureInfo.InvariantCulture),
            [MagpieEvents.Keys.ExtensionVersion] = version ?? string.Empty,
        };

        return new(Guid.Empty, MagpieEvents.Names.ExtensionInstalled, context, default, default, null);
    }

    public static MagpieEvent BuildExtensionPackageSourceCreatedEvent(Guid extensionPackageSourceId)
    {
        var context = new Dictionary<string, string>
        {
            [MagpieEvents.Keys.ExtensionPackageSourceId] =
                extensionPackageSourceId.ToString("D", CultureInfo.InvariantCulture),
        };

        return new(Guid.Empty, MagpieEvents.Names.ExtensionPackageSourceCreated, context, default, default, null);
    }

    public static MagpieEvent BuildExtensionPackageSourceDeletedEvent(Guid extensionPackageSourceId)
    {
        var context = new Dictionary<string, string>
        {
            [MagpieEvents.Keys.ExtensionPackageSourceId] =
                extensionPackageSourceId.ToString("D", CultureInfo.InvariantCulture),
        };

        return new(Guid.Empty, MagpieEvents.Names.ExtensionPackageSourceDeleted, context, default, default, null);
    }

    public static MagpieEvent BuildExtensionPackageSourceUpdatedEvent(
        Guid extensionPackageSourceId,
        IReadOnlyCollection<string> changedFields)
    {
        var context = new Dictionary<string, string>
        {
            [MagpieEvents.Keys.ExtensionPackageSourceId] =
                extensionPackageSourceId.ToString("D", CultureInfo.InvariantCulture),
            [MagpieEvents.Keys.ExtensionPackageSourceChangedFields] = string.Join(",", changedFields ?? []),
        };

        return new(Guid.Empty, MagpieEvents.Names.ExtensionPackageSourceUpdated, context, default, default, null);
    }

    public static MagpieEvent BuildExtensionUninstalledEvent(Guid extensionId, string? assemblyName, string? filePath)
    {
        var context = new Dictionary<string, string>
        {
            [MagpieEvents.Keys.ExtensionId] = extensionId.ToString("D", CultureInfo.InvariantCulture),
            [MagpieEvents.Keys.ExtensionAssemblyName] = assemblyName ?? string.Empty,
            [MagpieEvents.Keys.ExtensionFilePath] = filePath ?? string.Empty,
        };

        return new(Guid.Empty, MagpieEvents.Names.ExtensionUninstalled, context, default, default, null);
    }

    public static MagpieEvent BuildExtensionUpgradedEvent(Guid extensionId, string fromVersion, string toVersion)
    {
        var context = new Dictionary<string, string>
        {
            [MagpieEvents.Keys.ExtensionId] = extensionId.ToString("D", CultureInfo.InvariantCulture),
            [MagpieEvents.Keys.ExtensionFromVersion] = fromVersion ?? string.Empty,
            [MagpieEvents.Keys.ExtensionToVersion] = toVersion ?? string.Empty,
        };

        return new(Guid.Empty, MagpieEvents.Names.ExtensionUpgraded, context, default, default, null);
    }
}
