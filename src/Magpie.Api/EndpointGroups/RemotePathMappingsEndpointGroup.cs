using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Models.Api;
using Magpie.Models.Arguments;
using Magpie.Models.Data;
using Magpie.Sdk.Api;
using Magpie.Sdk.Services;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Magpie.Api.EndpointGroups;

internal sealed class RemotePathMappingsEndpointGroup : IApiEndpointGroup
{
    public string Name => "remote-path-mappings";

    public void MapEndpoints(IEndpointRouteBuilder builder)
    {
        builder.MapGet("", GetRemotePathMappingsAsync)
            .WithName(nameof(GetRemotePathMappingsAsync))
            .Produces<IReadOnlyList<RemotePathMapping>>()
            .RequireViewer();

        builder.MapPost("", CreateRemotePathMappingAsync)
            .WithName(nameof(CreateRemotePathMappingAsync))
            .Produces<RemotePathMapping>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .RequireAdmin();

        builder.MapPost("test", TestRemotePathMappingLocalPathAsync)
            .WithName(nameof(TestRemotePathMappingLocalPathAsync))
            .Produces<ConfigurationTestResult>()
            .RequireAdmin();

        builder.MapGet("{id:guid}", GetRemotePathMappingAsync)
            .WithName(nameof(GetRemotePathMappingAsync))
            .Produces<RemotePathMapping>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireViewer();

        builder.MapPatch("{id:guid}", PatchRemotePathMappingAsync)
            .WithName(nameof(PatchRemotePathMappingAsync))
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .RequireAdmin();

        builder.MapDelete("{id:guid}", DeleteRemotePathMappingAsync)
            .WithName(nameof(DeleteRemotePathMappingAsync))
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireAdmin();
    }

    internal async Task<IResult> GetRemotePathMappingsAsync(
        [FromServices] IRemotePathMappingService remotePathMappingService,
        CancellationToken cancellationToken)
    {
        var mappings = await remotePathMappingService.ListAsync(cancellationToken).ConfigureAwait(false);

        return Results.Ok(mappings);
    }

    internal async Task<IResult> CreateRemotePathMappingAsync(
        [FromServices] IRemotePathMappingService remotePathMappingService,
        AuthenticatedUser user,
        RemotePathMappingCreate create,
        CancellationToken cancellationToken)
    {
        var created = await remotePathMappingService.CreateAsync(create, user.Id, cancellationToken)
            .ConfigureAwait(false);

        return Results.Created($"/api/{Name}/{created.Id}", created);
    }

    internal async Task<IResult> TestRemotePathMappingLocalPathAsync(
        [FromServices] IRemotePathMappingService remotePathMappingService,
        RemotePathMappingLocalPathTestRequest request,
        CancellationToken cancellationToken)
    {
        var result = await remotePathMappingService.TestLocalPathAsync(request.LocalPath, cancellationToken)
            .ConfigureAwait(false);

        return Results.Ok(result);
    }

    internal async Task<IResult> GetRemotePathMappingAsync(
        [FromServices] IRemotePathMappingService remotePathMappingService,
        Guid id,
        CancellationToken cancellationToken)
    {
        var mapping = await remotePathMappingService.GetAsync(id, cancellationToken).ConfigureAwait(false);

        return mapping is null ? Results.NotFound() : Results.Ok(mapping);
    }

    internal async Task<IResult> PatchRemotePathMappingAsync(
        [FromServices] IRemotePathMappingService remotePathMappingService,
        AuthenticatedUser user,
        Guid id,
        RemotePathMappingPatch patch,
        CancellationToken cancellationToken)
    {
        var patched = await remotePathMappingService.PatchAsync(id, patch, user.Id, cancellationToken)
            .ConfigureAwait(false);

        return patched ? Results.NoContent() : Results.NotFound();
    }

    internal async Task<IResult> DeleteRemotePathMappingAsync(
        [FromServices] IRemotePathMappingService remotePathMappingService,
        AuthenticatedUser user,
        Guid id,
        CancellationToken cancellationToken)
    {
        var deleted = await remotePathMappingService.DeleteAsync(id, user.Id, cancellationToken)
            .ConfigureAwait(false);

        return deleted ? Results.NoContent() : Results.NotFound();
    }
}
