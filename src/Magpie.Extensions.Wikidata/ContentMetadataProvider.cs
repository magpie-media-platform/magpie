using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Extensions.Wikidata.Mapping;
using Magpie.Extensions.Wikidata.QueryEngine;
using Magpie.Models;
using Magpie.Models.Data;
using Magpie.Sdk.Content;

using Microsoft.Extensions.Logging;

namespace Magpie.Extensions.Wikidata;

internal sealed class ContentMetadataProvider(
    ISettingsResolver settingsResolver,
    IEntityResolver entityResolver,
    IQueryCoordinator cache,
    IQueryExecutor queryExecutor,
    TimeProvider timeProvider,
    ILogger<ContentMetadataProvider> logger) : IContentHierarchyMetadataRelationshipProvider
{
    public string ProviderType => Constants.ProviderType;

    public IEnumerable<string> SupportedContentTypes =>
        [Constants.MovieContentType, Constants.TvContentType];

    public async Task<ContentHierarchyMetadataResult?> TryGetMetadataAsync(
        MetadataFetchRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!TryKind(request.Item.ContentType, request.Item.Level, out var kind))
        {
            logger.LogDebug(
                "Wikidata provider does not support content type {ContentType} at level {Level}",
                request.Item.ContentType,
                request.Item.Level);
            return null;
        }

        var preferredLanguage = request.AncestorLanguagePreferences.FirstOrDefault(value =>
            !string.IsNullOrWhiteSpace(value)) ??
            request.DefaultLanguagePreference;
        var context = await settingsResolver.ResolveAsync(preferredLanguage, cancellationToken)
            .ConfigureAwait(false);
        var resolution = await entityResolver.ResolveContentAsync(
            kind,
            request.Item,
            request.ExistingMetadata,
            request.AncestorMetadata,
            context,
            cancellationToken).ConfigureAwait(false);
        if (resolution.Kind != EntityResolutionKind.Resolved || resolution.Qid is null)
        {
            return null;
        }

        var entity = await cache.GetEntityAsync(resolution.Qid, context, cancellationToken)
            .ConfigureAwait(false);
        if (entity is null || !entity.MatchesKind(kind))
        {
            return null;
        }

        var support = await GetSupportEntitiesAsync(entity, context, cancellationToken).ConfigureAwait(false);
        var parentQid = FindAncestorQids(request.AncestorMetadata).FirstOrDefault();
        var utcNow = timeProvider.GetUtcNow().UtcDateTime;
        var contribution = entity.ToMetadataContribution(kind, context.Language, support, utcNow, parentQid);
        if (contribution is null)
        {
            return null;
        }

        var children = await BuildChildrenAsync(
            kind,
            resolution.Qid,
            entity,
            contribution,
            context,
            utcNow,
            cancellationToken).ConfigureAwait(false);
        return new ContentHierarchyMetadataResult { Contribution = contribution, Children = children };
    }

    public async Task<IReadOnlyList<SubjectRelationshipDefinition>> GetRelationshipsAsync(
        MetadataRelationshipRequest request,
        CancellationToken cancellationToken = default)
    {
        var item = request.Item;
        if (!TryKind(item.ContentType, item.Level, out var kind))
        {
            return [];
        }

        var contributions = new[] { request.Contribution }.Concat(request.ExistingMetadata).ToArray();
        var languagePreference = item.LanguagePreferences.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))
            ?? request.Contribution.Language
            ?? Settings.FallbackLanguage;
        var context = await settingsResolver.ResolveAsync(
            languagePreference,
            cancellationToken).ConfigureAwait(false);
        var resolution = await entityResolver.ResolveContentAsync(
            kind,
            item,
            contributions,
            request.AncestorMetadata,
            context,
            cancellationToken).ConfigureAwait(false);
        if (resolution.Kind != EntityResolutionKind.Resolved || resolution.Qid is null)
        {
            return [];
        }

        var content = await cache.GetEntityAsync(resolution.Qid, context, cancellationToken)
            .ConfigureAwait(false);
        if (content is null)
        {
            return [];
        }

        var pending = content.ToPendingRelationships();
        var supportQids = pending.ToSupportQids();
        var support = await cache.GetEntitiesAsync(supportQids, context, cancellationToken)
            .ConfigureAwait(false);
        var result = new List<SubjectRelationshipDefinition>();
        foreach (var relationship in pending)
        {
            if (!support.TryGetValue(relationship.SubjectQid, out var subject) ||
                !subject.MatchesKind(relationship.SubjectKind))
            {
                continue;
            }

            var initial = subject.ToMetadataContribution(
                relationship.SubjectKind,
                context.Language,
                new Dictionary<string, EntityDto>(),
                timeProvider.GetUtcNow().UtcDateTime);
            if (initial?.Title is null)
            {
                continue;
            }

            var qualifier = relationship.Qualifier;
            if (qualifier is null &&
                relationship.CharacterQid is not null &&
                support.TryGetValue(relationship.CharacterQid, out var character))
            {
                qualifier = character.ResolveLabel(context.Language);
            }

            result.Add(new SubjectRelationshipDefinition
            {
                RelationshipKind = relationship.RelationshipKind,
                RelationshipQualifier = qualifier,
                RelationshipOrdinal = result.Count,
                SubjectKind = relationship.SubjectKind == EntityKind.Person ? "person" : "studio",
                SubjectName = initial.Title,
                SubjectExternalIds = initial.ExternalIds
                    .Where(external => external.Source is not null)
                    .ToDictionary(external => external.Source!, external => external.Value),
                InitialSubjectContribution = initial
            });
        }

        return result;
    }

    private static IReadOnlyList<string> FindAncestorQids(IEnumerable<MetadataContribution> contributions)
    {
        var qids =
            from contribution in contributions
            from external in contribution.ExternalIds
            where string.Equals(
                external.Source,
                Constants.ProviderType,
                StringComparison.OrdinalIgnoreCase)
            let value = external.Value?.Trim()
            where !string.IsNullOrWhiteSpace(value)
            select value;
        return [.. qids.Distinct(StringComparer.OrdinalIgnoreCase)];
    }

    private static bool TryKind(string contentType, string level, out EntityKind kind)
    {
        var resolved = (contentType, level) switch
        {
            (Constants.MovieContentType, Constants.MovieLevel) => EntityKind.Movie,
            (Constants.TvContentType, Constants.ShowLevel) => EntityKind.Show,
            (Constants.TvContentType, Constants.SeasonLevel) => EntityKind.Season,
            (Constants.TvContentType, Constants.EpisodeLevel) => EntityKind.Episode,
            _ => (EntityKind?)null
        };
        kind = resolved.GetValueOrDefault();
        return resolved.HasValue;
    }

    private async Task<IReadOnlyList<ChildHierarchyDefinition>> BuildChildrenAsync(
        EntityKind kind,
        string qid,
        EntityDto entity,
        MetadataContribution contribution,
        RequestContext context,
        DateTime utcNow,
        CancellationToken cancellationToken)
    {
        HierarchyResult hierarchy;
        int? seasonNumber = null;
        if (kind == EntityKind.Show)
        {
            hierarchy = await queryExecutor.GetHierarchyAsync(
                EntityKind.Season,
                qid,
                showQid: null,
                context,
                cancellationToken).ConfigureAwait(false);
        }
        else if (kind == EntityKind.Season)
        {
            var showQid = entity.FindParentQid(EntityKind.Season);
            seasonNumber = contribution.SequenceNumber;
            if (showQid is null || seasonNumber is not > 0)
            {
                return [];
            }

            hierarchy = await queryExecutor.GetHierarchyAsync(
                EntityKind.Episode,
                qid,
                showQid,
                context,
                cancellationToken).ConfigureAwait(false);
        }
        else
        {
            return [];
        }

        if (hierarchy.Truncated)
        {
            logger.LogWarning(
                "Wikidata hierarchy for {Qid} exceeded {ChildLimit} children; the rest were dropped",
                qid,
                SparqlQueryBuilder.HierarchyLimit);
        }

        return hierarchy.Rows.ToChildDefinitions(seasonNumber, utcNow);
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

        var bodies = await cache.GetEntitiesAsync(bodyQids, context, cancellationToken).ConfigureAwait(false);
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
