using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Models.Security;
using Magpie.Sdk.Security;

namespace Magpie.Core.Security;

internal sealed class DefaultPasswordPolicy : IPasswordPolicy
{
    public Task<PasswordPolicyResult> ValidatePasswordAsync(
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

        return Task.FromResult(new PasswordPolicyResult(validationErrors.Count == 0, validationErrors));
    }
}
