using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Models;
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

[MagpieCommandGroup("extensions", "extensions", "Manage extensions and package sources")]
internal sealed class ExtensionsEndpointGroup : IApiEndpointGroup
{
    private const string RestartRequiredHeader = "X-Magpie-Restart-Required";

    public string Name => "extensions";

    private static void SetRestartRequiredHeader(HttpContext httpContext, bool restartRequired)
    {
        httpContext.Response.Headers.Append(RestartRequiredHeader, restartRequired ? "true" : "false");
    }

    public void MapEndpoints(IEndpointRouteBuilder builder)
    {
        builder.MapGet("", GetAllExtensionsAsync)
            .WithName(nameof(GetAllExtensionsAsync))
            .Produces<IReadOnlyList<ExtensionListItem>>()
            .RequireViewer();

        builder.MapPost("", InstallExtensionAsync)
            .WithName(nameof(InstallExtensionAsync))
            .Produces<ExtensionInstallResult>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .RequireAdmin();

        builder.MapGet("search", SearchExtensionsAsync)
            .WithName(nameof(SearchExtensionsAsync))
            .Produces<IReadOnlyList<NuGetSearchResult>>()
            .RequireViewer();

        builder.MapPost("upload", UploadExtensionAsync)
            .WithName(nameof(UploadExtensionAsync))
            .Produces<ExtensionInstallResult>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .DisableAntiforgery()
            .RequireAdmin();

        builder.MapGet("package-sources", GetAllPackageSourcesAsync)
            .WithName(nameof(GetAllPackageSourcesAsync))
            .Produces<IReadOnlyList<ExtensionPackageSourceResponse>>()
            .RequireViewer();

        builder.MapPost("package-sources", CreatePackageSourceAsync)
            .WithName(nameof(CreatePackageSourceAsync))
            .Produces<ExtensionPackageSourceResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .RequireAdmin();

        builder.MapGet("package-sources/{id:guid}", GetPackageSourceByIdAsync)
            .WithName(nameof(GetPackageSourceByIdAsync))
            .Produces<ExtensionPackageSourceResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireViewer();

        builder.MapPatch("package-sources/{id:guid}", PatchPackageSourceAsync)
            .WithName(nameof(PatchPackageSourceAsync))
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireAdmin();

        builder.MapDelete("package-sources/{id:guid}", DeletePackageSourceAsync)
            .WithName(nameof(DeletePackageSourceAsync))
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireAdmin();

        builder.MapGet("{id:guid}", GetExtensionByIdAsync)
            .WithName(nameof(GetExtensionByIdAsync))
            .Produces<Extension>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireViewer();

        builder.MapPatch("{id:guid}", PatchExtensionAsync)
            .WithName(nameof(PatchExtensionAsync))
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireAdmin();

        builder.MapDelete("{id:guid}", UninstallExtensionAsync)
            .WithName(nameof(UninstallExtensionAsync))
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireAdmin();

        builder.MapPost("{id:guid}/enable", EnableExtensionAsync)
            .WithName(nameof(EnableExtensionAsync))
            .Produces<Extension>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireAdmin();

        builder.MapPost("{id:guid}/disable", DisableExtensionAsync)
            .WithName(nameof(DisableExtensionAsync))
            .Produces<Extension>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireAdmin();

        builder.MapPost("{id:guid}/upgrade/latest", UpgradeExtensionToLatestAsync)
            .WithName(nameof(UpgradeExtensionToLatestAsync))
            .Produces<ExtensionInstallResult>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireAdmin();

        builder.MapPost("{id:guid}/upgrade/{version}", UpgradeExtensionToVersionAsync)
            .WithName(nameof(UpgradeExtensionToVersionAsync))
            .Produces<ExtensionInstallResult>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireAdmin();
    }

    [MagpieCommand("list", "GET", "", "List installed extensions")]
    internal async Task<IResult> GetAllExtensionsAsync(
        [FromServices] IExtensionService extensionService,
        CancellationToken cancellationToken)
    {
        var extensions = await extensionService.ListAsync(cancellationToken).ConfigureAwait(false);

        return Results.Ok(extensions);
    }

    [MagpieCommand("get", "GET", "{id:guid}", "Show an extension")]
    internal async Task<IResult> GetExtensionByIdAsync(
        [FromServices] IExtensionService extensionService,
        Guid id,
        CancellationToken cancellationToken)
    {
        var extension = await extensionService.GetAsync(id, cancellationToken).ConfigureAwait(false);

        return extension is null ? Results.NotFound() : Results.Ok(extension);
    }

