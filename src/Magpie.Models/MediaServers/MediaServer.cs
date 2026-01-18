using System;
using System.Collections.Generic;

namespace Magpie.Models.MediaServers;

/// <summary>
/// A configured media server instance.
/// </summary>
/// <param name="Id">The unique identifier.</param>
/// <param name="Name">The display name.</param>
/// <param name="Provider">The provider that handles this server.</param>
/// <param name="Enabled">Whether the server is enabled.</param>
/// <param name="Settings">Provider-specific settings.</param>
/// <param name="Status">The current health status.</param>
/// <param name="LastChecked">The date of the last health check.</param>
/// <param name="CreatedAt">When the record was created.</param>
/// <param name="CreatedByUserId">The user who created the record.</param>
/// <param name="ModifiedAt">When the record was last modified.</param>
/// <param name="ModifiedByUserId">The user who last modified the record.</param>
public sealed record MediaServer(
    Guid Id,
    string Name,
    string Provider,
    bool Enabled,
    IReadOnlyDictionary<string, string?> Settings,
    MediaServerStatus Status,
    DateTimeOffset? LastChecked,
    DateTimeOffset CreatedAt,
    Guid? CreatedByUserId,
    DateTimeOffset? ModifiedAt,
    Guid? ModifiedByUserId);
