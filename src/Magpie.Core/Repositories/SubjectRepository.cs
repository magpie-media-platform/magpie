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
    TimeProvider? timeProvider = null) : ISubjectRepository
{
    private static readonly Expression<Func<Subject, string?>> EffectiveTitle =
        s => s.MetadataContainer == null
            ? null
            : (
                from c in s.MetadataContainer.Contributions
                where c.Title != null
                orderby c.Ordinal
                select c.Title).FirstOrDefault();

    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;

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

    public async Task<PagedList<Subject>> QueryAsync(
        SubjectQuery query,
        PagingParameter paging,
        SortParameter? sort,
        CancellationToken cancellationToken)
    {
        try
        {
            var filtered = context.Subjects.AsNoTracking();

            if (query.Criteria is not null)
            {
                filtered = ApplyCriteria(filtered, query.Criteria);
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
                    return new()
                    {
                        Items = [],
                        PageNumber = paging.PageNumber,
                        PageSize = paging.PageSize,
                        TotalItems = 0
                    };
                }

                var scope = new MetadataRelationshipScope(
                    pivotContainerId.Value,
                    MetadataRelationshipDirection.PivotIsSource);
                var candidateContainerIds = RelatedCandidateContainerIds(scope);
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
                    contribution.ProviderType == "user") == hasUserContributions);
            }

            if (query.HasTags is { } hasTags)
            {
                var containerIds = TaggedContainerIds();
                filtered = filtered.Where(s => containerIds.Contains(s.MetadataContainerId) == hasTags);
            }

            if (query.IsUserCreated is { } isUserCreated)
            {
                filtered = filtered.Where(s => context.Users.Any(user =>
                    user.Id == s.CreatedByUserId && user.ProviderType != "system") == isUserCreated);
            }

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
                CriteriaValueOperator.StartsWithString => query.Where(s =>
                    value != null &&
                    s.MetadataContainer != null &&
                    s.MetadataContainer.Contributions.Any(c =>
                        (isOriginalTitle ? c.OriginalTitle : c.Title) != null &&
                        (isOriginalTitle ? c.OriginalTitle! : c.Title!).StartsWith(
                            value,
                            StringComparison.CurrentCultureIgnoreCase))),
                CriteriaValueOperator.EndsWithString => query.Where(s =>
                    value != null &&
                    s.MetadataContainer != null &&
                    s.MetadataContainer.Contributions.Any(c =>
                        (isOriginalTitle ? c.OriginalTitle : c.Title) != null &&
                        (isOriginalTitle ? c.OriginalTitle! : c.Title!).ToLower()
                            .EndsWith(value, StringComparison.Ordinal))),
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
                    : e2.Value.ToLower() == e.Value.ToLower()
                orderby c2.Ordinal
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

    private IQueryable<Guid> RelatedCandidateContainerIds(MetadataRelationshipScope scope)
    {
        return scope.Direction switch
        {
            MetadataRelationshipDirection.PivotIsSource => (
                from c in context.MetadataContributions.AsNoTracking()
                where c.MetadataContainerId == scope.PivotContainerId
                join r in context.Relationships.AsNoTracking() on c.Id equals r.MetadataContributionId
                where r.RelatedMetadataContainerId != null
                select r.RelatedMetadataContainerId!.Value).Distinct(),
            _ => throw new ArgumentOutOfRangeException(nameof(scope))
        };
    }

    public async Task<Subject> CreateAsync(Subject model, Guid? createdByUserId, CancellationToken cancellationToken)
    {
        try
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
        catch (Exception ex)
        {
            logger.LogError(ex, "The subject repository failed to create a subject of kind {Kind}.", model.Kind);
            throw;
        }
    }

    public async Task<bool> UpdateAsync(Subject model, Guid? modifiedByUserId, CancellationToken cancellationToken)
    {
        try
        {
            return await transactionManager.ExecuteWithTransactionAsync(
                async _ =>
                {
                    var now = _time.GetUtcNow().UtcDateTime;
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

    public async Task<bool> PatchAsync(TagsPatch patch, Guid? modifiedByUserId, CancellationToken cancellationToken)
    {
        try
        {
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
                await eventBus.PublishAsync(
                    SubjectEventBuilder.BuildSubjectUpdatedEvent(patch.Id, ["tags"]),
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
