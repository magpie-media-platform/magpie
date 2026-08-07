using System.Threading;
using System.Threading.Tasks;

using Magpie.Models.Security;

namespace Magpie.Sdk.Security;

/// <summary>
/// The validation contract for passwords.
/// </summary>
public interface IPasswordPolicy
{
    /// <summary>
    /// Validates a password.
    /// </summary>
    /// <param name="password">The password to validate</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<PasswordPolicyResult> ValidatePasswordAsync(
        string? password,
        CancellationToken cancellationToken = default);
}
