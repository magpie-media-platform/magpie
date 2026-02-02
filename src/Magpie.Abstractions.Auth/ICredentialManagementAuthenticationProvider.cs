using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Magpie.Abstractions.Auth;

/// <summary>
/// Provides credential based authentication and credential management.
/// </summary>
public interface ICredentialManagementAuthenticationProvider : ICredentialAuthenticationProvider
{
    /// <summary>
    /// Reset the password for a user.
    /// </summary>
    /// <param name="userId">The id of the user whose password is being reset.</param>
    /// <param name="newPassword">The new password.</param>
    /// <param name="requestingUserId">The id of the user requesting the reset.</param>
    /// <param name="verificationPayload">Verification payload that may be required.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>
    /// <see langword="true"/> if the password was successfully reset; otherwise, <see langword="false"/>.
    /// </returns>
    Task<bool> ResetPasswordAsync(
        Guid userId,
        string newPassword,
        Guid requestingUserId,
        IReadOnlyDictionary<string, string> verificationPayload,
        CancellationToken cancellationToken = default);
}
