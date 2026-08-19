using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Extensions.Tmdb.Mapping;
using Magpie.Extensions.Tmdb.QueryEngine;
using Magpie.Models.Data;
using Magpie.Sdk.Content;

using Microsoft.Extensions.Logging;

namespace Magpie.Extensions.Tmdb;

internal sealed class SubjectMetadataProvider(
    ISettingsResolver resolver,
    ITmdbClient client,
    IQueryCoordinator queryCache,
    TimeProvider timeProvider,
    ILogger<SubjectMetadataProvider> logger) : ISubjectMetadataProvider
{
    public IEnumerable<string> SupportedSubjectKinds
    {
        get
        {
            return [Constants.Person, Constants.Studio, Constants.Network];
        }
    }

    public async Task<MetadataContribution?> TryGetMetadataAsync(
        Subject subject,
        IEnumerable<MetadataContribution> existingMetadata,
        string defaultLanguagePreference,
        CancellationToken cancellationToken = default)
    {
        if (subject.Kind is not (Constants.Person or Constants.Studio or Constants.Network))
        {
            logger.LogDebug("TMDb subject provider does not support kind {Kind}", subject.Kind);
            return null;
        }

        var tmdbId = existingMetadata.FindTmdbId();
        if (tmdbId is null)
        {
            logger.LogDebug("TMDb subject provider has no TMDb id for Subject {Id}", subject.Id);
            return null;
        }

        if (!int.TryParse(tmdbId, out _))
        {
            logger.LogDebug(
                "TMDb subject provider found a non-numeric TMDb id {TmdbId} for Subject {Id}",
                tmdbId,
                subject.Id);
            return null;
        }

        var context = await resolver.AcquireContextAsync(defaultLanguagePreference, cancellationToken)
            .ConfigureAwait(false);
        if (context is null)
        {
            return null;
        }

        return subject.Kind == Constants.Person
            ? await GetPersonContributionAsync(context, tmdbId, cancellationToken).ConfigureAwait(false)
            : await GetCompanyContributionAsync(context, subject.Kind, tmdbId, cancellationToken)
                .ConfigureAwait(false);
    }

    private async Task<MetadataContribution?> GetPersonContributionAsync(
        RequestContext context,
        string tmdbId,
        CancellationToken cancellationToken)
    {
        var queryParameters = new (string, string)[]
        {
            ("language", context.Language),
            ("append_to_response", "external_ids,images")
        };

        var dto = await client.GetAsync<PersonDetailDto>(
            context,
            $"3/person/{tmdbId}",
            queryParameters,
            ResponseProfile.Detail,
            cancellationToken).ConfigureAwait(false);
        if (dto is null)
        {
            return null;
        }

        var imageBase = await queryCache.GetImageBaseUrlAsync(
            context,
            client,
            logger,
            cancellationToken).ConfigureAwait(false);
        return dto.ToContribution(imageBase, timeProvider.GetUtcNow().UtcDateTime);
    }

    private async Task<MetadataContribution?> GetCompanyContributionAsync(
        RequestContext context,
        string subjectKind,
        string tmdbId,
        CancellationToken cancellationToken)
    {
        var network = subjectKind == Constants.Network;
        var dto = await client.GetAsync<CompanyDetailDto>(
            context,
            network ? $"3/network/{tmdbId}" : $"3/company/{tmdbId}",
            queryParameters: null,
            ResponseProfile.Detail,
            cancellationToken).ConfigureAwait(false);
        if (dto is null)
        {
            return null;
        }

        var imageBase = await queryCache.GetImageBaseUrlAsync(
            context,
            client,
            logger,
            cancellationToken).ConfigureAwait(false);
        return dto.ToContribution(subjectKind, imageBase, timeProvider.GetUtcNow().UtcDateTime);
    }
}
