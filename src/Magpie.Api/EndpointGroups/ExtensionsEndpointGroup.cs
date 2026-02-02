using System;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Api;
using Magpie.Abstractions.Repositories;
using Magpie.Models.Data;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

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
    }

    private async Task<IResult> GetAllExtensions(CancellationToken cancellationToken)
    {
        var extensions = await extensionRepository.ListAllAsync(cancellationToken).ConfigureAwait(false);

        return Results.Ok(extensions);
    }

    private async Task<IResult> GetExtensionById(Guid id, CancellationToken cancellationToken)
    {
        var extension = await extensionRepository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);

        return extension is null
            ? Results.NotFound()
            : Results.Ok(extension);
    }
}
