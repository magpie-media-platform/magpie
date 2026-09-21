using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Models.Api;
using Magpie.Models.Data;
using Magpie.Sdk.Api;
using Magpie.Sdk.Services;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Magpie.Api.EndpointGroups;

internal sealed class FileTypesEndpointGroup : IApiEndpointGroup
{
    public string Name => "system/file-types";

    public void MapEndpoints(IEndpointRouteBuilder builder)
    {
        builder.MapGet("exclusions", GetExclusionsAsync)
            .WithName(nameof(GetExclusionsAsync))
            .Produces<IReadOnlyList<BlockedFileType>>()
            .RequireManager();

        builder.MapPost("exclusions", CreateExclusionAsync)
            .WithName(nameof(CreateExclusionAsync))
            .Produces<BlockedFileType>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .RequireManager();

        builder.MapDelete("exclusions/{id:guid}", DeleteExclusionAsync)
            .WithName(nameof(DeleteExclusionAsync))
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireManager();

        builder.MapGet("mime", GetMimeMappingsAsync)
            .WithName(nameof(GetMimeMappingsAsync))
            .Produces<IReadOnlyList<MimeMapping>>()
            .RequireManager();

        builder.MapPut("mime/{extension}", UpsertMimeMappingAsync)
            .WithName(nameof(UpsertMimeMappingAsync))
            .Produces<MimeMapping>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .RequireManager();

        builder.MapDelete("mime/{extension}", DeleteMimeMappingAsync)
            .WithName(nameof(DeleteMimeMappingAsync))
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireManager();
    }

    internal async Task<IResult> GetExclusionsAsync(
        [FromServices] IFileSystem fileSystem,
        CancellationToken cancellationToken)
    {
        var exclusions = await fileSystem.ListExclusionsAsync(cancellationToken).ConfigureAwait(false);

        return Results.Ok(exclusions);
    }

    internal async Task<IResult> CreateExclusionAsync(
        [FromServices] IFileSystem fileSystem,
        AuthenticatedUser user,
        [FromBody] string pattern,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(pattern))
        {
            return Results.Problem(
                detail: "The pattern must not be empty.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var created = await fileSystem.CreateExclusionAsync(pattern.Trim(), user.Id, cancellationToken)
            .ConfigureAwait(false);

        return Results.Created($"/api/{Name}/exclusions/{created.Id}", created);
    }

    internal async Task<IResult> DeleteExclusionAsync(
        [FromServices] IFileSystem fileSystem,
        AuthenticatedUser user,
        Guid id,
        CancellationToken cancellationToken)
    {
        var deleted = await fileSystem.DeleteExclusionAsync(id, user.Id, cancellationToken)
            .ConfigureAwait(false);

        return deleted ? Results.NoContent() : Results.NotFound();
    }

    internal async Task<IResult> GetMimeMappingsAsync(
        [FromServices] IFileSystem fileSystem,
        CancellationToken cancellationToken)
    {
        var mappings = await fileSystem.ListMimeMappingsAsync(cancellationToken).ConfigureAwait(false);

        return Results.Ok(mappings);
    }

    internal async Task<IResult> UpsertMimeMappingAsync(
        [FromServices] IFileSystem fileSystem,
        AuthenticatedUser user,
        string extension,
        MimeMappingUpdate request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.MimeType))
        {
            return Results.Problem(
                detail: "The MIME type is required.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var mapping = await fileSystem
            .UpsertMimeMappingAsync(extension, request.MimeType, user.Id, cancellationToken)
            .ConfigureAwait(false);

        return Results.Ok(mapping);
    }

    internal async Task<IResult> DeleteMimeMappingAsync(
        [FromServices] IFileSystem fileSystem,
        AuthenticatedUser user,
        string extension,
        CancellationToken cancellationToken)
    {
        var deleted = await fileSystem.DeleteMimeMappingAsync(extension, user.Id, cancellationToken)
            .ConfigureAwait(false);

        return deleted ? Results.NoContent() : Results.NotFound();
    }
}
