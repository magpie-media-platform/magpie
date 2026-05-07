using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Api;
using Magpie.Abstractions.Database;
using Magpie.Models.Api;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Magpie.Api.EndpointGroups;

internal sealed class BackupsEndpointGroup(IDatabaseManagementProvider databaseProvider) : IApiEndpointGroup
{
    public string Name => "backups";

    public void MapEndpoints(IEndpointRouteBuilder builder)
    {
        builder.MapGet("", ListBackups)
            .WithName(nameof(ListBackups))
            .Produces<BackupInfo[]>()
            .RequireAdmin();

        builder.MapPost("", CreateBackup)
            .WithName(nameof(CreateBackup))
            .Produces<BackupCreateResponse>(StatusCodes.Status201Created)
            .RequireAdmin();

        builder.MapDelete("{id}", DeleteBackup)
            .WithName(nameof(DeleteBackup))
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireAdmin();

        builder.MapPost("{id}/restore", RestoreBackup)
            .WithName(nameof(RestoreBackup))
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireAdmin();

        builder.MapGet("{id}/download", DownloadBackup)
            .WithName(nameof(DownloadBackup))
            .Produces(StatusCodes.Status200OK, contentType: "application/octet-stream")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireAdmin();
    }

    private async Task<IResult> ListBackups(CancellationToken cancellationToken)
    {
        var providerBackups = await databaseProvider.ListBackupsAsync(cancellationToken).ConfigureAwait(false);
        var backups = providerBackups.Select(b => new BackupInfo
        {
            Id = b.Id,
            Name = b.Name,
            CreatedAt = b.CreatedAt,
            SizeBytes = b.SizeBytes,
        }).ToArray();
        return Results.Ok(backups);
    }

    private async Task<IResult> CreateBackup(BackupCreateRequest? request, CancellationToken cancellationToken)
    {
        var backupId = await databaseProvider.BackupAsync(request?.Name, cancellationToken).ConfigureAwait(false);
        return Results.Created($"api/{Name}/{backupId}", new BackupCreateResponse(backupId));
    }

    private async Task<IResult> DeleteBackup(string id, CancellationToken cancellationToken)
    {
        var deleted = await databaseProvider.DeleteBackupAsync(id, cancellationToken).ConfigureAwait(false);

        return deleted ? Results.NoContent() : Results.NotFound();
    }

    private async Task<IResult> RestoreBackup(string id, CancellationToken cancellationToken)
    {
        var restored = await databaseProvider.RestoreAsync(id, cancellationToken).ConfigureAwait(false);

        return restored ? Results.NoContent() : Results.NotFound();
    }

    private async Task<IResult> DownloadBackup(string id, CancellationToken cancellationToken)
    {
        var backupPath = await databaseProvider.GetBackupFilePathAsync(id, cancellationToken).ConfigureAwait(false);

        return backupPath is null
            ? Results.NotFound()
            : Results.File(backupPath, "application/octet-stream", $"magpie-backup-{id}.db");
    }
}
