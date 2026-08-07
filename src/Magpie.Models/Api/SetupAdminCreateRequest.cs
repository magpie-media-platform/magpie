namespace Magpie.Models.Api;

/// <summary>
/// The request to create the first administrator account.
/// </summary>
public sealed record SetupAdminCreateRequest
{
    /// <summary>
    /// The username for the new administrator account.
    /// </summary>
    public required string Username { get; init; }

    /// <summary>
    /// The password for the new administrator account.
    /// </summary>
    public required string Password { get; init; }
}
