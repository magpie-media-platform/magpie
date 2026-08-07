using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Sdk.Api;
using Magpie.Sdk.Services;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Magpie.Api.EndpointGroups;

internal sealed class AuthProvidersEndpointGroup : IApiEndpointGroup
{
    public string Name => "auth-providers";

    public void MapEndpoints(IEndpointRouteBuilder builder)
    {
        builder.MapGet("", GetAuthProvidersAsync)
            .WithName(nameof(GetAuthProvidersAsync))
            .Produces<IReadOnlyList<string>>()
            .RequireAdmin();
    }

    internal async Task<IResult> GetAuthProvidersAsync(
        [FromServices] IAuthService authService,
        CancellationToken cancellationToken)
    {
        var providerTypes = await authService.ListCredentialProviderTypesAsync(cancellationToken)
            .ConfigureAwait(false);

        return Results.Ok(providerTypes);
    }
}
