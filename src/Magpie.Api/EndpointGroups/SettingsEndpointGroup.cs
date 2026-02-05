using System;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Api;
using Magpie.Abstractions.Repositories;
using Magpie.Abstractions.Security;
using Magpie.Models.Arguments;
using Magpie.Models.Data;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Magpie.Api.EndpointGroups;

internal sealed class SettingsEndpointGroup(ISettingsRepository settingsRepository) : IApiEndpointGroup
{
    public string Name => "settings";

    public void MapEndpoints(IEndpointRouteBuilder builder)
    {
        builder.MapGet("groups", GetAllSettingsGroups)
            .WithName(nameof(GetAllSettingsGroups))
            .Produces<SettingsGroup[]>()
            .RequireManager();

        builder.MapGet("groups/{extensionId:guid}/{name}", GetSettingsGroupByKey)
            .WithName(nameof(GetSettingsGroupByKey))
            .Produces<SettingsGroup>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireManager();

        builder.MapGet("groups/system/{name}", GetSystemSettingsGroupByName)
            .WithName(nameof(GetSystemSettingsGroupByName))
            .Produces<SettingsGroup>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireManager();

        builder.MapPatch("groups/{extensionId:guid}/{name}", PatchSettingsGroup)
            .WithName(nameof(PatchSettingsGroup))
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireAdmin();

        builder.MapPatch("groups/system/{name}", PatchSystemSettingsGroup)
            .WithName(nameof(PatchSystemSettingsGroup))
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireAdmin();
    }

    private async Task<IResult> GetAllSettingsGroups(CancellationToken cancellationToken)
    {
        var groups = await settingsRepository.ListAllAsync(cancellationToken).ConfigureAwait(false);
        return Results.Ok(groups);
    }

    private async Task<IResult> GetSettingsGroupByKey(
        Guid extensionId,
        string name,
        CancellationToken cancellationToken)
    {
        var key = new SettingsGroupLookupKey(extensionId, name);
        var group = await settingsRepository.GetByIdAsync(key, cancellationToken).ConfigureAwait(false);

        return group is null
            ? Results.NotFound()
            : Results.Ok(group);
    }

    private async Task<IResult> GetSystemSettingsGroupByName(
        string name,
        CancellationToken cancellationToken)
    {
        var key = new SettingsGroupLookupKey(null, name);
        var group = await settingsRepository.GetByIdAsync(key, cancellationToken).ConfigureAwait(false);

        return group is null
            ? Results.NotFound()
            : Results.Ok(group);
    }

    private async Task<IResult> PatchSettingsGroup(
        HttpContext httpContext,
        Guid extensionId,
        string name,
        SettingsGroupPatch patch,
        CancellationToken cancellationToken)
    {
        var currentUserId = httpContext.User.GetUserId();
        var effectivePatch = patch with { ExtensionId = extensionId, Name = name };
        var updated = await settingsRepository.PatchAsync(effectivePatch, currentUserId, cancellationToken)
            .ConfigureAwait(false);

        return updated
            ? Results.NoContent()
            : Results.NotFound();
    }

    private async Task<IResult> PatchSystemSettingsGroup(
        HttpContext httpContext,
        string name,
        SettingsGroupPatch patch,
        CancellationToken cancellationToken)
    {
        var currentUserId = httpContext.User.GetUserId();
        var effectivePatch = patch with { ExtensionId = null, Name = name };
        var updated = await settingsRepository.PatchAsync(effectivePatch, currentUserId, cancellationToken)
            .ConfigureAwait(false);

        return updated
            ? Results.NoContent()
            : Results.NotFound();
    }
}
