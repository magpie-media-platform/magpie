using System.Collections.Generic;

namespace Magpie.Models.Security;

/// <summary>
/// The result of password validation.
/// </summary>
/// <param name="IsValid">Whether the password satisfies the policy</param>
/// <param name="ValidationErrors">The policy validation errors</param>
public sealed record PasswordPolicyResult(bool IsValid, IReadOnlyList<string> ValidationErrors);