    [MagpieCommand("search", "GET", "search", "Search package sources for extensions")]
    internal async Task<IResult> SearchExtensionsAsync(
        [FromServices] IExtensionService extensionService,
        string? q,
        CancellationToken cancellationToken)
    {
        var results = await extensionService.SearchAsync(q, cancellationToken).ConfigureAwait(false);

        return Results.Ok(results);
    }

    [MagpieCommand("install", "POST", "", "Install an extension from a package source")]
    internal async Task<IResult> InstallExtensionAsync(
        [FromServices] IExtensionService extensionService,
        HttpContext httpContext,
        AuthenticatedUser user,
        [FromBody] InstallExtensionRequest request,
        CancellationToken cancellationToken)
    {
        var result = await extensionService
            .InstallFromSourceAsync(request.PackageId, request.Version, user.Id, cancellationToken)
            .ConfigureAwait(false);

        SetRestartRequiredHeader(httpContext, result.RestartRequired);

        return Results.Created($"api/{Name}/{result.Extension.Id}", result);
    }

    [MagpieCommand("upload", "POST", "upload", "Upload an extension package")]
    internal async Task<IResult> UploadExtensionAsync(
        [FromServices] IExtensionService extensionService,
        HttpContext httpContext,
        AuthenticatedUser user,
        [FromForm] IFormFile file,
        CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
        {
            return Results.Problem(
                detail: "An extension package file is required.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var stream = file.OpenReadStream();
        await using var _ = stream.ConfigureAwait(false);

        var result = await extensionService
            .UploadAsync(stream, file.FileName, user.Id, cancellationToken)
            .ConfigureAwait(false);

        SetRestartRequiredHeader(httpContext, result.RestartRequired);

        return Results.Created($"api/{Name}/{result.Extension.Id}", result);
    }

    [MagpieCommand("patch", "PATCH", "{id:guid}", "Update fields on an extension")]
    internal async Task<IResult> PatchExtensionAsync(
        [FromServices] IExtensionService extensionService,
        AuthenticatedUser user,
        Guid id,
        [FromBody] ExtensionPatch patch,
        CancellationToken cancellationToken)
    {
        var updated = await extensionService
            .PatchAsync(id, patch with { Id = id }, user.Id, cancellationToken)
            .ConfigureAwait(false);

        return updated ? Results.NoContent() : Results.NotFound();
    }

    [MagpieCommand("uninstall", "DELETE", "{id:guid}", "Uninstall an extension")]
    internal async Task<IResult> UninstallExtensionAsync(
        [FromServices] IExtensionService extensionService,
        HttpContext httpContext,
        AuthenticatedUser user,
        Guid id,
        CancellationToken cancellationToken)
    {
        var removed = await extensionService.UninstallAsync(id, user.Id, cancellationToken).ConfigureAwait(false);

        if (!removed)
        {
            return Results.NotFound();
        }

        SetRestartRequiredHeader(httpContext, true);

        return Results.NoContent();
    }

    [MagpieCommand("enable", "POST", "{id:guid}/enable", "Enable an extension")]
    internal async Task<IResult> EnableExtensionAsync(
        [FromServices] IExtensionService extensionService,
        AuthenticatedUser user,
        Guid id,
        CancellationToken cancellationToken)
    {
        var extension = await extensionService.EnableAsync(id, user.Id, cancellationToken).ConfigureAwait(false);

        return extension is null ? Results.NotFound() : Results.Ok(extension);
    }

    [MagpieCommand("disable", "POST", "{id:guid}/disable", "Disable an extension")]
    internal async Task<IResult> DisableExtensionAsync(
        [FromServices] IExtensionService extensionService,
        AuthenticatedUser user,
        Guid id,
        CancellationToken cancellationToken)
    {
        var extension = await extensionService.DisableAsync(id, user.Id, cancellationToken).ConfigureAwait(false);

        return extension is null ? Results.NotFound() : Results.Ok(extension);
    }

    [MagpieCommand("upgrade", "POST", "{id:guid}/upgrade/latest", "Upgrade an extension to the latest version")]
    internal async Task<IResult> UpgradeExtensionToLatestAsync(
        [FromServices] IExtensionService extensionService,
        HttpContext httpContext,
        AuthenticatedUser user,
        Guid id,
        CancellationToken cancellationToken)
    {
        var result = await extensionService.UpgradeToLatestAsync(id, user.Id, cancellationToken)
            .ConfigureAwait(false);

        if (result is null)
        {
            return Results.NotFound();
        }

        SetRestartRequiredHeader(httpContext, result.RestartRequired);

        return Results.Ok(result);
    }

    [MagpieCommand(
        "upgrade-version",
        "POST",
        "{id:guid}/upgrade/{version}",
        "Upgrade an extension to a specific version")]
    internal async Task<IResult> UpgradeExtensionToVersionAsync(
        [FromServices] IExtensionService extensionService,
        HttpContext httpContext,
        AuthenticatedUser user,
        Guid id,
        string version,
        CancellationToken cancellationToken)
    {
        var result = await extensionService.UpgradeToVersionAsync(id, version, user.Id, cancellationToken)
            .ConfigureAwait(false);

        if (result is null)
        {
            return Results.NotFound();
        }

        SetRestartRequiredHeader(httpContext, result.RestartRequired);

        return Results.Ok(result);
    }

    [MagpieCommand("package-sources", "GET", "package-sources", "List extension package sources")]
    internal async Task<IResult> GetAllPackageSourcesAsync(
        [FromServices] IExtensionService extensionService,
        CancellationToken cancellationToken)
    {
        var sources = await extensionService.ListPackageSourcesAsync(cancellationToken).ConfigureAwait(false);

        return Results.Ok(CreateResponse(sources));
    }

    [MagpieCommand("package-source", "GET", "package-sources/{id:guid}", "Show an extension package source")]
    internal async Task<IResult> GetPackageSourceByIdAsync(
        [FromServices] IExtensionService extensionService,
        Guid id,
        CancellationToken cancellationToken)
    {
        var source = await extensionService.GetPackageSourceAsync(id, cancellationToken).ConfigureAwait(false);

        return source is null ? Results.NotFound() : Results.Ok(CreateResponse(source));
    }

    [MagpieCommand("package-source-create", "POST", "package-sources", "Create an extension package source")]
    internal async Task<IResult> CreatePackageSourceAsync(
        [FromServices] IExtensionService extensionService,
        AuthenticatedUser user,
        [FromBody] ExtensionPackageSourceCreate create,
        CancellationToken cancellationToken)
    {
        var created = await extensionService.CreatePackageSourceAsync(create, user.Id, cancellationToken)
            .ConfigureAwait(false);

        return Results.Created($"api/{Name}/package-sources/{created.Id}", CreateResponse(created));
    }

    [MagpieCommand(
        "package-source-patch",
        "PATCH",
        "package-sources/{id:guid}",
        "Update fields on an extension package source")]
    internal async Task<IResult> PatchPackageSourceAsync(
        [FromServices] IExtensionService extensionService,
        AuthenticatedUser user,
        Guid id,
        [FromBody] ExtensionPackageSourcePatch patch,
        CancellationToken cancellationToken)
    {
        var updated = await extensionService.PatchPackageSourceAsync(id, patch, user.Id, cancellationToken)
            .ConfigureAwait(false);

        return updated ? Results.NoContent() : Results.NotFound();
    }

    [MagpieCommand(
        "package-source-delete",
        "DELETE",
        "package-sources/{id:guid}",
        "Delete an extension package source")]
    internal async Task<IResult> DeletePackageSourceAsync(
        [FromServices] IExtensionService extensionService,
        AuthenticatedUser user,
        Guid id,
        CancellationToken cancellationToken)
    {
        var deleted = await extensionService.DeletePackageSourceAsync(id, user.Id, cancellationToken)
            .ConfigureAwait(false);

        return deleted ? Results.NoContent() : Results.NotFound();
    }

    private static IReadOnlyList<ExtensionPackageSourceResponse> CreateResponse(
        IEnumerable<ExtensionPackageSource> sources) =>
        [
            .. sources.Select(CreateResponse)
        ];

    private static ExtensionPackageSourceResponse CreateResponse(ExtensionPackageSource source) => new()
    {
        Id = source.Id,
        Name = source.Name,
        SourceUrl = RedactSourceUrlCredentials(source.SourceUrl),
        Enabled = source.Enabled,
        SearchFilterKind = source.SearchFilterKind,
        SearchFilterValue = source.SearchFilterValue,
        CredentialKind = source.CredentialKind,
        Username = source.Username,
        SecretReference = source.SecretReference,
        HasSecret = !string.IsNullOrWhiteSpace(source.EncryptedSecret),
        CreatedAt = source.CreatedAt,
        ModifiedAt = source.ModifiedAt,
    };

    private static string RedactSourceUrlCredentials(string sourceUrl)
    {
        var trimmed = sourceUrl?.Trim() ?? string.Empty;
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) || string.IsNullOrEmpty(uri.UserInfo))
        {
            return trimmed;
        }

        var builder = new UriBuilder(uri)
        {
            UserName = string.Empty,
            Password = string.Empty
        };

        return builder.Uri.AbsoluteUri;
    }
}

