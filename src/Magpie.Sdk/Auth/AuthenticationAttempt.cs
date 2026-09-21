using System;
using System.Security.Claims;

namespace Magpie.Sdk.Auth;

/// <summary>
/// The outcome of an authentication attempt.
/// </summary>
/// <param name="Status">The status of the attempt</param>
/// <param name="Principal">The authenticated principal, or <see langword="null"/></param>
/// <param name="RejectionReason">The reason the credential was rejected, or <see langword="null"/></param>
public sealed record AuthenticationAttempt(
    AuthenticationAttemptStatus Status,
    ClaimsPrincipal? Principal,
    string? RejectionReason)
{
    /// <summary>
    /// The status of the attempt.
    /// </summary>
    public AuthenticationAttemptStatus Status { get; set; } = Status;

    /// <summary>
    /// The authenticated principal, or null.
    /// </summary>
    public ClaimsPrincipal? Principal { get; set; } = Principal;

    /// <summary>
    /// The reason for the rejected credential, or null.
    /// </summary>
    public string? RejectionReason { get; set; } = RejectionReason;

    /// <summary>
    /// The attempt for a request that presented no credential.
    /// </summary>
    public static AuthenticationAttempt None => new(AuthenticationAttemptStatus.NoCredentials, null, null);

    /// <summary>
    /// Creates the attempt for a request whose credential was rejected.
    /// </summary>
    /// <param name="reason">The reason the credential was rejected</param>
    public static AuthenticationAttempt Rejected(string reason)
    {
        return new AuthenticationAttempt(AuthenticationAttemptStatus.Rejected, null, reason);
    }

    /// <summary>
    /// Creates the attempt for a request whose credential authenticated a principal.
    /// </summary>
    /// <param name="principal">The authenticated principal</param>
    public static AuthenticationAttempt Authenticated(ClaimsPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);

        return new AuthenticationAttempt(AuthenticationAttemptStatus.Authenticated, principal, null);
    }
}
