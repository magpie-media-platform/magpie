using System;

using Magpie.Models.Enums;

namespace Magpie.Models.UI;

/// <summary>
/// Context information passed to web components.
/// </summary>
public sealed record UIContext
{
    /// <summary>
    /// The base URL for API requests.
    /// </summary>
    public required string ApiBaseUrl { get; init; }

    /// <summary>
    /// The current user ID, if authenticated.
    /// </summary>
    public Guid? UserId { get; init; }

    /// <summary>
    /// The current username, if authenticated.
    /// </summary>
    public string? Username { get; init; }

    /// <summary>
    /// The current user role, if authenticated.
    /// </summary>
    public Role? Role { get; init; }
}
