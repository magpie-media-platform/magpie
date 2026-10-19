# Authentication providers

[Documentation](../../README.md) / [Development](../README.md) / [Extension components](README.md)

An authentication provider connects an outside credential source, such as an identity service or a custom token, to a Magpie account. You write one when people should be able to sign in or call the API in a way Magpie does not support itself. The provider only proves who the caller is. The account stays the source of identity and [role](../../reference/roles.md), and [endpoints](api-endpoints.md#keeping-request-work-in-the-handler) still enforce their own requirements.

There are two flows, and a provider can serve one or both. `IAuthenticationProvider` looks at credentials on an HTTP request, such as a bearer token. `ICredentialAuthenticationProvider` receives the username and password that Magpie's login service collects. Both contracts need the [SDK's `net10.0` target](../../reference/extensions.md#framework-and-abi-compatibility).

## The request flow

The host first checks for an [API key](../../administration/access.md#issuing-an-api-key). If that does not authenticate the request, it calls the request providers in registration order: the built-in bearer token provider, then extension providers in [load order](README.md#registration-order). Each returns one of three results from `TryAuthenticateAsync`.

| Result | Meaning |
| --- | --- |
| `AuthenticationAttempt.None` | The request has no credential this provider handles. |
| `AuthenticationAttempt.Rejected(reason)` | A credential was supplied and failed verification. The host moves on to the next provider. |
| `AuthenticationAttempt.Authenticated(principal)` | Verification succeeded and the principal maps to a Magpie account. |

Return `Rejected` only for a credential the provider recognizes. The host takes the first valid result. It requires a GUID `NameIdentifier` claim and checks that the account exists and is enabled, but it uses the other claims as given, so build the role claim from the verified account. The host does not catch exceptions from `TryAuthenticateAsync`. A throw fails the request and skips the remaining providers, so return `Rejected` or `None` for expected failures such as an unreachable identity service. When an endpoint issues a challenge, the host calls `ChallengeAsync` on every request provider.

This is the end of a successful `TryAuthenticateAsync`, where `user` is a verified, enabled Magpie `User`. It uses `System.Security.Claims`:

```csharp
var identity = new ClaimsIdentity(
    new[]
    {
        new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
        new Claim(ClaimTypes.Name, user.Username),
        new Claim(ClaimTypes.Role, user.Role.ToString())
    },
    ProviderType);

return AuthenticationAttempt.Authenticated(new ClaimsPrincipal(identity));
```

The [bearer provider](../../../src/Magpie.Auth/JwtBearerAuthenticationProvider.cs) shows the parsing and verification around this step.

## The login flow

The login service tries the local provider first, then the other credential providers in registration order, and stops at the first one that returns a `User`. Return null when the credentials are not yours. The service issues tokens for whatever account you return without further checks, so return only an existing, enabled account. Look it up with `IUserService.GetAuthenticationUserAsync`. `IUserService.CreateAsync` creates local accounts only. The [local provider](../../../src/Magpie.Auth/LocalCredentialAuthenticationProvider.cs) is the model for this contract.

## Registering the provider

Register the provider [scoped](../using-built-in-services.md#service-lifetime-and-ownership) in [`IExtension.RegisterServices`](../../reference/extensions.md#entry-point). Magpie does not pick up implemented interfaces on its own, so register each flow you serve. A login-only provider returns `AuthenticationAttempt.None` from `TryAuthenticateAsync` and registers only as `ICredentialAuthenticationProvider`, as the local provider does. A provider serving both flows registers one object under both interfaces:

```csharp
services.AddScoped<ExampleAuthenticationProvider>();
services.AddScoped<IAuthenticationProvider>(
    provider => provider.GetRequiredService<ExampleAuthenticationProvider>());
services.AddScoped<ICredentialAuthenticationProvider>(
    provider => provider.GetRequiredService<ExampleAuthenticationProvider>());
```

Give the provider a unique, stable `ProviderType`, which appears in logs and authentication failure metrics. Keep credentials out of logs, pass the cancellation token to verification calls, and never let a dependency failure produce a successful principal.

See [IAuthenticationProvider](../../../src/Magpie.Sdk/Auth/IAuthenticationProvider.cs), [ICredentialAuthenticationProvider](../../../src/Magpie.Sdk/Auth/ICredentialAuthenticationProvider.cs), and the [host handler](../../../src/Magpie.Auth/MagpieAuthenticationHandler.cs).

---

[Extension components](README.md) | [Development](../README.md) | [Documentation](../../README.md)
