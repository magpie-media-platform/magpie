using System;
using System.Threading;
using System.Threading.Tasks;

namespace Magpie.Sdk.Auth;

/// <summary>
/// A credential authentication provider that also manages credentials.
/// </summary>
public interface ICredentialManagementAuthenticationProvider : ICredentialAuthenticationProvider
{
    /// <summary>
    /// Resets a user's password.
    /// </summary>
    /// <param name="userId">The id of the user whose password is being reset</param>
    /// <param name="newPassword">The new password</param>
    /// <param name="requestingUserId">The id of the user requesting the reset</param>
    /// <param name="cancellationToken">The cancellation token</param>
    /// <returns>
    /// <see langword="true"/> when the password was reset, otherwise <see langword="false"/>.
    /// </returns>
    Task<bool> ResetPasswordAsync(
        Guid userId,
        string newPassword,
        Guid requestingUserId,
        CancellationToken cancellationToken = default);
}
