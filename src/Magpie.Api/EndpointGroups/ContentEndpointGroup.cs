using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Models.Api;
using Magpie.Models.Arguments;
using Magpie.Models.Bulk;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Filtering;
using Magpie.Models.Integrations;
using Magpie.Models.Paging;
using Magpie.Models.Releases;
using Magpie.Sdk.Api;
using Magpie.Sdk.Services;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Magpie.Api.EndpointGroups;

[MagpieCommandGroup("content", "content", "Manage library content")]
internal sealed class ContentEndpointGroup : IApiEndpointGroup
{
    public string Name => "content";

    public void MapEndpoints(IEndpointRouteBuilder builder)
    {
        builder.MapGet("", QueryContentAsync)
            .WithName(nameof(QueryContentAsync))
            .Produces<PagedList<ContentHierarchyItem>>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .RequireViewer();

        builder.MapGet("stats", GetContentStatsAsync)
            .WithName(nameof(GetContentStatsAsync))
            .Produces<ContentStats>()
            .RequireViewer();

        builder.MapGet("{id:guid}", GetContentByIdAsync)
            .WithName(nameof(GetContentByIdAsync))
            .Produces<ContentHierarchyItem>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireViewer();

        builder.MapGet("{id:guid}/children", GetContentChildrenAsync)
            .WithName(nameof(GetContentChildrenAsync))
            .Produces<PagedList<ContentHierarchyItem>>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireViewer();

        builder.MapPatch("{id:guid}", PatchContentAsync)
            .WithName(nameof(PatchContentAsync))
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireManager();

        builder.MapDelete("{id:guid}", DeleteContentAsync)
            .WithName(nameof(DeleteContentAsync))
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireManager();

        builder.MapPatch("bulk", BulkPatchContentAsync)
            .WithName(nameof(BulkPatchContentAsync))
            .Produces<BulkResult>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .RequireManager();

        builder.MapPost("bulk/delete", BulkDeleteContentAsync)
            .WithName(nameof(BulkDeleteContentAsync))
            .Produces<BulkResult>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .RequireManager();

        builder.MapPost("from-external-id", HydrateExternalContentAsync)
            .WithName(nameof(HydrateExternalContentAsync))
            .Produces<Guid>(StatusCodes.Status201Created)
            .Produces<Guid>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .RequireRequester();

        builder.MapPost("preview-by-external-id", PreviewByExternalIdAsync)
            .WithName(nameof(PreviewByExternalIdAsync))
            .Produces<ContentPreview>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireRequester();

        builder.MapDelete("{id:guid}/files/{mediaFileId:guid}", DeleteContentFileAsync)
            .WithName(nameof(DeleteContentFileAsync))
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireManager();

        builder.MapGet("{id:guid}/files/browser-location", GetContentFileBrowserLocationAsync)
            .WithName(nameof(GetContentFileBrowserLocationAsync))
            .Produces<ContentFileBrowserLocation>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireManager();

        builder.MapPost("{id:guid}/files", AttachContentFileAsync)
            .WithName(nameof(AttachContentFileAsync))
            .Produces<ContentFileAttachmentResult>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireManager();

        builder.MapPost("{id:guid}/files/{mediaFileId:guid}/unlink", UnlinkContentFileAsync)
            .WithName(nameof(UnlinkContentFileAsync))
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireManager();

        builder.MapGet("{id:guid}/media-server-links", GetMediaServerLinksAsync)
            .WithName(nameof(GetMediaServerLinksAsync))
            .Produces<IReadOnlyList<MediaServerDeepLink>>()
            .RequireViewer();

        builder.MapGet("{id:guid}/metadata", GetContentMetadataAsync)
            .WithName(nameof(GetContentMetadataAsync))
            .Produces<MaterializedMetadata>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireViewer();

        builder.MapGet("{id:guid}/metadata/contributions", GetContentContributionsAsync)
            .WithName(nameof(GetContentContributionsAsync))
            .Produces<IReadOnlyList<MetadataContribution>>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireViewer();

        builder.MapPatch("{id:guid}/metadata", PatchContentMetadataAsync)
            .WithName(nameof(PatchContentMetadataAsync))
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireManager();

        builder.MapGet("{id:guid}/releases", GetContentReleasesAsync)
            .WithName(nameof(GetContentReleasesAsync))
            .Produces<IReadOnlyList<ContentRelease>>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireViewer();
    }

