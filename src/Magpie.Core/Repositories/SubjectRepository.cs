using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions;
using Magpie.Abstractions.Repositories;
using Magpie.Core.Events;
using Magpie.Data;
using Magpie.Models;
using Magpie.Models.Arguments;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Filtering;
using Magpie.Models.Paging;
using Magpie.Sdk.Services;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.Extensions.Logging;

namespace Magpie.Core.Repositories;

internal sealed class SubjectRepository(
    MagpieDbContext context,
    IAuditRecordRepository auditRecordRepository,
    ITransactionManager transactionManager,
    IEventBus eventBus,
    ILogger<SubjectRepository> logger,
    TimeProvider timeProvider) : ISubjectRepository
{
    private static readonly Expression<Func<Subject, string?>> EffectiveTitle =
        s => s.MetadataContainer == null
            ? null
            : (
                from c in s.MetadataContainer.Contributions
                where c.Title != null
                orderby c.Ordinal
                select c.Title).FirstOrDefault();

    private const string UserProviderType = "user";
    private const string SystemProviderType = "system";
    private const string CreateSavepoint = "content_subject_create";

    private const int ParameterBatchSize = 1_000;

    private readonly TimeProvider _time = timeProvider;

    private static IQueryable<Subject> IncludeReadModel(IQueryable<Subject> query)
    {
        var withIncludes = query
            .Include(s => s.MetadataContainer).ThenInclude(c => c!.Contributions).ThenInclude(c => c.AlternateTexts)
            .Include(s => s.MetadataContainer).ThenInclude(c => c!.Contributions).ThenInclude(c => c.Awards)
            .Include(s => s.MetadataContainer).ThenInclude(c => c!.Contributions).ThenInclude(c => c.ContentRatings)
            .Include(s => s.MetadataContainer).ThenInclude(c => c!.Contributions).ThenInclude(c => c.CustomMetadata)
            .Include(s => s.MetadataContainer).ThenInclude(c => c!.Contributions).ThenInclude(c => c.ExternalIds)
            .Include(s => s.MetadataContainer).ThenInclude(c => c!.Contributions).ThenInclude(c => c.Genres)
            .Include(s => s.MetadataContainer).ThenInclude(c => c!.Contributions).ThenInclude(c => c.Images)
            .Include(s => s.MetadataContainer).ThenInclude(c => c!.Contributions).ThenInclude(c => c.Previews)
            .Include(s => s.MetadataContainer).ThenInclude(c => c!.Contributions).ThenInclude(c => c.References)
            .Include(s => s.MetadataContainer).ThenInclude(c => c!.Contributions).ThenInclude(c => c.Relationships);
        return withIncludes.AsSplitQuery();
    }

    private static IQueryable<Subject> IncludeListModel(IQueryable<Subject> query)
    {
        return query
            .Include(s => s.MetadataContainer).ThenInclude(c => c!.Contributions).ThenInclude(c => c.Images)
            .AsSplitQuery();
    }

    private static IQueryable<Image> ApplyImageUrlCriteria(
        IQueryable<Image> query,
        CriteriaSet<PropertyCriterion<string>> criteria)
    {
        if (criteria.And is null || !criteria.And.Any())
        {
            return query;
        }

        foreach (var criterion in criteria.And)
        {
            var uri = ParseAbsoluteUri(criterion.Value);
            var uris = (criterion.Values ?? [])
                .Where(v => v != null)
                .Select(v => ParseAbsoluteUri(v)!)
                .ToList();
            query = criterion.Operator switch
            {
                CriteriaValueOperator.Equals => query.Where(i => uri != null && i.Url == uri),
                CriteriaValueOperator.NotEquals => query.Where(i => uri == null || i.Url != uri),
                CriteriaValueOperator.IsNull => query.Where(i => i.Url == null),
                CriteriaValueOperator.IsNotNull => query.Where(i => i.Url != null),
                CriteriaValueOperator.In => query.Where(i => uris.Contains(i.Url)),
                CriteriaValueOperator.NotIn => query.Where(i => !uris.Contains(i.Url)),
                var op => throw new ArgumentException(
                    $"Operator {op} is not supported for image URL.",
                    nameof(criteria))
            };
        }

        return query;
    }

    private static Uri? ParseAbsoluteUri(string? value)
    {
        if (value == null)
        {
            return null;
        }

        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            throw new ArgumentException($"Image URL \"{value}\" must be an absolute URL.", nameof(value));
        }

        return uri;
    }

    private IQueryable<Guid> ContentRelatedContainerIds()
    {
        return
            from relationship in context.Relationships.AsNoTracking()
            join contribution in context.MetadataContributions.AsNoTracking()
                on relationship.MetadataContributionId equals contribution.Id
            join item in context.ContentHierarchyItems.AsNoTracking()
                on contribution.MetadataContainerId equals item.MetadataContainerId
            where relationship.RelatedMetadataContainerId != null
            select relationship.RelatedMetadataContainerId!.Value;
    }

    private IQueryable<Guid> TaggedContainerIds()
    {
        return
            from tag in context.Tags.AsNoTracking()
            from container in tag.MetadataContainers
            select container.Id;
    }

    public async Task<IEnumerable<Subject>> ListAllAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await IncludeListModel(context.Subjects.AsNoTracking())
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The subject repository failed to list subjects.");
            throw;
        }
    }

    public async Task<Subject?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            return await IncludeReadModel(context.Subjects.AsNoTracking())
                .FirstOrDefaultAsync(s => s.Id == id, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The subject repository failed to read subject {SubjectId}.", id);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Guid>> ListIdsByExternalIdentityAsync(
        string kind,
        IReadOnlyCollection<ExternalId> externalIds,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var identities = externalIds
                .Where(id => !string.IsNullOrWhiteSpace(id.Source) && !string.IsNullOrWhiteSpace(id.Value))
                .Select(id => (id.Source!.NormalizeExternalIdentifierName(),
                    id.Value.NormalizeExternalIdentifierValue()))
                .ToHashSet();
            if (identities.Count == 0)
            {
                return [];
            }

            var rows =
                from subject in context.Subjects.AsNoTracking()
                join contribution in context.MetadataContributions.AsNoTracking()
                    on subject.MetadataContainerId equals contribution.MetadataContainerId
                join externalId in context.ExternalIds.AsNoTracking()
                    on contribution.Id equals externalId.MetadataContributionId
                where subject.Kind == kind && externalId.Source != null
                select new { subject.Id, externalId.Source, externalId.Value };
            var matches = new HashSet<Guid>();

            await foreach (var row in rows.TagWith("Subject identity candidates").AsAsyncEnumerable()
                .WithCancellation(cancellationToken).ConfigureAwait(false))
            {
                if (!string.IsNullOrWhiteSpace(row.Source) && !string.IsNullOrWhiteSpace(row.Value) &&
                    identities.Contains((
                        row.Source.NormalizeExternalIdentifierName(),
                        row.Value.NormalizeExternalIdentifierValue())))
                {
                    matches.Add(row.Id);
                }
            }

            return [.. matches];
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "The subject repository failed to match external identities for kind {SubjectKind}.",
                kind);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Subject>> ListIdentityDetailsAsync(
        IReadOnlyCollection<Guid> ids,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var subjects = new List<Subject>();
            foreach (var batch in ids.Distinct().Chunk(ParameterBatchSize))
            {
                subjects.AddRange(await context.Subjects.AsNoTracking()
                    .Where(subject => batch.Contains(subject.Id))
                    .Include(subject => subject.MetadataContainer)
                    .ThenInclude(container => container!.Contributions)
                    .ThenInclude(contribution => contribution.ExternalIds)
                    .AsSplitQuery()
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false));
            }

            return subjects;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The subject repository failed to read subject identity details.");
            throw;
        }
    }

    private async Task<IQueryable<Subject>> BuildFilteredAsync(SubjectQuery query, CancellationToken cancellationToken)
    {
        var filtered = context.Subjects.AsNoTracking();

        if (query.Criteria is not null)
        {
            filtered = ApplyCriteria(filtered, query.Criteria);
        }

        if (query.Ids is { } queryIds)
        {
            var ids = queryIds as Guid[] ?? [.. queryIds];
            filtered = filtered.Where(s => ids.Contains(s.Id));
        }

        if (!string.IsNullOrEmpty(query.Kind))
        {
            filtered = filtered.Where(s => s.Kind == query.Kind);
        }

        if (query.Kinds is { Count: > 0 })
        {
            filtered = filtered.Where(s => query.Kinds.Contains(s.Kind));
        }

        if (query.LastRefreshedBefore is { } lastRefreshedBefore)
        {
            filtered = filtered.Where(s =>
                s.DateLastRefreshed == null || s.DateLastRefreshed < lastRefreshedBefore);
        }

        if (query.HasExternalIds is { } hasExternalIds)
        {
            filtered = filtered.Where(s => context.MetadataContributions.AsNoTracking()
                .Where(c => c.MetadataContainerId == s.MetadataContainerId)
                .SelectMany(c => context.ExternalIds.Where(e => e.MetadataContributionId == c.Id))
                .Any() == hasExternalIds);
        }

        if (query.ExternalId is not null)
        {
            var containerIds = MatchingContainerIdsByExternalId(query.ExternalId);
            filtered = filtered.Where(s => containerIds.Contains(s.MetadataContainerId));
        }

        if (!string.IsNullOrEmpty(query.Tag))
        {
            var taggedContainerIds =
                from t in context.Tags.AsNoTracking()
                where t.Name == query.Tag
                from c in t.MetadataContainers
                select c.Id;
            filtered = filtered.Where(s => taggedContainerIds.Contains(s.MetadataContainerId));
        }

        if (query.RelatedContentHierarchyItemId is { } relatedContentHierarchyItemId)
        {
            var pivotContainerId = await (
                from i in context.ContentHierarchyItems.AsNoTracking()
                where i.Id == relatedContentHierarchyItemId
                select (Guid?)i.MetadataContainerId)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);
            if (pivotContainerId is null)
            {
                var noContainerIds = Array.Empty<Guid>();
                return filtered.Where(s => noContainerIds.Contains(s.MetadataContainerId));
            }

            var candidateContainerIds = RelatedCandidateContainerIds(pivotContainerId.Value);
            filtered = filtered.Where(s => candidateContainerIds.Contains(s.MetadataContainerId));
        }

        if (query.HasContentRelationships is { } hasContentRelationships)
        {
            var containerIds = ContentRelatedContainerIds();
            filtered = filtered.Where(s =>
                containerIds.Contains(s.MetadataContainerId) == hasContentRelationships);
        }

        if (query.HasUserContributions is { } hasUserContributions)
        {
            filtered = filtered.Where(s => context.MetadataContributions.AsNoTracking().Any(contribution =>
                contribution.MetadataContainerId == s.MetadataContainerId &&
                contribution.ProviderType == UserProviderType) == hasUserContributions);
        }

        if (query.HasTags is { } hasTags)
        {
            var containerIds = TaggedContainerIds();
            filtered = filtered.Where(s => containerIds.Contains(s.MetadataContainerId) == hasTags);
        }

        if (query.IsUserCreated is { } isUserCreated)
        {
            filtered = filtered.Where(s => context.Users.Any(user =>
                user.Id == s.CreatedByUserId && user.ProviderType != SystemProviderType) == isUserCreated);
        }

        return filtered;
    }

    public async Task<PagedList<Subject>> QueryAsync(
        SubjectQuery query,
        PagingParameter paging,
        SortParameter? sort,
        CancellationToken cancellationToken)
    {
        try
        {
            var filtered = await BuildFilteredAsync(query, cancellationToken).ConfigureAwait(false);

            var totalItems = await filtered.CountAsync(cancellationToken).ConfigureAwait(false);

            var sorted = ApplySort(filtered, sort);
            sorted = sorted.Skip((paging.PageNumber - 1) * paging.PageSize).Take(paging.PageSize);

            var pageIds = await sorted
                .Select(s => s.Id)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            var page = await IncludeListModel(context.Subjects.AsNoTracking())
                .Where(s => pageIds.Contains(s.Id))
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            var byId = page.ToDictionary(s => s.Id);
            var ordered = (from id in pageIds where byId.ContainsKey(id) select byId[id]).ToList();

            return new()
            {
                Items = ordered,
                PageNumber = paging.PageNumber,
                PageSize = paging.PageSize,
                TotalItems = totalItems
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The subject repository failed to query subjects.");
            throw;
        }
    }

    private static IQueryable<Subject> ApplySort(IQueryable<Subject> query, SortParameter? sort)
    {
        var descending = sort?.Direction == SortDirection.Descending;
        var ordered = sort?.PropertyName switch
        {
            var p when string.Equals(p, nameof(Subject.Kind), StringComparison.OrdinalIgnoreCase) =>
                descending ? query.OrderByDescending(s => s.Kind) : query.OrderBy(s => s.Kind),
            var p when string.Equals(p, nameof(Subject.DateLastRefreshed), StringComparison.OrdinalIgnoreCase) =>
                descending
                    ? query.OrderByDescending(s => s.DateLastRefreshed)
                    : query.OrderBy(s => s.DateLastRefreshed),
            var p when string.Equals(p, nameof(Subject.CreatedAt), StringComparison.OrdinalIgnoreCase) =>
                descending ? query.OrderByDescending(s => s.CreatedAt) : query.OrderBy(s => s.CreatedAt),
            var p when string.Equals(p, nameof(Subject.ModifiedAt), StringComparison.OrdinalIgnoreCase) =>
                descending ? query.OrderByDescending(s => s.ModifiedAt) : query.OrderBy(s => s.ModifiedAt),
            _ => descending ? query.OrderByDescending(EffectiveTitle) : query.OrderBy(EffectiveTitle)
        };

        return ordered.ThenBy(s => s.Id);
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Globalization",
        "CA1310",
        Justification = "EF Core cannot translate the StringComparison overloads, and both operands are lower-cased.")]
    private static IQueryable<Subject> ApplyTitleCriteria(
        IQueryable<Subject> query,
        CriteriaSet<PropertyCriterion<string>> criteria,
        bool isOriginalTitle)
    {
        if (criteria.And is null || !criteria.And.Any())
        {
            return query;
        }

        foreach (var criterion in criteria.And)
        {
            var value = criterion.Value?.ToLower();
            query = criterion.Operator switch
            {
                CriteriaValueOperator.ContainsString => query.Where(s =>
                    value != null &&
                    s.MetadataContainer != null &&
                    s.MetadataContainer.Contributions.Any(c =>
                        (isOriginalTitle ? c.OriginalTitle : c.Title) != null &&
                        (isOriginalTitle ? c.OriginalTitle! : c.Title!).ToLower().Contains(value))),
                CriteriaValueOperator.NotContainsString => query.Where(s =>
                    value == null ||
                    s.MetadataContainer == null ||
                    !s.MetadataContainer.Contributions.Any(c =>
                        (isOriginalTitle ? c.OriginalTitle : c.Title) != null &&
                        (isOriginalTitle ? c.OriginalTitle! : c.Title!).ToLower().Contains(value))),
                CriteriaValueOperator.NotStartsWithString => query.Where(s =>
                    value == null ||
                    s.MetadataContainer == null ||
                    !s.MetadataContainer.Contributions.Any(c =>
                        (isOriginalTitle ? c.OriginalTitle : c.Title) != null &&
                        (isOriginalTitle ? c.OriginalTitle! : c.Title!).ToLower().StartsWith(value))),
                CriteriaValueOperator.StartsWithString => query.Where(s =>
                    value != null &&
                    s.MetadataContainer != null &&
                    s.MetadataContainer.Contributions.Any(c =>
                        (isOriginalTitle ? c.OriginalTitle : c.Title) != null &&
                        (isOriginalTitle ? c.OriginalTitle! : c.Title!).ToLower().StartsWith(value))),
                CriteriaValueOperator.NotEndsWithString => query.Where(s =>
                    value == null ||
                    s.MetadataContainer == null ||
                    !s.MetadataContainer.Contributions.Any(c =>
                        (isOriginalTitle ? c.OriginalTitle : c.Title) != null &&
                        (isOriginalTitle ? c.OriginalTitle! : c.Title!).ToLower().EndsWith(value))),
                CriteriaValueOperator.EndsWithString => query.Where(s =>
                    value != null &&
                    s.MetadataContainer != null &&
                    s.MetadataContainer.Contributions.Any(c =>
                        (isOriginalTitle ? c.OriginalTitle : c.Title) != null &&
                        (isOriginalTitle ? c.OriginalTitle! : c.Title!).ToLower()
                            .EndsWith(value))),
                CriteriaValueOperator.Equals => query.Where(s =>
                    value != null &&
                    s.MetadataContainer != null &&
                    s.MetadataContainer.Contributions.Any(c =>
                        (isOriginalTitle ? c.OriginalTitle : c.Title) != null &&
                        (isOriginalTitle ? c.OriginalTitle! : c.Title!).ToLower() == value)),
                CriteriaValueOperator.NotEquals => query.Where(s =>
                    value == null ||
                    s.MetadataContainer == null ||
                    !s.MetadataContainer.Contributions.Any(c =>
                        (isOriginalTitle ? c.OriginalTitle : c.Title) != null &&
                        (isOriginalTitle ? c.OriginalTitle! : c.Title!).ToLower() == value)),
                CriteriaValueOperator.IsNull => query.Where(s =>
                    s.MetadataContainer == null ||
                    !s.MetadataContainer.Contributions.Any(c =>
                        (isOriginalTitle ? c.OriginalTitle : c.Title) != null)),
                CriteriaValueOperator.IsNotNull => query.Where(s =>
                    s.MetadataContainer != null &&
                    s.MetadataContainer.Contributions.Any(c =>
                        (isOriginalTitle ? c.OriginalTitle : c.Title) != null)),
                var op => throw new NotSupportedException($"Operator {op} is not supported for metadata Title")
            };
        }

        return query;
    }

    private static IQueryable<Subject> ApplyEffectiveStringCriteria(
        IQueryable<Subject> query,
        CriteriaSet<PropertyCriterion<string>> criteria,
        Expression<Func<Subject, string?>> effective)
    {
        if (criteria.And is null || !criteria.And.Any())
        {
            return query;
        }

        foreach (var criterion in criteria.And)
        {
            var value = criterion.Value?.ToLower();
            var predicate = criterion.Operator switch
            {
                CriteriaValueOperator.Equals => Compose(effective, v => v != null && v.ToLower() == value),
                CriteriaValueOperator.NotEquals => Compose(effective, v => v == null || v.ToLower() != value),
                CriteriaValueOperator.IsNull => Compose(effective, v => v == null),
                CriteriaValueOperator.IsNotNull => Compose(effective, v => v != null),
                CriteriaValueOperator.ContainsString =>
                    Compose(effective, v => value != null && v != null && v.ToLower().Contains(value)),
                CriteriaValueOperator.NotContainsString =>
                    Compose(effective, v => value == null || v == null || !v.ToLower().Contains(value)),
                CriteriaValueOperator.StartsWithString =>
                    Compose(effective, v => value != null && v != null && v.ToLower().StartsWith(value)),
                CriteriaValueOperator.NotStartsWithString =>
                    Compose(effective, v => value == null || v == null || !v.ToLower().StartsWith(value)),
                CriteriaValueOperator.EndsWithString =>
                    Compose(effective, v => value != null && v != null && v.ToLower().EndsWith(value)),
                CriteriaValueOperator.NotEndsWithString =>
                    Compose(effective, v => value == null || v == null || !v.ToLower().EndsWith(value)),
                var op => throw new NotSupportedException($"Operator {op} is not supported for metadata string")
            };
            query = query.Where(predicate);
        }

        return query;
    }

    private static IQueryable<Subject> ApplyCustomMetadataCriteria(
        IQueryable<Subject> query,
        CriteriaSet<DictionaryCriteria<PropertyCriterion<string>>> criteria)
    {
        foreach (var dictionary in criteria.And ?? [])
        {
            foreach (var (key, criterion) in dictionary.Items ?? [])
            {
                var normalizedKey = key.ToLowerInvariant();
                Expression<Func<Subject, string?>> effective = subject => subject.MetadataContainer == null
                    ? null
                    : (
                        from contribution in subject.MetadataContainer.Contributions
                        from value in contribution.CustomMetadata
                        where value.Key.ToLower() == normalizedKey
                        orderby contribution.Ordinal
                        select value.Value).FirstOrDefault();
                query = ApplyEffectiveStringCriteria(query, new() { And = [criterion] }, effective);
            }
        }

        return query;
    }

    private static IQueryable<Subject> ApplyEffectiveDateCriteria(
        IQueryable<Subject> query,
        CriteriaSet<PropertyCriterion<TemporalValue>> criteria,
        Expression<Func<Subject, DateTime?>> effective)
    {
        if (criteria.And is null || !criteria.And.Any())
        {
            return query;
        }

        foreach (var criterion in criteria.And)
        {
            var predicate = effective.Body.BuildTemporalPredicateBody(criterion);
            query = query.Where(Expression.Lambda<Func<Subject, bool>>(predicate, effective.Parameters));
        }

        return query;
    }

    private static Expression<Func<Subject, bool>> Compose<TValue>(
        Expression<Func<Subject, TValue>> selector,
        Expression<Func<TValue, bool>> test)
    {
        var replaced = ReplacingExpressionVisitor.Replace(test.Parameters[0], selector.Body, test.Body);
        return Expression.Lambda<Func<Subject, bool>>(replaced, selector.Parameters);
    }

    private static IQueryable<Subject> ApplyCollectionCriteria<TCriteria>(
        IQueryable<Subject> query,
        CollectionCriteria<TCriteria> criteria,
        Func<TCriteria, IQueryable<Guid>> matchingContainerIds)
    {
        foreach (var group in criteria.Some)
        {
            if (group.Count == 0)
            {
                continue;
            }

            var groupContainerIds = group
                .Select(matchingContainerIds)
                .Aggregate((IQueryable<Guid>?)null, (acc, ids) => acc is null ? ids : acc.Concat(ids));

            if (groupContainerIds is not null)
            {
                query = query.Where(s => groupContainerIds.Contains(s.MetadataContainerId));
            }
        }

        foreach (var none in criteria.None)
        {
            var noneContainerIds = matchingContainerIds(none);
            query = query.Where(s => !noneContainerIds.Contains(s.MetadataContainerId));
        }

        return query;
    }

    private IQueryable<Subject> ApplyCriteria(IQueryable<Subject> query, SubjectFilterCriteria criteria)
    {
        query = query.ApplyCriteriaSet(criteria.Kind, nameof(Subject.Kind));
        query = query.ApplyCriteriaSet(criteria.DateLastRefreshed, nameof(Subject.DateLastRefreshed));

        if (criteria.Metadata.And is not null)
        {
            foreach (var metadata in criteria.Metadata.And)
            {
                query = ApplyMetadataCriteria(query, metadata);
            }
        }

        return query;
    }

    private IQueryable<Subject> ApplyMetadataCriteria(IQueryable<Subject> query, MetadataCriteria metadata)
    {
        query = ApplyTitleCriteria(query, metadata.Title, isOriginalTitle: false);
        query = ApplyTitleCriteria(query, metadata.OriginalTitle, isOriginalTitle: true);
        query = ApplyEffectiveStringCriteria(
            query,
            metadata.Overview,
            s => s.MetadataContainer == null
                ? null
                : (
                    from c in s.MetadataContainer.Contributions
                    where c.Overview != null
                    orderby c.Ordinal
                    select c.Overview).FirstOrDefault());
        query = ApplyEffectiveStringCriteria(
            query,
            metadata.Language,
            s => s.MetadataContainer == null
                ? null
                : (
                    from c in s.MetadataContainer.Contributions
                    where c.Language != null
                    orderby c.Ordinal
                    select c.Language).FirstOrDefault());
        query = ApplyEffectiveDateCriteria(
            query,
            metadata.BirthDate,
            s => s.MetadataContainer == null
                ? null
                : (
                    from c in s.MetadataContainer.Contributions
                    where c.BirthDate != null
                    orderby c.Ordinal
                    select c.BirthDate).FirstOrDefault());
        query = ApplyEffectiveDateCriteria(
            query,
            metadata.DeathDate,
            s => s.MetadataContainer == null
                ? null
                : (
                    from c in s.MetadataContainer.Contributions
                    where c.DeathDate != null
                    orderby c.Ordinal
                    select c.DeathDate).FirstOrDefault());
        query = ApplyEffectiveDateCriteria(
            query,
            metadata.Established,
            s => s.MetadataContainer == null
                ? null
                : (
                    from c in s.MetadataContainer.Contributions
                    where c.Established != null
                    orderby c.Ordinal
                    select c.Established).FirstOrDefault());
        query = ApplyEffectiveDateCriteria(
            query,
            metadata.Dissolved,
            s => s.MetadataContainer == null
                ? null
                : (
                    from c in s.MetadataContainer.Contributions
                    where c.Dissolved != null
                    orderby c.Ordinal
                    select c.Dissolved).FirstOrDefault());
        query = ApplyCustomMetadataCriteria(query, metadata.CustomMetadata);
        query = ApplyTagsCriteria(query, metadata.Tags);
        query = ApplyCollectionCriteria(query, metadata.Awards, MatchingContainerIdsForAward);
        query = ApplyCollectionCriteria(query, metadata.ExternalIds, MatchingContainerIdsByExternalId);
        query = ApplyCollectionCriteria(query, metadata.Images, MatchingContainerIdsForImage);

        return query;
    }

    private IQueryable<Subject> ApplyTagsCriteria(IQueryable<Subject> query, CollectionCriteria<TagCriteria> criteria)
    {
        return ApplyCollectionCriteria(query, criteria, MatchingContainerIdsForTag);
    }

    private IQueryable<Guid> MatchingContainerIdsForTag(TagCriteria criterion)
    {
        return context.Tags.AsNoTracking()
            .ApplyCriteriaSet(criterion.Name, nameof(Tag.Name))
            .SelectMany(t => t.MetadataContainers)
            .Select(mc => mc.Id)
            .Distinct();
    }

    private IQueryable<Guid> MatchingContainerIdsForAward(AwardCriteria criterion)
    {
        var awards = context.Awards.AsNoTracking()
            .ApplyCriteriaSet(criterion.Body, nameof(Award.Body))
            .ApplyCriteriaSet(criterion.Name, nameof(Award.Name))
            .ApplyCriteriaSet(criterion.Category, nameof(Award.Category))
            .ApplyCriteriaSet(criterion.Status, nameof(Award.Status))
            .ApplyCriteriaSet(criterion.Year, nameof(Award.Year))
            .ApplyCriteriaSet(criterion.Language, nameof(Award.Language))
            .ApplyCriteriaSet(criterion.Country, nameof(Award.Country));
        return ContainerIdsThroughContributions(awards, a => a.MetadataContributionId);
    }

    private IQueryable<ExternalId> DeduplicatedExternalIds()
    {
        return
            from c in context.MetadataContributions.AsNoTracking()
            from e in c.ExternalIds
            where c.Id == (
                from c2 in context.MetadataContributions
                from e2 in c2.ExternalIds
                where c2.MetadataContainerId == c.MetadataContainerId
                where e.Source != null
                    ? e2.Source != null && e2.Source.ToLower() == e.Source.ToLower()
                    : e2.Source == null && e2.Value.ToLower() == e.Value.ToLower()
                orderby c2.Ordinal, c2.Id
                select c2.Id).FirstOrDefault()
            orderby c.Ordinal, e.Ordinal
            select e;
    }

    private IQueryable<Guid> MatchingContainerIdsByExternalId(ExternalIdCriteria criterion)
    {
        var externalIds = DeduplicatedExternalIds()
            .ApplyCriteriaSet(criterion.Value, nameof(ExternalId.Value))
            .ApplyCriteriaSet(criterion.Source, nameof(ExternalId.Source));

        return (
            from c in context.MetadataContributions.AsNoTracking()
            join e in externalIds on c.Id equals e.MetadataContributionId
            select c.MetadataContainerId).Distinct();
    }

    private IQueryable<Guid> MatchingContainerIdsForImage(ImageCriteria criterion)
    {
        var images = context.Images.AsNoTracking()
            .ApplyCriteriaSet(criterion.Type, nameof(Image.Type))
            .ApplyCriteriaSet(criterion.MimeType, nameof(Image.MimeType))
            .ApplyCriteriaSet(criterion.Source, nameof(Image.Source))
            .ApplyCriteriaSet(criterion.Language, nameof(Image.Language))
            .ApplyCriteriaSet(criterion.Width, nameof(Image.Width))
            .ApplyCriteriaSet(criterion.Height, nameof(Image.Height));
        images = ApplyImageUrlCriteria(images, criterion.Url);
        return ContainerIdsThroughContributions(images, i => i.MetadataContributionId);
    }

    private IQueryable<Guid> ContainerIdsThroughContributions<TItem>(
        IQueryable<TItem> items,
        Expression<Func<TItem, Guid>> contributionId)
        where TItem : class
    {
        return items
            .Join(
                context.MetadataContributions.AsNoTracking(),
                contributionId,
                con => con.Id,
                (_, con) => con.MetadataContainerId)
            .Distinct();
    }

    private IQueryable<Guid> RelatedCandidateContainerIds(Guid pivotContainerId)
    {
        return (
            from c in context.MetadataContributions.AsNoTracking()
            where c.MetadataContainerId == pivotContainerId
            join r in context.Relationships.AsNoTracking() on c.Id equals r.MetadataContributionId
            where r.RelatedMetadataContainerId != null
            select r.RelatedMetadataContainerId!.Value).Distinct();
    }

    public async Task<Subject> CreateAsync(Subject model, Guid? createdByUserId, CancellationToken cancellationToken)
    {
        try
        {
            return await CreateCoreAsync(model, createdByUserId, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The subject repository failed to create a subject of kind {Kind}.", model.Kind);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<bool> TryCreateAsync(
        Subject model,
        Guid? createdByUserId,
        CancellationToken cancellationToken = default)
    {
        var transaction = context.Database.CurrentTransaction;
        var savepointTaken = transaction is { SupportsSavepoints: true };
        try
        {
            if (savepointTaken)
            {
                await transaction!.CreateSavepointAsync(CreateSavepoint, cancellationToken).ConfigureAwait(false);
            }

            await CreateCoreAsync(model, createdByUserId, cancellationToken).ConfigureAwait(false);
            if (savepointTaken)
            {
                await transaction!.ReleaseSavepointAsync(CreateSavepoint, cancellationToken).ConfigureAwait(false);
            }

            return true;
        }
        catch (DbUpdateException ex)
        {
            context.Entry(model).State = EntityState.Detached;
            if (model.MetadataContainer is not null)
            {
                context.Entry(model.MetadataContainer).State = EntityState.Detached;
                foreach (var contribution in model.MetadataContainer.Contributions)
                {
                    context.Entry(contribution).State = EntityState.Detached;
                }
            }

            if (savepointTaken)
            {
                await transaction!.RollbackToSavepointAsync(CreateSavepoint, cancellationToken)
                    .ConfigureAwait(false);
                await transaction.ReleaseSavepointAsync(CreateSavepoint, cancellationToken).ConfigureAwait(false);
            }

            if (!await context.Subjects.AsNoTracking()
                .AnyAsync(subject => subject.Id == model.Id, cancellationToken)
                .ConfigureAwait(false))
            {
                logger.LogError(ex, "The subject repository failed to create subject {SubjectId}.", model.Id);
                throw;
            }

            logger.LogWarning(ex, "The subject {SubjectId} of kind {Kind} already exists", model.Id, model.Kind);
            return false;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The subject repository failed to create subject {SubjectId}.", model.Id);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<IReadOnlyList<Subject>>> FindDuplicatesAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            var rows = await (
                from subject in context.Subjects.AsNoTracking()
                join contribution in context.MetadataContributions.AsNoTracking()
                    on subject.MetadataContainerId equals contribution.MetadataContainerId
                join externalId in context.ExternalIds.AsNoTracking()
                    on contribution.Id equals externalId.MetadataContributionId
                where externalId.Source != null && externalId.Value != ""
                select new { SubjectId = subject.Id, subject.Kind, externalId.Source, externalId.Value })
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            var identities = rows
                .Select(row => (
                    row.SubjectId,
                    Identity: (
                        row.Kind,
                        Source: row.Source!.NormalizeExternalIdentifierName(),
                        Value: row.Value.NormalizeExternalIdentifierValue())))
                .Where(row => row.Identity.Source.Length > 0 && row.Identity.Value.Length > 0)
                .Distinct()
                .ToList();
            var identitiesBySubject = identities.ToLookup(row => row.SubjectId, row => row.Identity);
            var subjectsByIdentity = identities.ToLookup(row => row.Identity, row => row.SubjectId);
            var visited = new HashSet<Guid>();
            var sets = new List<List<Guid>>();
            foreach (var start in identitiesBySubject)
            {
                if (!visited.Add(start.Key))
                {
                    continue;
                }

                List<Guid> set = [start.Key];
                for (var index = 0; index < set.Count; index++)
                {
                    foreach (var identity in identitiesBySubject[set[index]])
                    {
                        foreach (var subjectId in subjectsByIdentity[identity])
                        {
                            if (visited.Add(subjectId))
                            {
                                set.Add(subjectId);
                            }
                        }
                    }
                }

                var clash = set
                    .SelectMany(subjectId => identitiesBySubject[subjectId]
                        .Select(identity => (SubjectId: subjectId, identity.Source, identity.Value)))
                    .GroupBy(row => row.Source)
                    .Any(source => source.Select(row => row.Value).Distinct().Count() >
                        source.GroupBy(row => row.SubjectId).Max(subject => subject.Count()));
                if (set.Count > 1 && !clash)
                {
                    sets.Add(set);
                }
            }

            var subjects = await ListIdentityDetailsAsync([.. sets.SelectMany(ids => ids)], cancellationToken)
                .ConfigureAwait(false);
            var subjectsById = subjects.ToDictionary(subject => subject.Id);

            return [.. sets
                .Select(ids => ids.Where(subjectsById.ContainsKey).Select(id => subjectsById[id]).ToList())
                .Where(set => set.Count > 1)];
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The subject repository failed to find duplicate subjects.");
            throw;
        }
    }

    /// <inheritdoc />
    public async Task MergeAsync(
        Guid survivorId,
        IReadOnlyCollection<Guid> duplicateIds,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await transactionManager.ExecuteWithTransactionAsync(
                async ct =>
                {
                    var survivorContainerId = await context.Subjects.AsNoTracking()
                        .Where(subject => subject.Id == survivorId)
                        .Select(subject => subject.MetadataContainerId)
                        .SingleAsync(ct)
                        .ConfigureAwait(false);
                    var containerIds = await context.Subjects.AsNoTracking()
                        .Where(subject => duplicateIds.Contains(subject.Id))
                        .Select(subject => subject.MetadataContainerId)
                        .ToListAsync(ct)
                        .ConfigureAwait(false);

                    await context.Relationships
                        .Where(relationship => relationship.RelatedMetadataContainerId != null &&
                            containerIds.Contains(relationship.RelatedMetadataContainerId.Value))
                        .ExecuteUpdateAsync(
                            setters => setters.SetProperty(
                                relationship => relationship.RelatedMetadataContainerId,
                                survivorContainerId),
                            ct)
                        .ConfigureAwait(false);

                    await context.MetadataContributions
                        .Where(contribution => containerIds.Contains(contribution.MetadataContainerId) &&
                            contribution.ProviderType == UserProviderType)
                        .ExecuteUpdateAsync(
                            setters => setters.SetProperty(
                                contribution => contribution.MetadataContainerId,
                                survivorContainerId),
                            ct)
                        .ConfigureAwait(false);

                    var survivorContributions = await context.MetadataContributions.AsNoTracking()
                        .Where(contribution => contribution.MetadataContainerId == survivorContainerId)
                        .Select(contribution => new { contribution.Id, contribution.ProviderType })
                        .ToListAsync(ct)
                        .ConfigureAwait(false);
                    var duplicateContributions = await context.MetadataContributions.AsNoTracking()
                        .Where(contribution => containerIds.Contains(contribution.MetadataContainerId))
                        .Select(contribution => new { contribution.Id, contribution.ProviderType })
                        .ToListAsync(ct)
                        .ConfigureAwait(false);
                    foreach (var contribution in duplicateContributions)
                    {
                        var target = survivorContributions.FirstOrDefault(survivorContribution =>
                            survivorContribution.ProviderType == contribution.ProviderType);
                        if (target is null)
                        {
                            await context.MetadataContributions
                                .Where(moved => moved.Id == contribution.Id)
                                .ExecuteUpdateAsync(
                                    setters => setters.SetProperty(
                                        moved => moved.MetadataContainerId,
                                        survivorContainerId),
                                    ct)
                                .ConfigureAwait(false);
                            survivorContributions.Add(contribution);
                            continue;
                        }

                        await context.Relationships
                            .Where(relationship => relationship.MetadataContributionId == contribution.Id)
                            .ExecuteUpdateAsync(
                                setters => setters.SetProperty(
                                    relationship => relationship.MetadataContributionId,
                                    target.Id),
                                ct)
                            .ConfigureAwait(false);
                    }

                    var survivorContributionIds =
                        survivorContributions.Select(contribution => contribution.Id).ToList();
                    var repeatedIds = (await context.Relationships.AsNoTracking()
                        .Where(relationship => relationship.RelatedMetadataContainerId != null &&
                            (relationship.RelatedMetadataContainerId == survivorContainerId ||
                                survivorContributionIds.Contains(relationship.MetadataContributionId)))
                        .Select(relationship => new
                        {
                            relationship.Id,
                            relationship.MetadataContributionId,
                            relationship.RelatedMetadataContainerId,
                            relationship.Kind,
                            relationship.Qualifier
                        })
                        .ToListAsync(ct)
                        .ConfigureAwait(false))
                        .GroupBy(relationship => (
                            relationship.MetadataContributionId,
                            relationship.RelatedMetadataContainerId,
                            relationship.Kind,
                            relationship.Qualifier))
                        .SelectMany(group => group.Skip(1))
                        .Select(relationship => relationship.Id)
                        .ToList();
                    await context.Relationships
                        .Where(relationship => repeatedIds.Contains(relationship.Id))
                        .ExecuteDeleteAsync(ct)
                        .ConfigureAwait(false);

                    var tags = await context.Tags
                        .Where(tag => tag.MetadataContainers.Any(container => containerIds.Contains(container.Id)) &&
                            !tag.MetadataContainers.Any(container => container.Id == survivorContainerId))
                        .ToListAsync(ct)
                        .ConfigureAwait(false);
                    if (tags.Count > 0)
                    {
                        var survivorContainer = await context.MetadataContainers
                            .SingleAsync(container => container.Id == survivorContainerId, ct)
                            .ConfigureAwait(false);
                        foreach (var tag in tags)
                        {
                            tag.MetadataContainers.Add(survivorContainer);
                        }

                        await context.SaveChangesAsync(ct).ConfigureAwait(false);
                    }
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "The subject repository failed to merge duplicate subjects into subject {SubjectId}.",
                survivorId);
            throw;
        }
    }

    private async Task<Subject> CreateCoreAsync(
        Subject model,
        Guid? createdByUserId,
        CancellationToken cancellationToken)
    {
        var created = await transactionManager.ExecuteWithTransactionAsync(
            async _ =>
            {
                var now = _time.GetUtcNow().UtcDateTime;
                model.CreatedAt = now;
                model.CreatedByUserId = createdByUserId;

                if (model.MetadataContainer is not null)
                {
                    model.MetadataContainer.CreatedAt = now;
                    model.MetadataContainerId = model.MetadataContainer.Id;
                    foreach (var contribution in model.MetadataContainer.Contributions)
                    {
                        contribution.MetadataContainerId = model.MetadataContainer.Id;
                        contribution.CreatedAt = now;
                        contribution.CreatedByUserId ??= createdByUserId;
                    }
                }

                context.Subjects.Add(model);
                await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

                await auditRecordRepository.CreateAsync(
                    new()
                    {
                        Category = AuditCategories.System,
                        Operation = "Create",
                        EntityId = model.Id.ToString(),
                        EntityType = nameof(Subject),
                        Detail = $"Created subject of kind {model.Kind}",
                        Timestamp = now,
                        UserId = createdByUserId
                    },
                    cancellationToken).ConfigureAwait(false);

                return model;
            },
            cancellationToken).ConfigureAwait(false);

        await transactionManager.OnCommittedAsync(
            token => eventBus.PublishAsync(SubjectEventBuilder.BuildSubjectCreatedEvent(created.Id), token),
            cancellationToken).ConfigureAwait(false);

        return created;
    }

    public async Task<bool> UpdateAsync(Subject model, Guid? modifiedByUserId, CancellationToken cancellationToken)
    {
        try
        {
            return await transactionManager.ExecuteWithTransactionAsync(
                async _ =>
                {
                    var now = _time.GetUtcNow().UtcDateTime;
                    var current = await context.Subjects.AsNoTracking()
                        .FirstOrDefaultAsync(entity => entity.Id == model.Id, cancellationToken)
                        .ConfigureAwait(false);
                    if (current is null)
                    {
                        return false;
                    }

                    var changedFields = new List<string>();
                    if (!Equals(current.Kind, model.Kind))
                    {
                        changedFields.Add("kind");
                    }

                    if (!Equals(current.DateLastRefreshed, model.DateLastRefreshed))
                    {
                        changedFields.Add("dateLastRefreshed");
                    }

                    var updated = await context.Subjects
                        .Where(s => s.Id == model.Id)
                        .ExecuteUpdateAsync(
                            s => s
                                .SetProperty(e => e.Kind, model.Kind)
                                .SetProperty(e => e.DateLastRefreshed, model.DateLastRefreshed)
                                .SetProperty(e => e.ModifiedAt, now)
                                .SetProperty(e => e.ModifiedByUserId, modifiedByUserId),
                            cancellationToken)
                        .ConfigureAwait(false);
                    if (updated > 0)
                    {
                        await auditRecordRepository.CreateAsync(
                            new()
                            {
                                Category = AuditCategories.System,
                                Operation = "Update",
                                EntityId = model.Id.ToString(),
                                EntityType = nameof(Subject),
                                Detail = $"Updated subject of kind {model.Kind}",
                                Timestamp = now,
                                UserId = modifiedByUserId
                            },
                            cancellationToken).ConfigureAwait(false);
                    }

                    if (updated > 0 && changedFields.Count > 0)
                    {
                        await transactionManager.OnCommittedAsync(
                            token => eventBus.PublishAsync(
                                SubjectEventBuilder.BuildSubjectUpdatedEvent(model.Id, changedFields),
                                token),
                            cancellationToken).ConfigureAwait(false);
                    }

                    return updated > 0;
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The subject repository failed to update subject {SubjectId}.", model.Id);
            throw;
        }
    }

    public async Task<bool> DeleteAsync(Guid id, Guid? deletedByUserId, CancellationToken cancellationToken)
    {
        try
        {
            var removed = await transactionManager.ExecuteWithTransactionAsync(
                async _ =>
                {
                    var now = _time.GetUtcNow().UtcDateTime;
                    var model = await (
                        from s in context.Subjects.AsNoTracking()
                        where s.Id == id
                        select new { s.Kind })
                        .FirstOrDefaultAsync(cancellationToken)
                        .ConfigureAwait(false);

                    if (model is null)
                    {
                        return false;
                    }

                    var deleted = await context.Subjects
                        .Where(s => s.Id == id)
                        .ExecuteDeleteAsync(cancellationToken)
                        .ConfigureAwait(false);
                    if (deleted > 0)
                    {
                        await auditRecordRepository.CreateAsync(
                            new()
                            {
                                Category = AuditCategories.System,
                                Operation = "Delete",
                                EntityId = id.ToString(),
                                EntityType = nameof(Subject),
                                Detail = $"Deleted subject of kind {model.Kind}",
                                Timestamp = now,
                                UserId = deletedByUserId
                            },
                            cancellationToken).ConfigureAwait(false);
                    }

                    return deleted > 0;
                },
                cancellationToken).ConfigureAwait(false);

            if (removed)
            {
                await transactionManager.OnCommittedAsync(
                    token => eventBus.PublishAsync(SubjectEventBuilder.BuildSubjectDeletedEvent(id), token),
                    cancellationToken).ConfigureAwait(false);
            }

            return removed;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The subject repository failed to delete subject {SubjectId}.", id);
            throw;
        }
    }

    public async Task<int> DeleteWhereAsync(SubjectQuery query, CancellationToken cancellationToken = default)
    {
        try
        {
            var filtered = await BuildFilteredAsync(query, cancellationToken).ConfigureAwait(false);
            var targets = await filtered
                .Select(subject => new { subject.Id, subject.Kind })
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            if (targets.Count == 0)
            {
                return 0;
            }

            var deleted = await transactionManager.ExecuteWithTransactionAsync(
                async ct =>
                {
                    var now = _time.GetUtcNow().UtcDateTime;
                    var removed = 0;
                    foreach (var batch in targets.Chunk(ParameterBatchSize))
                    {
                        var ids = batch.Select(target => target.Id).ToArray();
                        removed += await context.Subjects
                            .Where(subject => ids.Contains(subject.Id))
                            .ExecuteDeleteAsync(ct)
                            .ConfigureAwait(false);

                        foreach (var target in batch)
                        {
                            await auditRecordRepository.CreateAsync(
                                new()
                                {
                                    Category = AuditCategories.System,
                                    Operation = "Delete",
                                    EntityId = target.Id.ToString(),
                                    EntityType = nameof(Subject),
                                    Detail = $"Deleted subject of kind {target.Kind}",
                                    Timestamp = now
                                },
                                ct).ConfigureAwait(false);
                        }
                    }

                    return removed;
                },
                cancellationToken).ConfigureAwait(false);

            foreach (var target in targets)
            {
                await transactionManager.OnCommittedAsync(
                    token => eventBus.PublishAsync(SubjectEventBuilder.BuildSubjectDeletedEvent(target.Id), token),
                    cancellationToken).ConfigureAwait(false);
            }

            return deleted;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The subject repository failed to delete subjects by query.");
            throw;
        }
    }

    public async Task<bool> PatchAsync(
        SubjectRowPatch patch,
        Guid? modifiedByUserId,
        CancellationToken cancellationToken)
    {
        try
        {
            var changedFields = new List<string>();
            var patched = await transactionManager.ExecuteWithTransactionAsync(
                async _ =>
                {
                    var now = _time.GetUtcNow().UtcDateTime;
                    var model = await context.Subjects
                        .Include(s => s.MetadataContainer)
                        .FirstOrDefaultAsync(s => s.Id == patch.Id, cancellationToken)
                        .ConfigureAwait(false);

                    if (model is null || model.MetadataContainer is null)
                    {
                        return false;
                    }

                    if (patch.DateLastRefreshed is not null &&
                        model.DateLastRefreshed != patch.DateLastRefreshed.Value)
                    {
                        model.DateLastRefreshed = patch.DateLastRefreshed.Value;
                        changedFields.Add("dateLastRefreshed");
                    }

                    var tagsAdded = new List<string>();
                    var tagsRemoved = new List<string>();

                    if (patch.Tags is not null)
                    {
                        if (patch.Tags.Add is not null)
                        {
                            foreach (var tagName in patch.Tags.Add)
                            {
                                var tag = await context.Tags
                                    .Include(t => t.MetadataContainers)
                                    .FirstOrDefaultAsync(t => t.Name == tagName, cancellationToken)
                                    .ConfigureAwait(false);

                                if (tag is null)
                                {
                                    tag = new() { Name = tagName };
                                    context.Tags.Add(tag);
                                }

                                if (!tag.MetadataContainers.Contains(model.MetadataContainer))
                                {
                                    tag.MetadataContainers.Add(model.MetadataContainer);
                                    tagsAdded.Add(tagName);
                                }
                            }
                        }

                        if (patch.Tags.Remove is not null)
                        {
                            foreach (var tagName in patch.Tags.Remove)
                            {
                                var tag = await context.Tags
                                    .Include(t => t.MetadataContainers)
                                    .FirstOrDefaultAsync(t => t.Name == tagName, cancellationToken)
                                    .ConfigureAwait(false);

                                if (tag is not null && tag.MetadataContainers.Remove(model.MetadataContainer))
                                {
                                    tagsRemoved.Add(tagName);
                                }
                            }
                        }
                    }

                    if (!tagsAdded.ToHashSet(StringComparer.Ordinal).SetEquals(tagsRemoved))
                    {
                        changedFields.Add("tags");
                    }

                    model.ModifiedAt = now;
                    model.ModifiedByUserId = modifiedByUserId;

                    var updated = await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                    if (updated > 0)
                    {
                        await auditRecordRepository.CreateAsync(
                            new()
                            {
                                Category = AuditCategories.System,
                                Operation = "Patch",
                                EntityId = patch.Id.ToString(),
                                EntityType = nameof(Subject),
                                Detail = $"Updated subject ({model.Kind}).",
                                Timestamp = now,
                                UserId = modifiedByUserId
                            },
                            cancellationToken).ConfigureAwait(false);

                        foreach (var tagName in tagsAdded)
                        {
                            await auditRecordRepository.CreateAsync(
                                new()
                                {
                                    Category = AuditCategories.System,
                                    Operation = "AddTag",
                                    EntityId = patch.Id.ToString(),
                                    EntityType = nameof(Subject),
                                    Detail = $"Added tag {tagName} to subject of kind {model.Kind}",
                                    Timestamp = now,
                                    UserId = modifiedByUserId
                                },
                                cancellationToken).ConfigureAwait(false);
                        }

                        foreach (var tagName in tagsRemoved)
                        {
                            await auditRecordRepository.CreateAsync(
                                new()
                                {
                                    Category = AuditCategories.System,
                                    Operation = "RemoveTag",
                                    EntityId = patch.Id.ToString(),
                                    EntityType = nameof(Subject),
                                    Detail = $"Removed tag {tagName} from subject of kind {model.Kind}",
                                    Timestamp = now,
                                    UserId = modifiedByUserId
                                },
                                cancellationToken).ConfigureAwait(false);
                        }
                    }

                    return updated > 0;
                },
                cancellationToken).ConfigureAwait(false);

            if (patched)
            {
                await transactionManager.OnCommittedAsync(
                    token => eventBus.PublishAsync(
                        SubjectEventBuilder.BuildSubjectUpdatedEvent(patch.Id, changedFields),
                        token),
                    cancellationToken)
                    .ConfigureAwait(false);
            }

            return patched;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The subject repository failed to patch subject {SubjectId}.", patch.Id);
            throw;
        }
    }
}
