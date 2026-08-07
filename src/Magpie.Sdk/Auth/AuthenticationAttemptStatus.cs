namespace Magpie.Sdk.Auth;

/// <summary>
/// The status of an authentication attempt.
/// </summary>
public enum AuthenticationAttemptStatus
{
    /// <summary>
    /// The request presented no credential the provider handles.
    /// </summary>
    NoCredentials,

    /// <summary>
    /// The request presented a credential the provider rejected.
    /// </summary>
    Rejected,

    /// <summary>
    /// The request presented a credential the provider accepted.
    /// </summary>
    Authenticated,
}
