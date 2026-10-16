using System;
using System.Collections.Generic;
using System.Globalization;

using Magpie.Models;
using Magpie.Sdk.Events;

namespace Magpie.Core.Events;

internal static class SecurityEventBuilder
{
    public static MagpieEvent BuildApiKeyCreatedEvent(Guid apiKeyId, Guid userId)
    {
        var context = new Dictionary<string, string>
        {
            [MagpieEvents.Keys.ApiKeyId] = apiKeyId.ToString("D", CultureInfo.InvariantCulture),
            [MagpieEvents.Keys.UserId] = userId.ToString("D", CultureInfo.InvariantCulture),
        };

        return new(Guid.Empty, MagpieEvents.Names.ApiKeyCreated, context, default, default, null);
    }

    public static MagpieEvent BuildApiKeyDeletedEvent(Guid apiKeyId, Guid userId)
    {
        var context = new Dictionary<string, string>
        {
            [MagpieEvents.Keys.ApiKeyId] = apiKeyId.ToString("D", CultureInfo.InvariantCulture),
            [MagpieEvents.Keys.UserId] = userId.ToString("D", CultureInfo.InvariantCulture),
        };

        return new(Guid.Empty, MagpieEvents.Names.ApiKeyDeleted, context, default, default, null);
    }

    public static MagpieEvent BuildApiKeyUpdatedEvent(
        Guid apiKeyId,
        Guid userId,
        IReadOnlyCollection<string> changedFields)
    {
        var context = new Dictionary<string, string>
        {
            [MagpieEvents.Keys.ApiKeyId] = apiKeyId.ToString("D", CultureInfo.InvariantCulture),
            [MagpieEvents.Keys.UserId] = userId.ToString("D", CultureInfo.InvariantCulture),
            [MagpieEvents.Keys.ApiKeyChangedFields] = string.Join(",", changedFields ?? []),
        };

        return new(Guid.Empty, MagpieEvents.Names.ApiKeyUpdated, context, default, default, null);
    }

    public static MagpieEvent BuildUserCreatedEvent(Guid userId, string username, string role)
    {
        var context = new Dictionary<string, string>
        {
            [MagpieEvents.Keys.UserId] = userId.ToString("D", CultureInfo.InvariantCulture),
            [MagpieEvents.Keys.UserUsername] = username ?? string.Empty,
            [MagpieEvents.Keys.UserRole] = role ?? string.Empty,
        };

        return new(Guid.Empty, MagpieEvents.Names.UserCreated, context, default, default, null);
    }

    public static MagpieEvent BuildUserDeletedEvent(Guid userId)
    {
        var context = new Dictionary<string, string>
        {
            [MagpieEvents.Keys.UserId] = userId.ToString("D", CultureInfo.InvariantCulture),
        };

        return new(Guid.Empty, MagpieEvents.Names.UserDeleted, context, default, default, null);
    }

    public static MagpieEvent BuildUserLockedOutEvent(
        Guid userId,
        string username,
        int failedAttemptCount,
        DateTime lockedUntil)
    {
        var context = new Dictionary<string, string>
        {
            [MagpieEvents.Keys.UserId] = userId.ToString("D", CultureInfo.InvariantCulture),
            [MagpieEvents.Keys.UserUsername] = username ?? string.Empty,
            [MagpieEvents.Keys.UserFailedAttemptCount] = failedAttemptCount.ToString(CultureInfo.InvariantCulture),
            [MagpieEvents.Keys.UserLockedUntil] = lockedUntil.ToString("o", CultureInfo.InvariantCulture),
        };

        return new(Guid.Empty, MagpieEvents.Names.UserLockedOut, context, default, default, null);
    }

    public static MagpieEvent BuildUserUnlockedEvent(Guid userId, string username, string unlockReason)
    {
        var context = new Dictionary<string, string>
        {
            [MagpieEvents.Keys.UserId] = userId.ToString("D", CultureInfo.InvariantCulture),
            [MagpieEvents.Keys.UserUsername] = username ?? string.Empty,
            [MagpieEvents.Keys.UserUnlockReason] = unlockReason,
        };

        return new(Guid.Empty, MagpieEvents.Names.UserUnlocked, context, default, default, null);
    }

    public static MagpieEvent BuildUserUpdatedEvent(Guid userId, IReadOnlyCollection<string> changedFields)
    {
        var context = new Dictionary<string, string>
        {
            [MagpieEvents.Keys.UserId] = userId.ToString("D", CultureInfo.InvariantCulture),
            [MagpieEvents.Keys.UserChangedFields] = string.Join(",", changedFields ?? []),
        };

        return new(Guid.Empty, MagpieEvents.Names.UserUpdated, context, default, default, null);
    }
}
