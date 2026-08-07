using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Extensions.MusicBrainz.Mapping;
using Magpie.Extensions.MusicBrainz.QueryEngine;
using Magpie.Models;
using Magpie.Models.Data;
using Magpie.Sdk.Content;

using Microsoft.Extensions.Logging;

namespace Magpie.Extensions.MusicBrainz;

internal sealed class ContentMetadataProvider(
    ISettingsResolver settingsResolver,
    IMusicBrainzClient client,
    ICoverArtArchiveClient coverClient,
    IQueryCache cache,
    TimeProvider timeProvider,
    ILogger<ContentMetadataProvider> logger) : IContentHierarchyMetadataRelationshipProvider
{
    private const int BrowseLimit = 100;
    private const int MaximumReleaseGroups = 500;
    private const int CoverSize = 500;
    private const int MaximumLabelsPerExpansion = 25;
    private const string WebsiteDefault = "website-default";

    public string ProviderType => Constants.ProviderType;

    public IEnumerable<string> SupportedContentTypes => [Constants.ContentTypeSlug];

    public async Task<ContentHierarchyMetadataResult?> TryGetMetadataAsync(
        MetadataFetchRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!string.Equals(request.Item.ContentType, Constants.ContentTypeSlug, StringComparison.Ordinal))
        {
            return null;
        }

        var settings = await settingsResolver.ResolveAsync(cancellationToken).ConfigureAwait(false);
        if (string.Equals(request.Item.Level, Constants.ArtistLevelSlug, StringComparison.Ordinal))
        {
            return await ArtistResultAsync(request, settings, cancellationToken).ConfigureAwait(false);
        }

        if (string.Equals(request.Item.Level, Constants.AlbumLevelSlug, StringComparison.Ordinal))
        {
            return await AlbumResultAsync(request, settings, cancellationToken).ConfigureAwait(false);
        }

        if (string.Equals(request.Item.Level, Constants.TrackLevelSlug, StringComparison.Ordinal))
        {
            return await TrackResultAsync(request, settings, cancellationToken).ConfigureAwait(false);
        }

        return null;
    }

    public async Task<IReadOnlyList<SubjectRelationshipDefinition>> GetRelationshipsAsync(
        MetadataRelationshipRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!string.Equals(request.Item.ContentType, Constants.ContentTypeSlug, StringComparison.Ordinal))
        {
            return [];
        }

        if (string.Equals(request.Item.Level, Constants.AlbumLevelSlug, StringComparison.Ordinal))
        {
            return await ExpandAlbumAsync(
                request.Contribution,
                request.ExistingMetadata,
                cancellationToken).ConfigureAwait(false);
        }

        if (string.Equals(request.Item.Level, Constants.TrackLevelSlug, StringComparison.Ordinal))
        {
            return await ExpandTrackAsync(
                request.Contribution,
                request.ExistingMetadata,
                cancellationToken).ConfigureAwait(false);
        }

        return [];
    }

    private async Task<IReadOnlyList<SubjectRelationshipDefinition>> ExpandAlbumAsync(
        MetadataContribution contribution,
        IEnumerable<MetadataContribution> existingMetadata,
        CancellationToken cancellationToken)
    {
        var mbid = new[] { contribution }.Concat(existingMetadata).FindExternalId(Constants.ProviderType);
        if (mbid is null)
        {
            return [];
        }

        var settings = await settingsResolver.ResolveAsync(cancellationToken).ConfigureAwait(false);
        var groupEntity = await GetEntityAsync(
            EntityKind.ReleaseGroup,
            mbid,
            LookupProfile.Hydration,
            settings,
            cancellationToken).ConfigureAwait(false);
        if (groupEntity?.ReleaseGroup is not { } releaseGroup)
        {
            return [];
        }

        var representative = releaseGroup.SelectRepresentativeRelease();
        if (representative?.Id is not { } releaseMbid || !Guid.TryParse(releaseMbid, out _))
        {
            return releaseGroup.AlbumSubjectRelationships(null, timeProvider.GetUtcNow().UtcDateTime);
        }

        var releaseEntity = await GetEntityAsync(
            EntityKind.Release,
            releaseMbid,
            LookupProfile.Hydration,
            settings,
            cancellationToken).ConfigureAwait(false);
        var release = releaseEntity?.Release;
        var labels = await GetLabelsAsync(release, settings, cancellationToken).ConfigureAwait(false);
        return releaseGroup.AlbumSubjectRelationships(release, timeProvider.GetUtcNow().UtcDateTime, labels);
    }

    private async Task<IReadOnlyList<SubjectRelationshipDefinition>> ExpandTrackAsync(
        MetadataContribution contribution,
        IEnumerable<MetadataContribution> existingMetadata,
        CancellationToken cancellationToken)
    {
        var mbid = new[] { contribution }.Concat(existingMetadata).FindExternalId(Constants.ProviderType);
        if (mbid is null)
        {
            return [];
        }

        var settings = await settingsResolver.ResolveAsync(cancellationToken).ConfigureAwait(false);
        var entity = await GetEntityAsync(
            EntityKind.Recording,
            mbid,
            LookupProfile.Hydration,
            settings,
            cancellationToken).ConfigureAwait(false);
        return entity?.Recording is { } recording
            ? recording.TrackSubjectRelationships(timeProvider.GetUtcNow().UtcDateTime)
            : [];
    }

    private async Task<IReadOnlyDictionary<string, LabelDto>> GetLabelsAsync(
        ReleaseDto? release,
        Settings settings,
        CancellationToken cancellationToken)
    {
        var labels = new Dictionary<string, LabelDto>(StringComparer.OrdinalIgnoreCase);
        foreach (var mbid in release.LabelReferences()
            .Select(reference => reference.Mbid)
            .Where(value => value is not null)
            .Select(value => value!)
            .Take(MaximumLabelsPerExpansion))
        {
            var entity = await GetEntityAsync(
                EntityKind.Label,
                mbid,
                LookupProfile.Hydration,
                settings,
                cancellationToken).ConfigureAwait(false);
            if (entity?.Label is { } label)
            {
                labels[mbid] = label;
            }
        }

        return labels;
    }

    private async Task<ContentHierarchyMetadataResult?> ArtistResultAsync(
        MetadataFetchRequest request,
        Settings settings,
        CancellationToken cancellationToken)
    {
        var mbid = request.ExistingMetadata.FindExternalId(Constants.ProviderType);
        if (mbid is null)
        {
            return null;
        }

        var entity = await GetEntityAsync(
            EntityKind.Artist,
            mbid,
            LookupProfile.Hydration,
            settings,
            cancellationToken).ConfigureAwait(false);
        if (entity?.Artist is not { } artist)
        {
            return null;
        }

        var releaseGroups = new List<ReleaseGroupDto>();
        var offset = 0;
        var hasMore = false;
        while (releaseGroups.Count < MaximumReleaseGroups)
        {
            var key = new BrowseCacheKey
            {
                ArtistMbid = mbid,
                StatusFilter = WebsiteDefault,
                Limit = BrowseLimit,
                Offset = offset
            };
            var fetched = await cache.GetBrowsePageAsync(
                key,
                token => client.BrowseReleaseGroupsAsync(key, settings, token),
                cancellationToken).ConfigureAwait(false);
            if (fetched is not { Outcome: FetchOutcome.Success, Value: { } page })
            {
                return null;
            }

            var values = page.ReleaseGroups ?? [];
            releaseGroups.AddRange(values.Take(MaximumReleaseGroups - releaseGroups.Count));
            offset = (page.ReleaseGroupOffset ?? page.Offset) + values.Count;
            var count = page.ReleaseGroupCount ?? page.Count;
            hasMore = values.Count > 0 && offset < count;
            if (values.Count == 0 || offset >= count)
            {
                break;
            }
        }

        if (releaseGroups.Count >= MaximumReleaseGroups && hasMore)
        {
            logger.LogWarning(
                "The extension kept only the first {Limit} release groups of this artist and " + "ignored the rest.",
                MaximumReleaseGroups);
        }

        return new ContentHierarchyMetadataResult
        {
            Contribution = artist.ArtistContribution(timeProvider.GetUtcNow().UtcDateTime),
            Children = releaseGroups.ReleaseGroupChildren()
        };
    }

    private async Task<ContentHierarchyMetadataResult?> AlbumResultAsync(
        MetadataFetchRequest request,
        Settings settings,
        CancellationToken cancellationToken)
    {
        var mbid = request.ExistingMetadata.FindExternalId(Constants.ProviderType);
        if (mbid is null)
        {
            return null;
        }

        var groupEntity = await GetEntityAsync(
            EntityKind.ReleaseGroup,
            mbid,
            LookupProfile.Hydration,
            settings,
            cancellationToken).ConfigureAwait(false);
        if (groupEntity?.ReleaseGroup is not { } releaseGroup)
        {
            return null;
        }

        var representative = releaseGroup.SelectRepresentativeRelease();
        var release = default(ReleaseDto?);
        if (representative?.Id is { } releaseMbid && Guid.TryParse(releaseMbid, out _))
        {
            var releaseEntity = await GetEntityAsync(
                EntityKind.Release,
                releaseMbid,
                LookupProfile.Hydration,
                settings,
                cancellationToken).ConfigureAwait(false);
            release = releaseEntity?.Release;
        }

        var coverKey = new CoverCacheKey { ReleaseGroupMbid = mbid, ThumbnailSize = CoverSize };
        var cover = await cache.GetCoverAsync(
            coverKey,
            token => coverClient.GetReleaseGroupFrontAsync(mbid, CoverSize, settings, token),
            cancellationToken).ConfigureAwait(false);
        var coverUrl = cover.Outcome == FetchOutcome.Success ? cover.Value : null;
        return new ContentHierarchyMetadataResult
        {
            Contribution = releaseGroup.AlbumContribution(release, coverUrl, timeProvider.GetUtcNow().UtcDateTime),
            Children = release.TrackChildren()
        };
    }

    private async Task<ContentHierarchyMetadataResult?> TrackResultAsync(
        MetadataFetchRequest request,
        Settings settings,
        CancellationToken cancellationToken)
    {
        var mbid = request.ExistingMetadata.FindExternalId(Constants.ProviderType);
        if (mbid is null)
        {
            return null;
        }

        var entity = await GetEntityAsync(
            EntityKind.Recording,
            mbid,
            LookupProfile.Hydration,
            settings,
            cancellationToken).ConfigureAwait(false);
        if (entity?.Recording is not { } recording)
        {
            return null;
        }

        return new ContentHierarchyMetadataResult
        {
            Contribution = recording.RecordingContribution(timeProvider.GetUtcNow().UtcDateTime)
        };
    }

    private async Task<EntityResponseDto?> GetEntityAsync(
        EntityKind kind,
        string mbid,
        LookupProfile profile,
        Settings settings,
        CancellationToken cancellationToken)
    {
        var key = new EntityCacheKey { Kind = kind, Mbid = mbid, Profile = profile };
        var fetched = await cache.GetEntityAsync(
            key,
            token => client.GetEntityAsync(key, settings, token),
            cancellationToken).ConfigureAwait(false);
        return fetched.Outcome == FetchOutcome.Success ? fetched.Value : null;
    }
}
