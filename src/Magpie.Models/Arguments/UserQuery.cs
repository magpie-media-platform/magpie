using Magpie.Models.Enums;

namespace Magpie.Models.Arguments;

/// <summary>
/// The query filters for users.
/// </summary>
public sealed record UserQuery
{
    /// <summary>
    /// The username to match.
    /// </summary>
    public string? Username { get; init; }

    /// <summary>
    /// The enabled state to match.
    /// </summary>
    public bool? Enabled { get; init; }

    /// <summary>
    /// The <see cref="Role"/> to match.
    /// </summary>
    public Role? Role { get; init; }

    /// <summary>
    /// The authentication provider type to match.
    /// </summary>
    public string? ProviderType { get; init; }

    /// <summary>
    /// The external identity-provider id to match.
    /// </summary>
    public string? ExternalId { get; init; }
}
