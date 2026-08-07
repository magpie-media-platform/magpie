namespace Magpie.Models.Arguments;

/// <summary>
/// A request to change the password of a user.
/// </summary>
public sealed record PasswordChange
{
    /// <summary>
    /// The current password of the caller, required for a self-service change.
    /// </summary>
    public string? CurrentPassword { get; set; }

    /// <summary>
    /// The new password.
    /// </summary>
    public required string NewPassword { get; set; }
}
