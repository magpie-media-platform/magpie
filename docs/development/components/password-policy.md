# Password policy

[Documentation](../../README.md) / [Development](../README.md) / [Extension components](README.md)

`IPasswordPolicy` [replaces](README.md#replacement-components) the check Magpie runs when it sets a [local account](../../administration/access.md#creating-and-maintaining-accounts) password: account creation, password changes, and the first administrator account during setup. You write one to enforce rules such as a longer minimum or a banned-password list. The user service runs the policy before it hashes and saves the password. A rejection stops the save and reports the validation messages joined into one.

The policy applies only when a password is set. It doesn't touch hashing, sign-in, or [external authentication providers](authentication.md), and existing passwords stay valid after a policy change.

Register one [singleton](../using-built-in-services.md#service-lifetime-and-ownership) in [`IExtension.RegisterServices`](../../reference/extensions.md#entry-point), with `Magpie.Sdk.Security` and `Microsoft.Extensions.DependencyInjection` imported:

```csharp
services.AddSingleton<IPasswordPolicy, ExamplePasswordPolicy>();
```

Magpie registers its default only when no extension does, and it uses a single policy, so your policy must include every rule it requires. The default rules are a non-empty value, no leading or trailing whitespace, and at least 8 characters, and your policy drops them unless you include them.

## Validating a password

This complete component requires at least 12 characters. Add it to the project from [Creating an extension](../extensions.md).

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using Magpie.Models.Security;
using Magpie.Sdk.Security;

internal sealed class ExamplePasswordPolicy : IPasswordPolicy
{
    /// <inheritdoc />
    public Task<PasswordPolicyResult> ValidatePasswordAsync(
        string? password,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var accepted = password is not null && password.Length >= 12;
        return Task.FromResult(new PasswordPolicyResult(
            accepted,
            accepted ? Array.Empty<string>() : new[] { "Use at least 12 characters." }));
    }
}
```

The result says whether the value is accepted and, if not, what must change. Keep the submitted password out of messages and logs.

The singleton must handle concurrent requests and must not hold request-scoped services. If the policy calls an external service, pass the cancellation token along. If it throws, Magpie doesn't save the password.

See [IPasswordPolicy](../../../src/Magpie.Sdk/Security/IPasswordPolicy.cs) and [PasswordPolicyResult](../../../src/Magpie.Models/Security/PasswordPolicyResult.cs).

---

[Extension components](README.md) | [Development](../README.md) | [Documentation](../../README.md)