    [MagpieCommand("list", "GET", "", "List library content")]
    internal async Task<IResult> QueryContentAsync(
        [FromServices] IFilterService filterService,
        [FromServices] IContentService contentService,
        string? filter,
        string? contentType,
        string? level,
        Guid? parentId,
        bool? topLevelOnly,
        Guid? rootFolderId,
        bool? monitoring,
        FulfillmentStatus? fulfillment,
        string? search,
        string? tag,
        int? pageNumber,
        int? pageSize,
        string? sortBy,
        SortDirection? sortDirection,
        CancellationToken cancellationToken)
    {
        var (parsed, error) = ParseFilter(filterService, filter);
        if (error is not null)
        {
            return Results.BadRequest(error);
        }

        var query = new ContentQuery
        {
            Criteria = parsed,
            ContentType = contentType,
            Level = level,
            ParentId = parentId,
            TopLevelOnly = topLevelOnly,
            RootFolderId = rootFolderId,
            Monitoring = monitoring,
            Fulfillment = fulfillment,
            Search = search,
            Tag = tag
        };

        var paging = new PagingParameter
        {
            PageNumber = pageNumber ?? 1,
            PageSize = pageSize ?? 25
        };

        var sort = sortBy is not null
            ? new SortParameter
            {
                PropertyName = sortBy,
                Direction = sortDirection ?? SortDirection.Ascending
            }
            : null;

        var result = await contentService.QueryAsync(query, paging, sort, cancellationToken)
            .ConfigureAwait(false);

        return Results.Ok(result);
    }

    [MagpieCommand("stats", "GET", "stats", "Show library content statistics")]
    internal async Task<IResult> GetContentStatsAsync(
        [FromServices] IContentService contentService,
        CancellationToken cancellationToken)
    {
        var stats = await contentService.GetStatsAsync(cancellationToken).ConfigureAwait(false);

        return Results.Ok(stats);
    }

    [MagpieCommand("get", "GET", "{id:guid}", "Show a content item")]
    internal async Task<IResult> GetContentByIdAsync(
        [FromServices] IContentService contentService,
        Guid id,
        CancellationToken cancellationToken)
    {
        var item = await contentService.GetAsync(id, cancellationToken).ConfigureAwait(false);

        return item is null ? Results.NotFound() : Results.Ok(item);
    }

    [MagpieCommand("children", "GET", "{id:guid}/children", "List children of a content item")]
    internal async Task<IResult> GetContentChildrenAsync(
        [FromServices] IContentService contentService,
        Guid id,
        int? pageNumber,
        int? pageSize,
        CancellationToken cancellationToken)
    {
        var paging = new PagingParameter
        {
            PageNumber = pageNumber ?? 1,
            PageSize = pageSize ?? 25
        };

        var result = await contentService.GetChildrenAsync(id, paging, cancellationToken)
            .ConfigureAwait(false);

        return result is null ? Results.NotFound() : Results.Ok(result);
    }

    [MagpieCommand("patch", "PATCH", "{id:guid}", "Update fields on a content item")]
    internal async Task<IResult> PatchContentAsync(
        [FromServices] IContentService contentService,
        AuthenticatedUser user,
        Guid id,
        [FromBody] ContentHierarchyItemPatch patch,
        CancellationToken cancellationToken)
    {
        var patched = await contentService.PatchAsync(id, patch, user.Id, cancellationToken)
            .ConfigureAwait(false);

        return patched ? Results.NoContent() : Results.NotFound();
    }

