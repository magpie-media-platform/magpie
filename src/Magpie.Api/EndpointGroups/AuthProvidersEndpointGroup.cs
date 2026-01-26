using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Api;
using Magpie.Abstractions.Data;
using Magpie.Models.Result;
using Magpie.Models.Users;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

using HttpResult = Microsoft.AspNetCore.Http.IResult;

namespace Magpie.Api.EndpointGroups;

internal sealed class AuthProvidersEndpointGroup(IAuthProviderRepository authProviderRepository) : IApiEndpointGroup
{
    public string Name => "auth-providers";

    public void MapEndpoints(IEndpointRouteBuilder builder)
    {
        builder.MapGet("", GetAllAuthProviders)
            .WithName("GetAllAuthProviders")
            .WithTags("AuthProviders")
            .Produces<AuthProvider[]>()
            .AllowAnonymous();

        builder.MapGet("/{id:guid}", GetAuthProviderById)
            .WithName("GetAuthProviderById")
            .WithTags("AuthProviders")
            .Produces<AuthProvider>()
            .Produces(StatusCodes.Status404NotFound)
            .RequireAuthorization();
    }

    private async Task<HttpResult> GetAllAuthProviders(CancellationToken cancellationToken)
    {
        var result = await authProviderRepository.GetAllAsync(cancellationToken).ConfigureAwait(false);

        return result switch
        {
            SuccessResult<IReadOnlyList<AuthProvider>> success =>
                Results.Ok(success.Value.ToArray()),
            FailureResult<IReadOnlyList<AuthProvider>> failure =>
                Results.Problem(failure.Reason, statusCode: StatusCodes.Status500InternalServerError),
            _ =>
                Results.Problem("Unexpected result type", statusCode: StatusCodes.Status500InternalServerError)
        };
    }

    private async Task<HttpResult> GetAuthProviderById(Guid id, CancellationToken cancellationToken)
    {
        var result = await authProviderRepository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);

        return result switch
        {
            SuccessResult<AuthProvider?> { Value: null } => Results.NotFound(),
            SuccessResult<AuthProvider?> success => Results.Ok(success.Value),
            FailureResult<AuthProvider?> failure =>
                Results.Problem(failure.Reason, statusCode: StatusCodes.Status500InternalServerError),
            _ =>
                Results.Problem("Unexpected result type", statusCode: StatusCodes.Status500InternalServerError)
        };
    }
}
