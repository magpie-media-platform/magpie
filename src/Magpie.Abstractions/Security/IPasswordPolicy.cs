using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Magpie.Abstractions.Security;

/// <summary>
/// Represents a password policy.
/// </summary>
public interface IPasswordPolicy
{
    /// <summary>
    /// The result of a password policy validation.
    /// </summary>
    /// <param name="IsValid">Whether the password is valid according to the policy.</param>
    /// <param name="ValidationErrors">The validation errors.</param>
    public record PasswordPolicyResult(bool IsValid, IReadOnlyList<string> ValidationErrors);

    /// <summary>
    /// Validates the given password against the policy.
    /// </summary>
    /// <param name="password"></param>
    /// <param name="cancellationToken">The cancellation Token</param>
    /// <returns>
    /// A <see cref="PasswordPolicyResult"/> containing the result of the password policy validation.
    /// </returns>
    Task<PasswordPolicyResult> ValidatePasswordAsync(
        string? password,
        CancellationToken cancellationToken = default);
}
