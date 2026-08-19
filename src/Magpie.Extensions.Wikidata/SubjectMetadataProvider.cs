using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Extensions.Wikidata.Mapping;
using Magpie.Extensions.Wikidata.QueryEngine;
using Magpie.Models.Data;
using Magpie.Sdk.Content;

namespace Magpie.Extensions.Wikidata;

internal sealed class SubjectMetadataProvider(
    ISettingsResolver settingsResolver,
    IEntityResolver entityResolver,
    IQueryCoordinator cache,
    TimeProvider timeProvider) : ISubjectMetadataProvider
{
    public IEnumerable<string> SupportedSubjectKinds => ["person", "studio"];

    public async Task<MetadataContribution?> TryGetMetadataAsync(
        Subject subject,
        IEnumerable<MetadataContribution> existingMetadata,
        string defaultLanguagePreference,
        CancellationToken cancellationToken = default)
    {
        var kind = subject.Kind switch
        {
            "person" => EntityKind.Person,
            "studio" => EntityKind.Studio,
            _ => (EntityKind?)null
        };
        if (kind is null)
        {
            return null;
        }

        var context = await settingsResolver.ResolveAsync(defaultLanguagePreference, cancellationToken)
            .ConfigureAwait(false);
        var resolution = await entityResolver.ResolveSubjectAsync(
            kind.Value,
            existingMetadata,
            context,
            cancellationToken).ConfigureAwait(false);
        if (resolution.Kind != EntityResolutionKind.Resolved || resolution.Qid is null)
        {
            return null;
        }

        var entity = await cache.GetEntityAsync(resolution.Qid, context, cancellationToken)
            .ConfigureAwait(false);
        if (entity is null)
        {
            return null;
        }

        var support = await GetSupportEntitiesAsync(entity, context, cancellationToken).ConfigureAwait(false);
        return entity.ToMetadataContribution(
            kind.Value,
            context.Language,
            support,
            timeProvider.GetUtcNow().UtcDateTime);
    }

    private async Task<IReadOnlyDictionary<string, EntityDto>> GetSupportEntitiesAsync(
        EntityDto entity,
        RequestContext context,
        CancellationToken cancellationToken)
    {
        var support = await cache.GetEntitiesAsync(
            entity.RequiredSupportQids(),
            context,
            cancellationToken).ConfigureAwait(false);
        var bodyQids = entity.RequiredAwardBodyQids(support);
        if (bodyQids.Count == 0)
        {
            return support;
        }

        var bodies = await cache.GetEntitiesAsync(bodyQids, context, cancellationToken)
            .ConfigureAwait(false);
        var merged = new Dictionary<string, EntityDto>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in support)
        {
            merged[pair.Key] = pair.Value;
        }

        foreach (var pair in bodies)
        {
            merged[pair.Key] = pair.Value;
        }

        return merged;
    }
}