    internal async Task<IResult> DeleteContentAsync(
        [FromServices] IContentService contentService,
        AuthenticatedUser user,
        Guid id,
        bool? deleteFromLibrary,
        CancellationToken cancellationToken)
    {
        var deleted = await contentService
            .DeleteAsync(id, deleteFromLibrary ?? false, user.Id, cancellationToken)
            .ConfigureAwait(false);

        return deleted ? Results.NoContent() : Results.NotFound();
    }

    [MagpieCommand("bulk-patch", "PATCH", "bulk", "Update fields on multiple content items")]
    internal async Task<IResult> BulkPatchContentAsync(
        [FromServices] IFilterService filterService,
        [FromServices] IContentService contentService,
        AuthenticatedUser user,
        [FromBody] BulkContentPatchRequest request,
        CancellationToken cancellationToken)
    {
        var (filterCriteria, error) = ParseFilter(filterService, request.Filter);
        if (error is not null)
        {
            return Results.BadRequest(error);
        }

        var patch = new BulkPatch<ContentFilterCriteria, ContentHierarchyItemPatch>
        {
            Ids = request.Ids,
            Filter = filterCriteria,
            Patch = request.Patch
        };

        var result = await contentService.BulkPatchAsync(patch, user.Id, cancellationToken)
            .ConfigureAwait(false);

        return Results.Ok(result);
    }

    [MagpieCommand("bulk-delete", "POST", "bulk/delete", "Delete multiple content items")]
    internal async Task<IResult> BulkDeleteContentAsync(
        [FromServices] IFilterService filterService,
        [FromServices] IContentService contentService,
        AuthenticatedUser user,
        [FromBody] BulkContentDeleteRequest request,
        CancellationToken cancellationToken)
    {
        var (filterCriteria, error) = ParseFilter(filterService, request.Filter);
        if (error is not null)
        {
            return Results.BadRequest(error);
        }

        var action = new BulkAction<ContentFilterCriteria>
        {
            Ids = request.Ids,
            Filter = filterCriteria
        };

        var result = await contentService.BulkDeleteAsync(action, user.Id, cancellationToken)
            .ConfigureAwait(false);

        return Results.Ok(result);
    }

    [MagpieCommand("from-external-id", "POST", "from-external-id", "Create content from an external metadata id")]
    internal async Task<IResult> HydrateExternalContentAsync(
        [FromServices] IContentService contentService,
        AuthenticatedUser user,
        [FromBody] PreviewByExternalIdRequest request,
        Guid? rootFolderId,
        bool? monitoring,
        Guid? qualityTierListId,
        Guid? requestedViaWishlistId,
        [FromQuery] string[]? tags,
        CancellationToken cancellationToken)
    {
        var result = await contentService.HydrateFromExternalIdAsync(
            request.ProviderType,
            request.ExternalIdKey,
            request.ExternalId,
            request.ContentType,
            request.LevelSlug,
            rootFolderId,
            monitoring,
            qualityTierListId,
            requestedViaWishlistId,
            tags,
            user.Id,
            cancellationToken)
            .ConfigureAwait(false);

        if (result is null)
        {
            return Results.NotFound();
        }

        return Results.Ok(result.Id);
    }

    internal async Task<IResult> PreviewByExternalIdAsync(
        [FromServices] IMetadataSearchService metadataSearchService,
        PreviewByExternalIdRequest request,
        CancellationToken cancellationToken)
    {
        var preview = await metadataSearchService
            .PreviewByExternalIdAsync(
                request.ProviderType,
                request.ExternalIdKey,
                request.ExternalId,
                request.ContentType,
                request.LevelSlug,
                cancellationToken)
            .ConfigureAwait(false);

        return preview is null ? Results.NotFound() : Results.Ok(preview);
    }

    internal async Task<IResult> DeleteContentFileAsync(
        [FromServices] ILibraryImportService libraryImportService,
        AuthenticatedUser user,
        Guid id,
        Guid mediaFileId,
        CancellationToken cancellationToken)
    {
        var deleted = await libraryImportService.DeleteMediaFileAsync(id, mediaFileId, user.Id, cancellationToken)
            .ConfigureAwait(false);

        return deleted ? Results.NoContent() : Results.NotFound();
    }

