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
    INuGetService nuGetSearchService,
    IExtensionInstaller extensionInstaller) : IApiEndpointGroup
{
    /// <summary>
    /// Header name surfaced on install / update / uninstall responses to flag that the
    /// host process must restart before the change takes effect.
    /// </summary>
    private const string RestartRequiredHeader = "X-Magpie-Restart-Required";

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

        builder.MapPost("install", InstallExtension)
            .WithName(nameof(InstallExtension))
            .Produces<Extension>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .RequireAdmin();

        builder.MapPost("install-file", InstallExtensionFromFile)
            .WithName(nameof(InstallExtensionFromFile))
            .Produces<Extension>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .DisableAntiforgery()
            .RequireAdmin();

        builder.MapPost("check-updates", CheckExtensionUpdates)
            .WithName(nameof(CheckExtensionUpdates))
            .Produces<ExtensionUpdateInfo[]>()
            .RequireManager();

        builder.MapPost("{id:guid}/update", UpdateExtension)
            .WithName(nameof(UpdateExtension))
            .Produces<Extension>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireAdmin();

        builder.MapPost("{id:guid}/uninstall", UninstallExtension)
            .WithName(nameof(UninstallExtension))
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireAdmin();
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

    private async Task<IResult> InstallExtension(
        HttpContext httpContext,
        InstallExtensionRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request?.PackageId))
        {
            return Results.BadRequest(new { error = "packageId is required." });
        }

        var currentUserId = httpContext.User.GetUserId();
        try
        {
            var extension = await extensionInstaller
                .InstallFromNuGetAsync(request.PackageId, request.Version, currentUserId, cancellationToken)
                .ConfigureAwait(false);

            httpContext.Response.Headers.Append(RestartRequiredHeader, "true");
            return Results.Created($"/api/extensions/{extension.Id}", new InstallExtensionResponse(extension, true));
        }
        catch (InvalidOperationException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }

    private async Task<IResult> InstallExtensionFromFile(
        HttpContext httpContext,
        IFormFile file,
        CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
        {
            return Results.BadRequest(new { error = "An extension package file is required." });
        }

        var currentUserId = httpContext.User.GetUserId();
        await using var stream = file.OpenReadStream();
        try
        {
            var extension = await extensionInstaller
                .InstallFromStreamAsync(stream, file.FileName ?? "extension", currentUserId, cancellationToken)
                .ConfigureAwait(false);

            httpContext.Response.Headers.Append(RestartRequiredHeader, "true");
            return Results.Created($"/api/extensions/{extension.Id}", new InstallExtensionResponse(extension, true));
        }
        catch (InvalidOperationException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }

    private async Task<IResult> CheckExtensionUpdates(CancellationToken cancellationToken)
    {
        var updates = await extensionInstaller.CheckUpdatesAsync(cancellationToken).ConfigureAwait(false);
        return Results.Ok(updates);
    }

    private async Task<IResult> UpdateExtension(
        HttpContext httpContext,
        Guid id,
        UpdateExtensionRequest? request,
        CancellationToken cancellationToken)
    {
        var currentUserId = httpContext.User.GetUserId();
        try
        {
            var updated = await extensionInstaller
                .UpdateAsync(id, request?.Version, currentUserId, cancellationToken)
                .ConfigureAwait(false);

            if (updated is null)
            {
                return Results.NotFound();
            }

            httpContext.Response.Headers.Append(RestartRequiredHeader, "true");
            return Results.Ok(new InstallExtensionResponse(updated, true));
        }
        catch (InvalidOperationException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }

    private async Task<IResult> UninstallExtension(
        HttpContext httpContext,
        Guid id,
        CancellationToken cancellationToken)
    {
        var currentUserId = httpContext.User.GetUserId();
        var removed = await extensionInstaller.UninstallAsync(id, currentUserId, cancellationToken)
            .ConfigureAwait(false);
        if (!removed)
        {
            return Results.NotFound();
        }

        httpContext.Response.Headers.Append(RestartRequiredHeader, "true");
        return Results.NoContent();
    }
}

/// <summary>
/// Request body for <c>POST /extensions/install</c>.
/// </summary>
/// <param name="PackageId">The NuGet package id to install.</param>
/// <param name="Version">The version to install. When omitted the latest stable version is used.</param>
internal sealed record InstallExtensionRequest(string PackageId, string? Version);

/// <summary>
/// Request body for <c>POST /extensions/{id}/update</c>.
/// </summary>
/// <param name="Version">The version to install. When omitted the latest stable version is used.</param>
internal sealed record UpdateExtensionRequest(string? Version);

/// <summary>
/// Response wrapper that surfaces the installed entity together with the restart-required hint.
/// </summary>
/// <param name="Extension">The extension entity that was created or updated.</param>
/// <param name="RestartRequired">Whether the host must be restarted to load the new assembly.</param>
internal sealed record InstallExtensionResponse(Extension Extension, bool RestartRequired);
