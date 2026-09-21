using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Models;
using Magpie.Models.Api;
using Magpie.Models.Enums;
using Magpie.Models.Storage;
using Magpie.Sdk.Api;
using Magpie.Sdk.Services;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Magpie.Api.EndpointGroups;

internal sealed class SystemEndpointGroup : IApiEndpointGroup
{
    public string Name => "system";

    public void MapEndpoints(IEndpointRouteBuilder builder)
    {
        builder.MapGet("", GetSystemInfo)
            .WithName(nameof(GetSystemInfo))
            .Produces<SystemInfo>()
            .RequireViewer();

        builder.MapGet("disk-space", GetDiskSpaceAsync)
            .WithName(nameof(GetDiskSpaceAsync))
            .Produces<DiskSpaceResult>()
            .RequireViewer();

        builder.MapGet("health", GetSystemHealthAsync)
            .WithName(nameof(GetSystemHealthAsync))
            .Produces<SystemHealth>()
            .AllowAnonymous();

        builder.MapGet("ready", GetSystemReadiness)
            .WithName(nameof(GetSystemReadiness))
            .Produces(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .AllowAnonymous();

        builder.MapGet("files", BrowseFilesAsync)
            .WithName(nameof(BrowseFilesAsync))
            .Produces<IReadOnlyList<FileSystemEntry>>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireManager();

        builder.MapPost("restart", RestartHostAsync)
            .WithName(nameof(RestartHostAsync))
            .Produces(StatusCodes.Status202Accepted)
            .RequireAdmin();

        builder.MapGet("setup/status", GetSetupStatusAsync)
            .WithName(nameof(GetSetupStatusAsync))
            .Produces<SetupStatusResponse>()
            .AllowAnonymous();

        builder.MapPost("setup/admin", CreateSetupAdminAsync)
            .WithName(nameof(CreateSetupAdminAsync))
            .Produces<UserResponse>()
            .AllowAnonymous();

        builder.MapGet("setup/db-status", GetDatabaseSetupStatusAsync)
            .WithName(nameof(GetDatabaseSetupStatusAsync))
            .Produces<SetupDatabaseStatusResponse>()
            .AllowAnonymous();

        builder.MapPost("setup/db", ConfigureDatabaseAsync)
            .WithName(nameof(ConfigureDatabaseAsync))
            .Produces<DatabaseSetupResult>()
            .AllowAnonymous();

        builder.MapPost("setup/complete", CompleteSetupAsync)
            .WithName(nameof(CompleteSetupAsync))
            .Produces<SetupStatusResponse>()
            .AllowAnonymous();

        builder.MapPost("shutdown", ShutdownHostAsync)
            .WithName(nameof(ShutdownHostAsync))
            .Produces(StatusCodes.Status202Accepted)
            .RequireAdmin();
    }

    internal IResult GetSystemInfo([FromServices] IMagpieSystem magpieSystem)
    {
        return Results.Ok(magpieSystem.GetInfo());
    }

    internal async Task<IResult> GetDiskSpaceAsync(
        [FromServices] IRootFolderService rootFolderService,
        CancellationToken cancellationToken)
    {
        var diskSpace = await rootFolderService.GetDiskSpaceAsync(cancellationToken).ConfigureAwait(false);

        return Results.Ok(diskSpace);
    }

    internal async Task<IResult> GetSystemHealthAsync(
        [FromServices] IMagpieSystem magpieSystem,
        CancellationToken cancellationToken)
    {
        var health = await magpieSystem.CheckHealthAsync(cancellationToken).ConfigureAwait(false);

        return Results.Ok(health);
    }

    internal IResult GetSystemReadiness()
    {
        return Results.Ok();
    }

    internal async Task<IResult> BrowseFilesAsync(
        [FromServices] IFileSystem fileSystem,
        [FromServices] IContentService contentService,
        AuthenticatedUser user,
        string? path,
        Guid? scope,
        CancellationToken cancellationToken)
    {
        if (scope is { } contentId)
        {
            if (path is null)
            {
                var location = await contentService.GetFileBrowserLocationAsync(contentId, cancellationToken)
                    .ConfigureAwait(false);
                if (location is null)
                {
                    return Results.NotFound();
                }

                path = location.InitialPath;
            }

            var scopedEntries = await contentService.BrowseFilesAsync(contentId, path, cancellationToken)
                .ConfigureAwait(false);
            return scopedEntries is null ? Results.NotFound() : Results.Ok(scopedEntries);
        }

        if (!user.IsInRole(Role.Admin))
        {
            return Results.Forbid();
        }

        var entries = await fileSystem.BrowseAsync(path, cancellationToken).ConfigureAwait(false);

        return Results.Ok(entries);
    }

    internal async Task<IResult> RestartHostAsync(
        [FromServices] IMagpieSystem magpieSystem,
        AuthenticatedUser user,
        CancellationToken cancellationToken)
    {
        await magpieSystem.RequestRestartAsync(user.Id, cancellationToken).ConfigureAwait(false);

        return Results.Accepted();
    }

    internal async Task<IResult> GetSetupStatusAsync(
        [FromServices] ISetupService setupService,
        CancellationToken cancellationToken)
    {
        var isComplete = await setupService.IsCompleteAsync(cancellationToken).ConfigureAwait(false);
        var hasLocalAdministrator = await setupService
            .HasLocalAdministratorAsync(cancellationToken)
            .ConfigureAwait(false);

        return Results.Ok(new SetupStatusResponse
        {
            IsRequired = !isComplete,
            IsComplete = isComplete,
            RequiresAdminClaim = !hasLocalAdministrator
        });
    }

    internal async Task<IResult> CreateSetupAdminAsync(
        [FromServices] ISetupService setupService,
        [FromBody] SetupAdminCreateRequest request,
        CancellationToken cancellationToken)
    {
        var created = await setupService
            .CreateAdministratorAsync(request.Username, request.Password, cancellationToken)
            .ConfigureAwait(false);

        return Results.Ok(created.ToResponse());
    }

    internal async Task<IResult> GetDatabaseSetupStatusAsync(
        [FromServices] ISetupService setupService,
        CancellationToken cancellationToken)
    {
        var canConnect = await setupService.CanConnectAsync(cancellationToken).ConfigureAwait(false);
        var appliedMigrations = canConnect
            ? await setupService.GetAppliedMigrationsAsync(cancellationToken).ConfigureAwait(false)
            : [];

        return Results.Ok(new SetupDatabaseStatusResponse
        {
            Initialized = appliedMigrations.Count > 0,
            Provider = setupService.DatabaseProvider
        });
    }

    internal async Task<IResult> ConfigureDatabaseAsync(
        [FromServices] ISetupService setupService,
        [FromBody] DatabaseSetupRequest request,
        CancellationToken cancellationToken)
    {
        var result = await setupService
            .ConfigureDatabaseAsync(request.Provider, request.ConnectionString, cancellationToken)
            .ConfigureAwait(false);

        return Results.Ok(result);
    }

    internal async Task<IResult> CompleteSetupAsync(
        [FromServices] ISetupService setupService,
        CancellationToken cancellationToken)
    {
        await setupService.CompleteAsync(cancellationToken).ConfigureAwait(false);

        return Results.Ok(new SetupStatusResponse
        {
            IsRequired = false,
            IsComplete = true,
            RequiresAdminClaim = false
        });
    }

    internal async Task<IResult> ShutdownHostAsync(
        [FromServices] IMagpieSystem magpieSystem,
        AuthenticatedUser user,
        CancellationToken cancellationToken)
    {
        await magpieSystem.RequestShutdownAsync(user.Id, cancellationToken).ConfigureAwait(false);

        return Results.Accepted();
    }
}
