using System;
using System.Collections.Generic;

namespace Magpie.Models.Users;

/// <summary>
/// A user account.
/// </summary>
/// <param name="Id">The unique identifier.</param>
/// <param name="Username">The username.</param>
/// <param name="Email">The email address.</param>
/// <param name="AuthProviderId">The authentication provider for this user.</param>
/// <param name="ExternalId">The user identifier from the auth provider.</param>
/// <param name="Enabled">Whether the user account is enabled.</param>
/// <param name="Role">The role assigned to this user.</param>
/// <param name="ProviderData">Provider-specific user data.</param>
/// <param name="LastLoginAt">When the user last logged in.</param>
/// <param name="CreatedAt">When the record was created.</param>
/// <param name="CreatedByUserId">The user who created the record.</param>
/// <param name="ModifiedAt">When the record was last modified.</param>
/// <param name="ModifiedByUserId">The user who last modified the record.</param>
public sealed record User(
    Guid Id,
    string Username,
    string? Email,
    Guid? AuthProviderId,
    string? ExternalId,
    bool Enabled,
    Role Role,
    IReadOnlyDictionary<string, string?>? ProviderData,
    DateTimeOffset? LastLoginAt,
    DateTimeOffset CreatedAt,
    Guid? CreatedByUserId,
    DateTimeOffset? ModifiedAt,
    Guid? ModifiedByUserId);