    internal async Task<IResult> GetContentFileBrowserLocationAsync(
        [FromServices] IContentService contentService,
        Guid id,
        CancellationToken cancellationToken)
    {
        var location = await contentService.GetFileBrowserLocationAsync(id, cancellationToken)
            .ConfigureAwait(false);
        return location is null ? Results.NotFound() : Results.Ok(location);
    }

    internal async Task<IResult> AttachContentFileAsync(
        [FromServices] IContentService contentService,
        AuthenticatedUser user,
        Guid id,
        [FromBody] AttachContentFileRequest request,
        CancellationToken cancellationToken)
    {
        var result = await contentService.AttachFileAsync(id, request.FilePath, user.Id, cancellationToken)
            .ConfigureAwait(false);
        if (result is null)
        {
            return Results.NotFound();
        }

        return Results.Ok(result);
    }

    internal async Task<IResult> UnlinkContentFileAsync(
        [FromServices] IContentService contentService,
        AuthenticatedUser user,
        Guid id,
        Guid mediaFileId,
        CancellationToken cancellationToken)
    {
        var unlinked = await contentService.UnlinkFileAsync(id, mediaFileId, user.Id, cancellationToken)
            .ConfigureAwait(false);
        if (!unlinked)
        {
            return Results.NotFound();
        }

        return Results.NoContent();
    }

    internal async Task<IResult> GetMediaServerLinksAsync(
        [FromServices] IMediaServerService mediaServerService,
        Guid id,
        CancellationToken cancellationToken)
    {
        var links = await mediaServerService.GetContentLinksAsync(id, cancellationToken)
            .ConfigureAwait(false);

        return Results.Ok(links);
    }

    [MagpieCommand("metadata", "GET", "{id:guid}/metadata", "Show merged metadata for a content item")]
    internal async Task<IResult> GetContentMetadataAsync(
        [FromServices] IMetadataService metadataService,
        Guid id,
        CancellationToken cancellationToken)
    {
        var metadata = await metadataService.GetContentMetadataAsync(id, cancellationToken)
            .ConfigureAwait(false);

        return metadata is null ? Results.NotFound() : Results.Ok(metadata);
    }

    [MagpieCommand(
        "metadata-contributions",
        "GET",
        "{id:guid}/metadata/contributions",
        "List metadata contributions for a content item")]
    internal async Task<IResult> GetContentContributionsAsync(
        [FromServices] IMetadataService metadataService,
        Guid id,
        CancellationToken cancellationToken)
    {
        var contributions = await metadataService.GetContentContributionsAsync(id, cancellationToken)
            .ConfigureAwait(false);

        return contributions is null ? Results.NotFound() : Results.Ok(contributions);
    }

    internal async Task<IResult> PatchContentMetadataAsync(
        [FromServices] IMetadataService metadataService,
        AuthenticatedUser user,
        Guid id,
        MetadataContainerPatch patch,
        CancellationToken cancellationToken)
    {
        var patched = await metadataService
            .PatchContentContributionAsync(id, patch, user.Id, cancellationToken)
            .ConfigureAwait(false);

        return patched ? Results.NoContent() : Results.NotFound();
    }

    [MagpieCommand("releases", "GET", "{id:guid}/releases", "List releases for a content item")]
    internal async Task<IResult> GetContentReleasesAsync(
        [FromServices] IReleaseService releaseService,
        Guid id,
        CancellationToken cancellationToken)
    {
        var releases = await releaseService.GetReleaseViewAsync(id, cancellationToken)
            .ConfigureAwait(false);

        return releases is null ? Results.NotFound() : Results.Ok(releases);
    }

    private static (ContentFilterCriteria? Criteria, MagpieQlParseError? Error) ParseFilter(
        IFilterService filterService,
        string? filter)
    {
        if (string.IsNullOrWhiteSpace(filter))
        {
            return (null, null);
        }

        var parseResult = filterService.ParseContentCriteria(filter);
        if (!parseResult.IsSuccess)
        {
            return (null, parseResult.Error);
        }

        return (parseResult.Value, null);
    }
}

