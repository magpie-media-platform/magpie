using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Models.Result;

using static Magpie.Core.Security.IPasswordPolicy;

namespace Magpie.Core.Security;

/// <summary>
/// Default password policy implementation that accepts any non-empty password.
/// </summary>
public sealed class DefaultPasswordPolicy : IPasswordPolicy
{
    /// <inheritdoc />
    public Task<Result<PasswordPolicyResult>> ValidatePasswordAsync(
        string? password,
        CancellationToken cancellationToken = default)
    {
        var validationErrors = new List<string>();

        if (string.IsNullOrWhiteSpace(password))
        {
            validationErrors.Add("Password cannot be empty");
        }
        if (password?.Length > password?.Trim().Length)
        {
            validationErrors.Add("Password cannot contain leading or trailing whitespace");
        }
        if (password?.Length < 8)
        {
            validationErrors.Add("Password must be at least 8 characters long");
        }

        return Task.FromResult(validationErrors.Count > 0
            ? Result.Fail<PasswordPolicyResult>(string.Join(", ", validationErrors))
            : Result.Success(new PasswordPolicyResult(true, validationErrors)));
    }
}
