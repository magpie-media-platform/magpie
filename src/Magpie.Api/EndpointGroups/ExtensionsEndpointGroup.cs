using System;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions;
using Magpie.Abstractions.Api;
using Magpie.Abstractions.Repositories;
using Magpie.Abstractions.Security;
using Magpie.Models;
using Magpie.Models.Arguments;
using Magpie.Models.Data;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Magpie.Api.EndpointGroups;

internal sealed class ExtensionsEndpointGroup(
    IExtensionRepository extensionRepository,
    INuGetService nuGetSearchService) : IApiEndpointGroup
{
    public string Name => "extensions";

    public void MapEndpoints(IEndpointRouteBuilder builder)
    {
        builder.MapGet("", GetAllExtensions)
            .WithName(nameof(GetAllExtensions))
            .Produces<Extension[]>()
            .RequireViewer();

        builder.MapGet("{id:guid}", GetExtensionById)
            .WithName(nameof(GetExtensionById))
            .Produces<Extension>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireViewer();

        builder.MapGet("search", SearchExtensions)
            .WithName(nameof(SearchExtensions))
            .Produces<NuGetSearchResult[]>()
            .RequireManager();

        builder.MapPatch("{id:guid}", PatchExtension)
            .WithName(nameof(PatchExtension))
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .RequireManager();

        builder.MapPost("{id:guid}/enable", EnableExtension)
            .WithName(nameof(EnableExtension))
            .Produces<Extension>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireManager();

        builder.MapPost("{id:guid}/disable", DisableExtension)
            .WithName(nameof(DisableExtension))
            .Produces<Extension>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireManager();
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

    private async Task<IResult> SearchExtensions(string? q, CancellationToken cancellationToken)
    {
        var results = await nuGetSearchService.SearchAsync(q, cancellationToken).ConfigureAwait(false);

        return Results.Ok(results);
    }

    private async Task<IResult> PatchExtension(
        HttpContext httpContext,
        Guid id,
        ExtensionPatch request,
        CancellationToken cancellationToken)
    {
        var existing = await extensionRepository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);

        if (existing is null || !existing.Enabled || !existing.Managed)
        {
            return Results.NotFound();
        }

        var currentUserId = httpContext.User.GetUserId();
        var patch = request with { Id = id };

        var updated = await extensionRepository.PatchAsync(patch, currentUserId, cancellationToken)
            .ConfigureAwait(false);

        return updated
            ? Results.NoContent()
            : Results.NotFound();
    }

    private async Task<IResult> EnableExtension(
        HttpContext httpContext,
        Guid id,
        CancellationToken cancellationToken)
    {
        var existing = await extensionRepository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);

        if (existing is null)
        {
            return Results.NotFound();
        }

        var currentUserId = httpContext.User.GetUserId();

        var extension = existing with
        {
            Enabled = true,
            DisabledReason = null,
            ModifiedAt = DateTime.UtcNow,
            ModifiedByUserId = currentUserId
        };

        await extensionRepository.UpdateAsync(extension, currentUserId, cancellationToken)
            .ConfigureAwait(false);

        return Results.Ok(extension);
    }

    private async Task<IResult> DisableExtension(
        HttpContext httpContext,
        Guid id,
        CancellationToken cancellationToken)
    {
        var existing = await extensionRepository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);

        if (existing is null)
        {
            return Results.NotFound();
        }

        var currentUserId = httpContext.User.GetUserId();

        var extension = existing with
        {
            Enabled = false,
            ModifiedAt = DateTime.UtcNow,
            ModifiedByUserId = currentUserId
        };

        await extensionRepository.UpdateAsync(extension, currentUserId, cancellationToken)
            .ConfigureAwait(false);

        return Results.Ok(extension);
    }
}
