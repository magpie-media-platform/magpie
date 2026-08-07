using System;
using System.Collections.Generic;
using System.Globalization;

using Magpie.Models;
using Magpie.Sdk.Events;

namespace Magpie.Core.Events;

internal static class DatabaseEventBuilder
{
    public static MagpieEvent BuildBackupCompletedEvent(string backupId, string path, long sizeBytes)
    {
        var context = new Dictionary<string, string>
        {
            [MagpieEvents.Keys.BackupId] = backupId ?? string.Empty,
            [MagpieEvents.Keys.BackupPath] = path ?? string.Empty,
            [MagpieEvents.Keys.BackupSizeBytes] = sizeBytes.ToString(CultureInfo.InvariantCulture),
        };

        return new(Guid.Empty, MagpieEvents.Names.BackupCompleted, context, default, default, null);
    }

    public static MagpieEvent BuildBackupDeletedEvent(string backupId)
    {
        var context = new Dictionary<string, string>
        {
            [MagpieEvents.Keys.BackupId] = backupId ?? string.Empty,
        };

        return new(Guid.Empty, MagpieEvents.Names.BackupDeleted, context, default, default, null);
    }

    public static MagpieEvent BuildBackupFailedEvent(string? backupId, string failureReason)
    {
        var context = new Dictionary<string, string>
        {
            [MagpieEvents.Keys.BackupFailureReason] = failureReason ?? string.Empty,
        };

        if (!string.IsNullOrEmpty(backupId))
        {
            context[MagpieEvents.Keys.BackupId] = backupId;
        }

        return new(Guid.Empty, MagpieEvents.Names.BackupFailed, context, default, default, null);
    }
}
