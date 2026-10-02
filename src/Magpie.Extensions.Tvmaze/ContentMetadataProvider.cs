using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Extensions.Tvmaze.Mapping;
using Magpie.Models;
using Magpie.Models.Data;
using Magpie.Sdk.Content;

using Microsoft.Extensions.Logging;

namespace Magpie.Extensions.Tvmaze;

internal sealed class ContentMetadataProvider(
    IClient client,
    TimeProvider timeProvider,
    ILogger<ContentMetadataProvider> logger) : IContentHierarchyMetadataRelationshipProvider
{
    private const string TvContentType = "tv";
    private const string ShowLevel = "show";
    private const string SeasonLevel = "season";
    private const string EpisodeLevel = "episode";

    private static readonly string[] ShowEmbeds = ["episodes", "images", "akas", "seasons"];

    public string ProviderType => Constants.ProviderType;

    public IEnumerable<string> SupportedContentTypes => [TvContentType];

    private async Task<ShowDto?> LookupAndHydrateAsync(
        string externalSource,
        string externalValue,
        CancellationToken cancellationToken)
    {
        var matched = await client.LookupShowAsync(externalSource, externalValue, cancellationToken)
            .ConfigureAwait(false);
        if (matched is null || matched.Id <= 0)
        {
            return null;
        }

        return await client.GetShowWithEmbedsAsync(matched.Id, ShowEmbeds, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<ContentHierarchyMetadataResult?> TryGetMetadataAsync(
        MetadataFetchRequest request,
        CancellationToken cancellationToken = default)
    {
        var item = request.Item;
        if (item.ContentType != TvContentType)
        {
            logger.LogDebug(
                "TVmaze does not support content type {ContentType} at level {Level}",
                item.ContentType,
                item.Level);
            return null;
        }

        if (item.Level == EpisodeLevel)
        {
            return await TryGetEpisodeMetadataAsync(request.ExistingMetadata, cancellationToken)
                .ConfigureAwait(false);
        }

        if (item.Level != ShowLevel)
        {
            logger.LogDebug(
                "TVmaze does not support content type {ContentType} at level {Level}",
                item.ContentType,
                item.Level);
            return null;
        }

        var show = await ResolveShowAsync(request.ExistingMetadata, cancellationToken).ConfigureAwait(false);
        if (show is null)
        {
            logger.LogInformation("TVmaze resolved no show for ContentHierarchyItem {Id}", item.Id);
            return null;
        }

        var images = show.Embedded?.Images ?? [];
        var akas = show.Embedded?.Akas ?? [];
        var seasons = show.Embedded?.Seasons ?? [];
        var episodes = show.Embedded?.Episodes ?? [];

        var contribution = show.ToContribution(images, akas, timeProvider.GetUtcNow().UtcDateTime);
        var children = seasons.BuildSeasonDefinitions(episodes);

        return new ContentHierarchyMetadataResult { Contribution = contribution, Children = children };
    }

    public async Task<IReadOnlyList<SubjectRelationshipDefinition>> GetRelationshipsAsync(
        MetadataRelationshipRequest request,
        CancellationToken cancellationToken = default)
    {
        var item = request.Item;
        if (item.ContentType != TvContentType)
        {
            return [];
        }

        if (item.Level == SeasonLevel)
        {
            return [];
        }

        if (item.Level == EpisodeLevel)
        {
            return await ExpandEpisodeAsync(
                request.Contribution,
                request.ExistingMetadata,
                cancellationToken).ConfigureAwait(false);
        }

        if (item.Level != ShowLevel)
        {
            return [];
        }

        var tvmazeId = FindExternalId(request.Contribution, request.ExistingMetadata, Constants.ProviderType);
        if (string.IsNullOrWhiteSpace(tvmazeId) ||
            !int.TryParse(tvmazeId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var showId) ||
            showId <= 0)
        {
            logger.LogInformation("TVmaze has no show id for ContentHierarchyItem {Id}", item.Id);
            return [];
        }

        var show = await client.GetShowWithEmbedsAsync(showId, ["cast", "crew"], cancellationToken)
            .ConfigureAwait(false);
        if (show is null || show.Id <= 0)
        {
            return [];
        }

        return show.ToSubjectRelationships(timeProvider.GetUtcNow().UtcDateTime);
    }

    private string? FindExternalId(
        MetadataContribution contribution,
        IEnumerable<MetadataContribution> existingMetadata,
        string source)
    {
        return new[]
        {
            contribution
        }.FindExternalIdBySource(source) ?? existingMetadata.FindExternalIdBySource(source);
    }

    private async Task<IReadOnlyList<SubjectRelationshipDefinition>> ExpandEpisodeAsync(
        MetadataContribution contribution,
        IEnumerable<MetadataContribution> existingMetadata,
        CancellationToken cancellationToken)
    {
        var tvmazeId = FindExternalId(contribution, existingMetadata, Constants.ProviderType);
        if (string.IsNullOrWhiteSpace(tvmazeId) ||
            !long.TryParse(tvmazeId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var episodeId) ||
            episodeId <= 0)
        {
            return [];
        }

        var cast = await client.GetEpisodeGuestCastAsync(episodeId, cancellationToken).ConfigureAwait(false);
        var crew = await client.GetEpisodeGuestCrewAsync(episodeId, cancellationToken).ConfigureAwait(false);
        return cast.ToGuestSubjectRelationships(crew, timeProvider.GetUtcNow().UtcDateTime);
    }

    private async Task<ContentHierarchyMetadataResult?> TryGetEpisodeMetadataAsync(
        IReadOnlyList<MetadataContribution> existingMetadata,
        CancellationToken cancellationToken)
    {
        var externalId = existingMetadata.FindExternalIdBySource(Constants.ProviderType);
        if (string.IsNullOrWhiteSpace(externalId) ||
            !long.TryParse(externalId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var episodeId) ||
            episodeId <= 0)
        {
            return null;
        }

        var episode = await client.GetEpisodeAsync(episodeId, cancellationToken).ConfigureAwait(false);
        var contribution = episode.ToEpisodeContribution(timeProvider.GetUtcNow().UtcDateTime);
        return contribution is null
            ? null
            : new ContentHierarchyMetadataResult { Contribution = contribution };
    }

    private async Task<ShowDto?> ResolveShowAsync(
        IReadOnlyList<MetadataContribution> existingMetadata,
        CancellationToken cancellationToken)
    {
        var tvmazeId = existingMetadata.FindExternalIdBySource(Constants.ProviderType);
        if (!string.IsNullOrWhiteSpace(tvmazeId) &&
            int.TryParse(tvmazeId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var showId) &&
            showId > 0)
        {
            var byId = await client.GetShowWithEmbedsAsync(showId, ShowEmbeds, cancellationToken)
                .ConfigureAwait(false);
            if (byId is not null)
            {
                return byId;
            }
        }

        var imdbId = existingMetadata.FindExternalIdBySource(Constants.ImdbSource);
        if (!string.IsNullOrWhiteSpace(imdbId))
        {
            var byImdb = await LookupAndHydrateAsync("imdb", imdbId, cancellationToken)
                .ConfigureAwait(false);
            if (byImdb is not null)
            {
                return byImdb;
            }
        }

        var theTvdbId = existingMetadata.FindExternalIdBySource(Constants.TheTvdbSource);
        if (!string.IsNullOrWhiteSpace(theTvdbId))
        {
            var byTvdb = await LookupAndHydrateAsync("thetvdb", theTvdbId, cancellationToken)
                .ConfigureAwait(false);
            if (byTvdb is not null)
            {
                return byTvdb;
            }
        }

        return null;
    }
}
