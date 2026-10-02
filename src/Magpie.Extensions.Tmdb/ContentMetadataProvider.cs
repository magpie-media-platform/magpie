using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Extensions.Tmdb.Mapping;
using Magpie.Extensions.Tmdb.QueryEngine;
using Magpie.Models;
using Magpie.Models.Data;
using Magpie.Sdk.Content;

using Microsoft.Extensions.Logging;

namespace Magpie.Extensions.Tmdb;

internal sealed class ContentMetadataProvider(
    ISettingsResolver resolver,
    ITmdbClient client,
    IQueryCoordinator queryCache,
    TimeProvider timeProvider,
    ILogger<ContentMetadataProvider> logger) : IContentHierarchyMetadataRelationshipProvider
{
    public string ProviderType => Tmdb.Constants.ProviderType;

    public IEnumerable<string> SupportedContentTypes => ["movie", "tv"];

    public async Task<ContentHierarchyMetadataResult?> TryGetMetadataAsync(
        MetadataFetchRequest request,
        CancellationToken cancellationToken = default)
    {
        var contentHierarchyItem = request.Item;
        var existingMetadata = request.ExistingMetadata;
        var ancestorLanguagePreferences = request.AncestorLanguagePreferences;
        var defaultLanguagePreference = request.DefaultLanguagePreference;
        var ancestorMetadata = request.AncestorMetadata;

        var route = contentHierarchyItem.ContentType.ResolveContentRoute(contentHierarchyItem.Level);
        if (route is null)
        {
            logger.LogDebug(
                "TMDb provider does not support content type {ContentType} at level {Level}",
                contentHierarchyItem.ContentType,
                contentHierarchyItem.Level);
            return null;
        }

        if (route is ContentRoute.TvSeason or ContentRoute.TvEpisode)
        {
            var seasonOrEpisode = await TryGetSeasonOrEpisodeMetadataAsync(
                contentHierarchyItem,
                existingMetadata,
                route.Value,
                ancestorLanguagePreferences,
                defaultLanguagePreference,
                ancestorMetadata,
                cancellationToken).ConfigureAwait(false);
            return seasonOrEpisode is null
                ? null
                : new ContentHierarchyMetadataResult { Contribution = seasonOrEpisode };
        }

        var tmdbId = existingMetadata.FindTmdbId();
        if (tmdbId is null)
        {
            logger.LogInformation("TMDb id missing for ContentHierarchyItem {Id}", contentHierarchyItem.Id);
            return null;
        }

        if (!int.TryParse(tmdbId, out _))
        {
            logger.LogInformation(
                "TMDb id {TmdbId} is not numeric for ContentHierarchyItem {Id}",
                tmdbId,
                contentHierarchyItem.Id);
            return null;
        }

        var context = await resolver.AcquireContextAsync(
            ancestorLanguagePreferences,
            defaultLanguagePreference,
            cancellationToken).ConfigureAwait(false);
        if (context is null)
        {
            return null;
        }

        var language = context.Language;
        var imageBase = await queryCache.GetImageBaseUrlAsync(
            context,
            client,
            logger,
            cancellationToken).ConfigureAwait(false);
        var utcNow = timeProvider.GetUtcNow().UtcDateTime;

        if (route == ContentRoute.Movie)
        {
            var movieQuery = new (string, string)[]
            {
                ("language", language),
                ("append_to_response", "external_ids,credits,alternative_titles,release_dates,videos,images"),
                ("include_image_language", language.IncludeImageLanguage())
            };
            var dto = await client.GetAsync<MovieDetailDto>(
                context,
                $"3/movie/{tmdbId}",
                movieQuery,
                ResponseProfile.Detail,
                cancellationToken).ConfigureAwait(false);
            return dto is null
                ? null
                : new() { Contribution = dto.ToContribution(imageBase, utcNow) };
        }

        var tvQuery = new (string, string)[]
        {
            ("language", language),
            ("append_to_response", "external_ids,credits,alternative_titles,content_ratings,videos,images"),
            ("include_image_language", language.IncludeImageLanguage())
        };
        var tvDto = await client.GetAsync<TvDetailDto>(
            context,
            $"3/tv/{tmdbId}",
            tvQuery,
            ResponseProfile.Detail,
            cancellationToken).ConfigureAwait(false);
        if (tvDto is null)
        {
            return null;
        }

        var children = await BuildChildDefinitionsAsync(context, tmdbId, imageBase, tvDto, cancellationToken)
            .ConfigureAwait(false);
        return new ContentHierarchyMetadataResult
        {
            Contribution = tvDto.ToContribution(imageBase, utcNow),
            Children = children
        };
    }

    public async Task<IReadOnlyList<SubjectRelationshipDefinition>> GetRelationshipsAsync(
        MetadataRelationshipRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var item = request.Item;
        var existingMetadata = request.ExistingMetadata;
        var ancestorMetadata = request.AncestorMetadata;

        var route = item.ContentType.ResolveContentRoute(item.Level);
        if (route is null)
        {
            return [];
        }

        var isSeasonOrEpisode = route is ContentRoute.TvSeason or ContentRoute.TvEpisode;
        var tmdbId = existingMetadata.FindTmdbId();
        if (!isSeasonOrEpisode && (tmdbId is null || !int.TryParse(tmdbId, out _)))
        {
            return [];
        }

        var context = await resolver.AcquireContextAsync(defaultLanguagePreference: null, cancellationToken)
            .ConfigureAwait(false);
        if (context is null)
        {
            return [];
        }

        var imageBase = await queryCache.GetImageBaseUrlAsync(
            context,
            client,
            logger,
            cancellationToken).ConfigureAwait(false);
        CreditsDto? credits;
        IReadOnlyList<ProductionCompanyDto>? productionCompanies = null;
        IReadOnlyList<ProductionCompanyDto>? networks = null;
        if (route == ContentRoute.Movie)
        {
            var dto = await client.GetAsync<MovieDetailDto>(
                context,
                $"3/movie/{tmdbId}",
                [("append_to_response", "credits")],
                ResponseProfile.Detail,
                cancellationToken).ConfigureAwait(false);
            credits = dto?.Credits;
            productionCompanies = dto?.ProductionCompanies;
        }
        else if (route == ContentRoute.TvShow)
        {
            var dto = await client.GetAsync<TvDetailDto>(
                context,
                $"3/tv/{tmdbId}",
                [("append_to_response", "credits")],
                ResponseProfile.Detail,
                cancellationToken).ConfigureAwait(false);
            credits = dto?.Credits;
            productionCompanies = dto?.ProductionCompanies;
            networks = dto?.Networks;
        }
        else
        {
            credits = await FetchSeasonOrEpisodeCreditsAsync(
                item,
                existingMetadata,
                route.Value,
                ancestorMetadata,
                context,
                cancellationToken).ConfigureAwait(false);
        }

        return BuildRelationships(imageBase, credits, productionCompanies, networks);
    }

    private IReadOnlyList<SubjectRelationshipDefinition> BuildRelationships(
        string imageBase,
        CreditsDto? credits,
        IReadOnlyList<ProductionCompanyDto>? productionCompanies,
        IReadOnlyList<ProductionCompanyDto>? networks)
    {
        var utcNow = timeProvider.GetUtcNow().UtcDateTime;
        var relationships = new List<SubjectRelationshipDefinition>();
        relationships.AddRange(credits.ToSubjectRelationships(imageBase, utcNow));
        relationships.AddRange(productionCompanies.ToSubjectRelationships(imageBase, relationships.Count, utcNow));
        relationships.AddRange(networks.ToNetworkSubjectRelationships(imageBase, relationships.Count, utcNow));
        return relationships;
    }

    private async Task<CreditsDto?> FetchSeasonOrEpisodeCreditsAsync(
        ContentHierarchyItem item,
        IEnumerable<MetadataContribution> existingMetadata,
        ContentRoute route,
        IReadOnlyList<MetadataContribution>? ancestorMetadata,
        RequestContext context,
        CancellationToken cancellationToken)
    {
        var path = item.ResolveCreditsPath(existingMetadata, route, ancestorMetadata, logger);
        if (path is null)
        {
            return null;
        }

        var query = new (string, string)[] { ("append_to_response", "credits") };

        if (route == ContentRoute.TvSeason)
        {
            var detail = await client.GetAsync<SeasonDetailDto>(
                context,
                path,
                query,
                ResponseProfile.Detail,
                cancellationToken).ConfigureAwait(false);
            return detail?.Credits;
        }

        var episode = await client.GetAsync<EpisodeDto>(context, path, query, ResponseProfile.Detail, cancellationToken)
            .ConfigureAwait(false);
        return episode.ToEpisodeCredits();
    }

    private async Task<IReadOnlyList<ChildHierarchyDefinition>> BuildChildDefinitionsAsync(
        RequestContext context,
        string seriesId,
        string imageBaseUrl,
        TvDetailDto tvDto,
        CancellationToken cancellationToken)
    {
        if (tvDto.Seasons is null || tvDto.Seasons.Count == 0)
        {
            return [];
        }

        var seasons = new List<ChildHierarchyDefinition>(tvDto.Seasons.Count);
        foreach (var seasonSummary in tvDto.Seasons)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (seasonSummary is null || seasonSummary.Id <= 0)
            {
                continue;
            }

            var seasonDetail = await client.GetAsync<SeasonDetailDto>(
                context,
                $"3/tv/{seriesId}/season/{seasonSummary.SeasonNumber.ToString(CultureInfo.InvariantCulture)}",
                [
                    ("language", context.Language),
                    ("append_to_response", "external_ids,images"),
                    ("include_image_language", context.Language.IncludeImageLanguage())
                ],
                ResponseProfile.Detail,
                cancellationToken).ConfigureAwait(false);
            var episodes = seasonDetail.ToEpisodeDefinitions(imageBaseUrl);

            var seasonDef = seasonSummary.ToChildHierarchyDefinition(imageBaseUrl, episodes, seasonDetail?.ExternalIds);
            if (seasonDef is not null)
            {
                seasons.Add(seasonDef);
            }
        }

        return seasons;
    }

    private async Task<MetadataContribution?> TryGetSeasonOrEpisodeMetadataAsync(
        ContentHierarchyItem item,
        IEnumerable<MetadataContribution> existingMetadata,
        ContentRoute route,
        IEnumerable<string>? ancestorLanguagePreferences,
        string defaultLanguagePreference,
        IReadOnlyList<MetadataContribution>? ancestorMetadata,
        CancellationToken cancellationToken)
    {
        var path = item.ResolveCreditsPath(existingMetadata, route, ancestorMetadata, logger);
        if (path is null)
        {
            return null;
        }

        var context = await resolver.AcquireContextAsync(
            ancestorLanguagePreferences,
            defaultLanguagePreference,
            cancellationToken).ConfigureAwait(false);
        if (context is null)
        {
            return null;
        }

        var language = context.Language;
        var imageBase = await queryCache.GetImageBaseUrlAsync(
            context,
            client,
            logger,
            cancellationToken).ConfigureAwait(false);
        var query = new (string, string)[]
        {
            ("language", language),
            ("append_to_response", "credits,external_ids,images"),
            ("include_image_language", language.IncludeImageLanguage())
        };

        if (route == ContentRoute.TvSeason)
        {
            var detail = await client.GetAsync<SeasonDetailDto>(
                context,
                path,
                query,
                ResponseProfile.Detail,
                cancellationToken).ConfigureAwait(false);
            return detail?.ToSeasonContribution(imageBase);
        }

        var episode = await client.GetAsync<EpisodeDto>(context, path, query, ResponseProfile.Detail, cancellationToken)
            .ConfigureAwait(false);
        return episode?.ToEpisodeContribution(imageBase);
    }
}
