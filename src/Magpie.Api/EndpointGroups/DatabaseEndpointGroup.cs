using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Models;
using Magpie.Models.Api;
using Magpie.Sdk.Api;
using Magpie.Sdk.Services;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Magpie.Api.EndpointGroups;

[MagpieCommandGroup("database", "database", "Manage database")]
internal sealed class DatabaseEndpointGroup : IApiEndpointGroup
{
    public string Name => "database";

    public void MapEndpoints(IEndpointRouteBuilder builder)
    {
        builder.MapGet("", GetCapabilitiesAsync)
            .WithName(nameof(GetCapabilitiesAsync))
            .Produces<DatabaseManagementInfo>()
            .RequireAdmin();

        builder.MapGet("backups", ListBackupsAsync)
            .WithName(nameof(ListBackupsAsync))
            .Produces<IReadOnlyList<BackupInfo>>()
            .RequireAdmin();

        builder.MapDelete("backups/{id}", DeleteBackupAsync)
            .WithName(nameof(DeleteBackupAsync))
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireAdmin();

        builder.MapPost("backups/{id}/restore", RestoreBackupAsync)
            .WithName(nameof(RestoreBackupAsync))
            .Produces(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireAdmin();
    }

    [MagpieCommand("info", "GET", "", "Get database management info")]
    internal async Task<IResult> GetCapabilitiesAsync(
        [FromServices] IDatabaseManagementService databaseManagementService,
        CancellationToken cancellationToken)
    {
        var capabilities = await databaseManagementService.GetInfo(cancellationToken).ConfigureAwait(false);

        return Results.Ok(capabilities);
    }

    [MagpieCommand("backups", "GET", "backups", "List available database backups")]
    internal async Task<IResult> ListBackupsAsync(
        [FromServices] IDatabaseManagementService databaseManagementService,
        CancellationToken cancellationToken)
    {
        var backups = await databaseManagementService.ListBackupsAsync(cancellationToken).ConfigureAwait(false);

        return Results.Ok(backups);
    }

    [MagpieCommand("delete", "DELETE", "backups/{id}", "Delete a database backup")]
    internal async Task<IResult> DeleteBackupAsync(
        [FromServices] IDatabaseManagementService databaseManagementService,
        AuthenticatedUser user,
        string id,
        CancellationToken cancellationToken)
    {
        var deleted = await databaseManagementService.DeleteBackupAsync(id, user.Id, cancellationToken)
            .ConfigureAwait(false);

        return deleted ? Results.NoContent() : Results.NotFound();
    }

    internal async Task<IResult> RestoreBackupAsync(
        [FromServices] IDatabaseManagementService databaseManagementService,
        AuthenticatedUser user,
        string id,
        CancellationToken cancellationToken)
    {
        var restored = await databaseManagementService.RestoreBackupAsync(id, user.Id, cancellationToken)
            .ConfigureAwait(false);

        return restored ? Results.Accepted() : Results.NotFound();
    }
}
