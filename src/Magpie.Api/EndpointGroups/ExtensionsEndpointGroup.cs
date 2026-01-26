using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Api;
using Magpie.Abstractions.Data;
using Magpie.Models.Extensions;
using Magpie.Models.Result;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

using HttpResult = Microsoft.AspNetCore.Http.IResult;

namespace Magpie.Api.EndpointGroups;

internal sealed class ExtensionsEndpointGroup(IExtensionRepository extensionRepository) : IApiEndpointGroup
{
    public string Name => "extensions";

    public void MapEndpoints(IEndpointRouteBuilder builder)
    {
        builder.MapGet("", GetAllExtensions)
            .WithName("GetAllExtensions")
            .WithTags("Extensions")
            .Produces<Extension[]>()
            .RequireAuthorization();

        builder.MapGet("/{id:guid}", GetExtensionById)
            .WithName("GetExtensionById")
            .WithTags("Extensions")
            .Produces<Extension>()
            .Produces(StatusCodes.Status404NotFound)
            .RequireAuthorization();

        builder.MapGet("/by-name/{name}", GetExtensionByName)
            .WithName("GetExtensionByName")
            .WithTags("Extensions")
            .Produces<Extension>()
            .Produces(StatusCodes.Status404NotFound)
            .RequireAuthorization();

        builder.MapGet("/by-assembly/{assemblyName}", GetExtensionByAssemblyName)
            .WithName("GetExtensionByAssemblyName")
            .WithTags("Extensions")
            .Produces<Extension>()
            .Produces(StatusCodes.Status404NotFound)
            .RequireAuthorization();

        builder.MapGet("/enabled", GetEnabledExtensions)
            .WithName("GetEnabledExtensions")
            .WithTags("Extensions")
            .Produces<Extension[]>()
            .RequireAuthorization();

        builder.MapGet("/managed", GetManagedExtensions)
            .WithName("GetManagedExtensions")
            .WithTags("Extensions")
            .Produces<Extension[]>()
            .RequireAuthorization();
    }

    private async Task<HttpResult> GetAllExtensions(CancellationToken cancellationToken)
    {
        var result = await extensionRepository.GetAllAsync(cancellationToken).ConfigureAwait(false);

        return result switch
        {
            SuccessResult<IReadOnlyList<Extension>> success => Results.Ok(success.Value.ToArray()),
            FailureResult<IReadOnlyList<Extension>> failure =>
                Results.Problem(failure.Reason, statusCode: StatusCodes.Status500InternalServerError),
            _ => Results.Problem("Unexpected result type", statusCode: StatusCodes.Status500InternalServerError)
        };
    }

    private async Task<HttpResult> GetExtensionById(Guid id, CancellationToken cancellationToken)
    {
        var result = await extensionRepository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);

        return result switch
        {
            SuccessResult<Extension?> { Value: null } => Results.NotFound(),
            SuccessResult<Extension?> success => Results.Ok(success.Value),
            FailureResult<Extension?> failure =>
                Results.Problem(failure.Reason, statusCode: StatusCodes.Status500InternalServerError),
            _ => Results.Problem("Unexpected result type", statusCode: StatusCodes.Status500InternalServerError)
        };
    }

    private async Task<HttpResult> GetExtensionByName(string name, CancellationToken cancellationToken)
    {
        var result = await extensionRepository.GetByNameAsync(name, cancellationToken).ConfigureAwait(false);

        return result switch
        {
            SuccessResult<Extension?> { Value: null } => Results.NotFound(),
            SuccessResult<Extension?> success => Results.Ok(success.Value),
            FailureResult<Extension?> failure =>
                Results.Problem(failure.Reason, statusCode: StatusCodes.Status500InternalServerError),
            _ => Results.Problem("Unexpected result type", statusCode: StatusCodes.Status500InternalServerError)
        };
    }

    private async Task<HttpResult> GetExtensionByAssemblyName(string assemblyName, CancellationToken cancellationToken)
    {
        var result = await extensionRepository.GetByAssemblyNameAsync(assemblyName, cancellationToken)
            .ConfigureAwait(false);

        return result switch
        {
            SuccessResult<Extension?> { Value: null } => Results.NotFound(),
            SuccessResult<Extension?> success => Results.Ok(success.Value),
            FailureResult<Extension?> failure =>
                Results.Problem(failure.Reason, statusCode: StatusCodes.Status500InternalServerError),
            _ => Results.Problem("Unexpected result type", statusCode: StatusCodes.Status500InternalServerError)
        };
    }

    private async Task<HttpResult> GetEnabledExtensions(CancellationToken cancellationToken)
    {
        var result = await extensionRepository.GetEnabledAsync(cancellationToken).ConfigureAwait(false);

        return result switch
        {
            SuccessResult<IReadOnlyList<Extension>> success => Results.Ok(success.Value.ToArray()),
            FailureResult<IReadOnlyList<Extension>> failure =>
                Results.Problem(failure.Reason, statusCode: StatusCodes.Status500InternalServerError),
            _ => Results.Problem("Unexpected result type", statusCode: StatusCodes.Status500InternalServerError)
        };
    }

    private async Task<HttpResult> GetManagedExtensions(CancellationToken cancellationToken)
    {
        var result = await extensionRepository.GetManagedAsync(cancellationToken).ConfigureAwait(false);

        return result switch
        {
            SuccessResult<IReadOnlyList<Extension>> success => Results.Ok(success.Value.ToArray()),
            FailureResult<IReadOnlyList<Extension>> failure =>
                Results.Problem(failure.Reason, statusCode: StatusCodes.Status500InternalServerError),
            _ => Results.Problem("Unexpected result type", statusCode: StatusCodes.Status500InternalServerError)
        };
    }
}
