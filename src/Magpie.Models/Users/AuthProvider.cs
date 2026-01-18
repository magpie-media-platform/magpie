using System;
using System.Collections.Generic;

namespace Magpie.Models.Users;

/// <summary>
/// An authentication provider configuration.
/// </summary>
/// <param name="Id">The unique identifier.</param>
/// <param name="Name">The display name.</param>
/// <param name="Provider">The provider type (local, oidc, ldap, etc.).</param>
/// <param name="Enabled">Whether the provider is enabled.</param>
/// <param name="IsDefault">Whether this is the default provider for new users.</param>
/// <param name="Settings">Provider-specific settings.</param>
/// <param name="CreatedAt">When the record was created.</param>
/// <param name="CreatedByUserId">The user who created the record.</param>
/// <param name="ModifiedAt">When the record was last modified.</param>
/// <param name="ModifiedByUserId">The user who last modified the record.</param>
public sealed record AuthProvider(
    Guid Id,
    string Name,
    string Provider,
    bool Enabled,
    bool IsDefault,
    IReadOnlyDictionary<string, string?> Settings,
    DateTimeOffset CreatedAt,
    Guid? CreatedByUserId,
    DateTimeOffset? ModifiedAt,
    Guid? ModifiedByUserId);
