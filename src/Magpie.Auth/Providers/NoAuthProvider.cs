using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Auth;
using Magpie.Models.Result;
using Magpie.Models.Users;

using Microsoft.AspNetCore.Http;

namespace Magpie.Auth.Providers;

internal sealed class NoAuthProvider : IAuthenticationProvider
{
    public const string ProviderTypeName = "none";

    public static readonly Guid ProviderId = new("11111111-1111-1111-1111-111111111111");

    public static readonly Guid AdminUserId = new("11111111-1111-1111-1111-111111111112");

    public string ProviderType => ProviderTypeName;

    public Task<Result<ClaimsPrincipal?>> TryAuthenticateAsync(
        HttpContext context,
        CancellationToken cancellationToken = default)
    {
        var identity = new ClaimsIdentity(
            [
                new(ClaimTypes.NameIdentifier, AdminUserId.ToString()),
                new(ClaimTypes.Name, "admin"),
                new(ClaimTypes.Role, Role.Admin.ToString()),
                new(MagpieClaims.AuthProviderId, ProviderId.ToString()),
                new(ClaimTypes.AuthenticationMethod, ProviderTypeName),
            ],
            ProviderType);

        return Result.SuccessAsync<ClaimsPrincipal?>(new(identity));
    }

    public void Challenge(HttpContext context)
    {
    }
}
