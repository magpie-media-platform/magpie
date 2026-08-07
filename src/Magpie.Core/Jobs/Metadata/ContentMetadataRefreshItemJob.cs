using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions;
using Magpie.Abstractions.Repositories;
using Magpie.Core.Events;
using Magpie.Models;
using Magpie.Models.Arguments;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Filtering;
using Magpie.Models.Jobs;
using Magpie.Sdk.Content;
using Magpie.Sdk.Events;
using Magpie.Sdk.Jobs;
using Magpie.Sdk.Services;

using Microsoft.Extensions.Logging;

namespace Magpie.Core.Jobs.Metadata;

internal sealed class ContentMetadataRefreshItemJob(
    IEnumerable<IContentHierarchyMetadataProvider> metadataProviders,
    IContentHierarchyItemRepository contentHierarchyItemRepository,
    IMetadataContainerRepository metadataContainerRepository,
    ISubjectRepository subjectRepository,
    IEventBus eventBus,
    IContentTypeRegistry contentTypeRegistry,
    ITransactionManager transactionManager,
    IJobManager jobManager,
    ISubjectKindRegistry subjectKindRegistry,
    TimeProvider timeProvider,
    ILogger<ContentMetadataRefreshItemJob> logger,
    IProgressReporter? progressReporter = null) : IJob
{
    public static readonly JobSpec Spec = new()
    {
        JobType = "ContentMetadataRefreshItem",
        DisplayName = "Content metadata refresh item",
        Description = "Refreshes metadata for one content item.",
        Category = "Content",
        Scope = JobScope.Content,
        Enabled = true,
        OnDemand = true,
        FailurePolicy = JobFailurePolicy.Retry,
        Timeout = TimeSpan.FromMinutes(5),
        SupportedTriggerModes = [JobTriggerMode.Manual, JobTriggerMode.Schedule, JobTriggerMode.Event],
        RequesterTriggerModes = [JobTriggerMode.Schedule, JobTriggerMode.Event],
        ConcurrencyScope = ConcurrencyScope.PerTarget,
        LockPartitionFields = [MagpieJobs.Parameters.ContentHierarchyItemId],
        LockPartitionEventContextKeys = new Dictionary<string, string>
        {
            [MagpieJobs.Parameters.ContentHierarchyItemId] = MagpieEvents.Keys.ContentHierarchyItemId
        },
        Parameters =
        [
            new(
                MagpieJobs.Parameters.ContentHierarchyItemId,
                "The content hierarchy item id to refresh.",
                DataType.String,
                true,
                null)
        ],
    };

    private const string NoMetadataReturnedStatus =
        "The provider returned no metadata, so the refresh will try again.";

    private static Guid ResolveContentHierarchyItemId(JobContext context)
    {
        if (context.Parameters.TryGetValue(
            MagpieJobs.Parameters.ContentHierarchyItemId,
            out var rawParameter) && !string.IsNullOrWhiteSpace(rawParameter))
        {
            if (!Guid.TryParse(rawParameter, out var parsed))
            {
                throw new ArgumentException("The content item ID has an invalid format.");
            }

            return parsed;
        }

        if (context.Event is { Context: var eventContext } &&
            eventContext.TryGetValue(MagpieEvents.Keys.ContentHierarchyItemId, out var rawFromEvent) &&
            !string.IsNullOrWhiteSpace(rawFromEvent))
        {
            if (!Guid.TryParse(rawFromEvent, out var parsed))
            {
                throw new ArgumentException("The job received an invalid content item ID.");
            }

            return parsed;
        }

        throw new ArgumentException("A content item ID is required.");
    }

    private static string? ResolveCanonicalTitle(IEnumerable<MetadataContribution> contributions)
    {
        return (
            from c in contributions
            where !string.IsNullOrWhiteSpace(c.Title)
            orderby c.Ordinal
            select c.Title!.Trim())
            .FirstOrDefault();
    }

    private static async Task<ProviderFetchResult> InvokeProviderAsync(
        IContentHierarchyMetadataProvider provider,
        ContentHierarchyItem item,
        IReadOnlyList<MetadataContribution> existingContributions,
        IReadOnlyList<string>? ancestorLanguagePreferences,
        IReadOnlyList<MetadataContribution> ancestorMetadata,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await provider.TryGetMetadataAsync(
                new MetadataFetchRequest
                {
                    Item = item,
                    ExistingMetadata = existingContributions,
                    AncestorLanguagePreferences = ancestorLanguagePreferences ?? [],
                    DefaultLanguagePreference = string.Empty,
                    AncestorMetadata = ancestorMetadata,
                },
                cancellationToken).ConfigureAwait(false);
            if (result?.Contribution is null)
            {
                logger.LogInformation("Provider {ProviderType} returned no contribution", provider.ProviderType);
            }

            return new ProviderFetchResult(provider, result, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new ProviderFetchResult(provider, null, ex);
        }
    }

    public async Task ExecuteAsync(JobContext context, CancellationToken cancellationToken = default)
    {
        var itemId = ResolveContentHierarchyItemId(context);

        await RefreshAsync(
            itemId,
            context.ManualTriggerUserId,
            context.CorrelationId,
            progressReporter,
            cancellationToken).ConfigureAwait(false);
    }

    internal async Task RefreshAsync(
        Guid contentHierarchyItemId,
        Guid? userId,
        Guid? correlationId,
        IProgressReporter? progress,
        CancellationToken cancellationToken = default)
    {
        progress?.Report(0, "The refresh will load the content item.");

        var item = await contentHierarchyItemRepository.GetByIdAsync(contentHierarchyItemId, cancellationToken)
            .ConfigureAwait(false);
        if (item is null)
        {
            logger.LogInformation("The refresh skipped missing item {ContentHierarchyItemId}", contentHierarchyItemId);
            progress?.Report(1, "The refresh stopped because the content item does not exist.");
            return;
        }

        var container = item.MetadataContainer ??
            await metadataContainerRepository.GetByIdAsync(item.MetadataContainerId, cancellationToken)
                .ConfigureAwait(false);
        if (container is null)
        {
            logger.LogInformation(
                "The refresh skipped item {ContentHierarchyItemId} because it has no metadata container",
                item.Id);
            progress?.Report(1, "The refresh stopped because the metadata container does not exist.");
            return;
        }

        if (contentTypeRegistry.GetDefinitionFor(item.ContentType) is null)
        {
            logger.LogWarning(
                "The refresh skipped item {ContentHierarchyItemId} because its content type is not registered",
                item.Id);
            progress?.Report(1, "The refresh stopped because the content type is not registered.");
            return;
        }

        var providers = metadataProviders
            .Where(p => p.SupportedContentTypes.Any(supported => supported == "*" || supported == item.ContentType))
            .ToArray();

        foreach (var group in providers
            .Where(p => !string.IsNullOrWhiteSpace(p.ProviderType))
            .GroupBy(p => p.ProviderType))
        {
            var members = group.ToList();
            if (members.Count <= 1)
            {
                continue;
            }

            var typeNames = string.Join(", ", members.Select(p => p.GetType().FullName));
            logger.LogWarning(
                "Two or more types use ProviderType {ProviderType}: {TypeNames}. The refresh will run all " +
                "of them and their contributions will overwrite each other",
                group.Key,
                typeNames);
        }

        var existingContributions = container.Contributions
            .OrderBy(c => c.Ordinal)
            .ThenBy(c => c.ProviderType, StringComparer.Ordinal)
            .ToList();
        var (ancestorLanguagePreferences, ancestorMetadata) =
            await WalkAncestorsAsync(item, cancellationToken).ConfigureAwait(false);

        progress?.Report(0, "The refresh will get metadata from the providers.");

        var providerTasks = new List<Task<ProviderFetchResult>>(providers.Length);
        foreach (var provider in providers)
        {
            cancellationToken.ThrowIfCancellationRequested();

            providerTasks.Add(InvokeProviderAsync(
                provider,
                item,
                existingContributions,
                ancestorLanguagePreferences,
                ancestorMetadata,
                logger,
                cancellationToken));
        }

        var fetchResults = await Task.WhenAll(providerTasks).ConfigureAwait(false);
        var fetchByProvider = fetchResults.ToDictionary(r => r.Provider);

        var replacements = new List<ContributionReplacement>();
        var completedProviders = 0;
        foreach (var provider in providers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(
                providers.Length == 0 ? 0.5 : Math.Min(0.95, completedProviders / (double)providers.Length),
                $"The refresh will merge metadata from provider {provider.ProviderType}.");

            var fetch = fetchByProvider[provider];
            if (fetch.Exception is not null)
            {
                logger.LogWarning(fetch.Exception, "Metadata provider {ProviderType} failed", provider.ProviderType);
                completedProviders++;
                continue;
            }

            var contribution = fetch.Result?.Contribution;
            if (contribution is not null)
            {
                var providerContributions = existingContributions
                    .Where(c => c.ProviderType == provider.ProviderType)
                    .ToArray();
                var ordinal = providerContributions.Length > 0
                    ? providerContributions.Min(c => c.Ordinal)
                    : (existingContributions.Count == 0 ? 0 : existingContributions.Max(c => c.Ordinal) + 1);

                contribution.ProviderType = string.IsNullOrWhiteSpace(contribution.ProviderType)
                    ? provider.ProviderType
                    : contribution.ProviderType;
                contribution.MetadataContainerId = container.Id;
                contribution.Ordinal = ordinal;
                contribution.DateLastRefreshed = timeProvider.GetUtcNow().UtcDateTime;

                var subjectRelationships = new List<SubjectRelationshipDefinition>();
                if (provider is IContentHierarchyMetadataRelationshipProvider relationshipProvider)
                {
                    try
                    {
                        var expanded = await relationshipProvider.GetRelationshipsAsync(
                            new MetadataRelationshipRequest
                            {
                                Item = item,
                                Contribution = contribution,
                                ExistingMetadata = existingContributions,
                                AncestorMetadata = ancestorMetadata,
                            },
                            cancellationToken).ConfigureAwait(false);
                        if (expanded is { Count: > 0 })
                        {
                            subjectRelationships.AddRange(expanded);
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(
                            ex,
                            "Provider {ProviderType} failed to expand the related subjects, so the " +
                            "refresh saved no related subjects from this provider",
                            provider.ProviderType);
                    }
                }

                replacements.Add(new(providerContributions, contribution, subjectRelationships));

                existingContributions.RemoveAll(c => providerContributions.Any(p => p.Id == c.Id));
                existingContributions.Add(contribution);
                existingContributions = [.. existingContributions
                    .OrderBy(c => c.Ordinal)
                    .ThenBy(c => c.ProviderType, StringComparer.Ordinal)];
            }

            completedProviders++;
        }

        cancellationToken.ThrowIfCancellationRequested();
        var resolvedTitle = ResolveCanonicalTitle(existingContributions);
        var resolvedYear = existingContributions
            .OrderBy(c => c.Ordinal)
            .Select(c => c.PublishDate?.Year)
            .FirstOrDefault(year => year is not null);

        var stillPlaceholder = string.IsNullOrWhiteSpace(resolvedTitle) &&
            providers.Length > 0 &&
            item.PendingMetadataRefresh;
        if (stillPlaceholder)
        {
            var anyProviderFailedTransiently = fetchResults.Any(r => r.Exception is not null);
            if (anyProviderFailedTransiently)
            {
                logger.LogInformation(
                    "The refresh got no metadata for item {ContentHierarchyItemId} and will retry",
                    item.Id);
                progress?.Report(1, NoMetadataReturnedStatus);
                throw new MetadataNotReturnedException(NoMetadataReturnedStatus);
            }

            logger.LogInformation(
                "The refresh got no metadata for item {ContentHierarchyItemId}",
                item.Id);
            progress?.Report(1, NoMetadataReturnedStatus);
            return;
        }

        cancellationToken.ThrowIfCancellationRequested();
        var addedRelationshipContainerIds = await ApplyRefreshWritesAsync(
            container.Id,
            replacements,
            item,
            resolvedTitle,
            resolvedYear,
            userId,
            correlationId,
            cancellationToken).ConfigureAwait(false);

        try
        {
            await eventBus
                .PublishAsync(
                    ContentEventBuilder.BuildMetadataRefreshedEvent(new()
                    {
                        HierarchyItemId = item.Id,
                        MetadataContainerId = container.Id,
                        ParentId = item.ParentId,
                        ContentType = item.ContentType,
                        Level = item.Level,
                        TopLevel = item.ParentId is null,
                        RootFolderId = item.RootFolderId,
                        QualityTierListId = item.QualityTierListId,
                        Monitoring = item.EffectiveMonitoring,
                        MetadataSourceContainerId = container.Id,
                        MetadataAddedContainerIds = addedRelationshipContainerIds
                    }),
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "The refresh finished but failed to publish metadata.refreshed for item " + "{ContentHierarchyItemId}",
                item.Id);
        }

        progress?.Report(1, "The metadata refresh completed.");
    }

    private async Task<IReadOnlyList<Guid>> ApplyRefreshWritesAsync(
        Guid containerId,
        IReadOnlyList<ContributionReplacement> replacements,
        ContentHierarchyItem item,
        string? resolvedTitle,
        int? resolvedYear,
        Guid? userId,
        Guid? correlationId,
        CancellationToken cancellationToken)
    {
        var addedRelationshipContainerIds = new List<Guid>();
        try
        {
            await transactionManager.ExecuteWithTransactionAsync(
                async ct =>
                {
                    addedRelationshipContainerIds.Clear();
                    foreach (var replacement in replacements)
                    {
                        if (replacement.ExistingProviderContributions.Count == 1)
                        {
                            var existingId = replacement.ExistingProviderContributions[0].Id;
                            var updated = await metadataContainerRepository.UpdateContributionAsync(
                                containerId,
                                existingId,
                                replacement.Contribution,
                                userId,
                                ct).ConfigureAwait(false);
                            replacement.Contribution.Id = updated.Id;
                        }
                        else
                        {
                            if (replacement.ExistingProviderContributions.Count > 1)
                            {
                                var existingIds = replacement.ExistingProviderContributions.Select(c => c.Id).ToArray();
                                var primaryImageUrls = await metadataContainerRepository
                                    .ListPrimaryImageUrlsAsync(existingIds, ct)
                                    .ConfigureAwait(false);
                                var primaryImage = (
                                    from i in replacement.Contribution.Images
                                    where primaryImageUrls.Contains(i.Url)
                                    orderby i.Ordinal, i.Url.AbsoluteUri
                                    select i).FirstOrDefault();

                                foreach (var image in replacement.Contribution.Images)
                                {
                                    image.IsPrimary = ReferenceEquals(image, primaryImage);
                                }

                                await metadataContainerRepository.RemoveContributionsAsync(
                                    replacement.ExistingProviderContributions,
                                    userId,
                                    correlationId,
                                    ct).ConfigureAwait(false);
                            }

                            await metadataContainerRepository.AddContributionAsync(
                                containerId,
                                replacement.Contribution,
                                userId,
                                ct).ConfigureAwait(false);
                        }

                        if (replacement.SubjectRelationships.Count > 0)
                        {
                            addedRelationshipContainerIds.AddRange(await UpsertSubjectRelationshipsAsync(
                                replacement.Contribution,
                                replacement.SubjectRelationships,
                                userId,
                                correlationId,
                                ct).ConfigureAwait(false));
                        }
                    }

                    await MarkRefreshedAsync(item, resolvedTitle, resolvedYear, userId, ct).ConfigureAwait(false);
                    await EnqueueExistingChildRefreshesAsync(
                        item,
                        userId,
                        correlationId,
                        ct).ConfigureAwait(false);
                },
                cancellationToken).ConfigureAwait(false);

            return addedRelationshipContainerIds;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The refresh failed to write the metadata for item {ContentHierarchyItemId}", item.Id);
            throw;
        }
    }

    private async Task<IReadOnlyList<Guid>> UpsertSubjectRelationshipsAsync(
        MetadataContribution contribution,
        IReadOnlyList<SubjectRelationshipDefinition> definitions,
        Guid? userId,
        Guid? correlationId,
        CancellationToken cancellationToken)
    {
        var seen = await metadataContainerRepository
            .ListRelationshipKeysAsync(contribution.Id, cancellationToken)
            .ConfigureAwait(false);

        var seenSet = seen.ToHashSet();
        var addedContainerIds = new List<Guid>();

        foreach (var definition in definitions)
        {
            if (definition is null ||
                string.IsNullOrWhiteSpace(definition.RelationshipKind) ||
                string.IsNullOrWhiteSpace(definition.SubjectKind) ||
                string.IsNullOrWhiteSpace(definition.SubjectName))
            {
                continue;
            }

            if (!subjectKindRegistry.Contains(definition.SubjectKind))
            {
                logger.LogWarning("The refresh skipped unknown subject kind {SubjectKind}", definition.SubjectKind);
                continue;
            }

            var subjectContainerId = await UpsertSubjectAsync(definition, userId, correlationId, cancellationToken)
                .ConfigureAwait(false);

            if (subjectContainerId is null)
            {
                continue;
            }

            var key = new MetadataRelationshipKey(
                subjectContainerId,
                definition.RelationshipKind,
                definition.RelationshipQualifier);

            if (!seenSet.Add(key))
            {
                continue;
            }

            await metadataContainerRepository.AddRelationshipAsync(
                new Relationship
                {
                    Id = Guid.NewGuid(),
                    MetadataContributionId = contribution.Id,
                    RelatedMetadataContainerId = subjectContainerId,
                    Kind = definition.RelationshipKind,
                    Qualifier = definition.RelationshipQualifier,
                    Ordinal = definition.RelationshipOrdinal
                },
                cancellationToken).ConfigureAwait(false);
            addedContainerIds.Add(subjectContainerId.Value);
        }

        return addedContainerIds;
    }

    private async Task<Guid?> UpsertSubjectAsync(
        SubjectRelationshipDefinition definition,
        Guid? userId,
        Guid? correlationId,
        CancellationToken cancellationToken)
    {
        var existingContainerId = await FindExistingSubjectContainerIdAsync(definition, cancellationToken)
            .ConfigureAwait(false);
        if (existingContainerId is not null)
        {
            return existingContainerId;
        }

        var subject = await subjectRepository.CreateAsync(
            new Subject
            {
                Id = Guid.NewGuid(),
                Kind = definition.SubjectKind,
                MetadataContainer = new() { Id = Guid.NewGuid() }
            },
            userId,
            cancellationToken).ConfigureAwait(false);

        if (definition.InitialSubjectContribution is not null)
        {
            var seedContribution = definition.InitialSubjectContribution;
            seedContribution.MetadataContainerId = subject.MetadataContainerId;
            seedContribution.DateLastRefreshed = timeProvider.GetUtcNow().UtcDateTime;
            await metadataContainerRepository
                .AddContributionAsync(subject.MetadataContainerId, seedContribution, userId, cancellationToken)
                .ConfigureAwait(false);
        }

        var hasExternalId = definition.SubjectExternalIds is { Count: > 0 } &&
            definition.SubjectExternalIds.Any(kvp =>
                !string.IsNullOrWhiteSpace(kvp.Key) && !string.IsNullOrWhiteSpace(kvp.Value));
        if (hasExternalId)
        {
            await jobManager.ExecuteAsync(
                new JobEnqueueRequest
                {
                    JobType = "SubjectMetadataRefreshItem",
                    Parameters = new Dictionary<string, string>()
                    {
                        ["subjectId"] = subject.Id.ToString("D"),
                    },
                    CorrelationId = correlationId,
                    UserId = userId,
                    Mode = JobExecutionMode.Queue,
                },
                cancellationToken).ConfigureAwait(false);

            logger.LogInformation(
                "The metadata refresh enqueued a refresh for subject {SubjectId} of kind {Kind}",
                subject.Id,
                subject.Kind);
        }

        return subject.MetadataContainerId;
    }

    private async Task<Guid?> FindExistingSubjectContainerIdAsync(
        SubjectRelationshipDefinition definition,
        CancellationToken cancellationToken)
    {
        if (definition.SubjectExternalIds is { Count: > 0 })
        {
            foreach (var (source, value) in definition.SubjectExternalIds)
            {
                if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(value))
                {
                    continue;
                }

                var matches = await subjectRepository.QueryAsync(
                    new SubjectQuery
                    {
                        Kind = definition.SubjectKind,
                        ExternalId = new()
                        {
                            Source = new() { And = [new() { Value = source }] },
                            Value = new() { And = [new() { Value = value }] }
                        }
                    },
                    new() { PageNumber = 1, PageSize = 1 },
                    null,
                    cancellationToken).ConfigureAwait(false);
                var match = matches.Items.FirstOrDefault()?.MetadataContainerId;
                if (match is not null)
                {
                    return match;
                }
            }
        }

        return null;
    }

    private async Task EnqueueExistingChildRefreshesAsync(
        ContentHierarchyItem item,
        Guid? userId,
        Guid? correlationId,
        CancellationToken cancellationToken)
    {
        const int pageSize = 500;
        var childIds = new List<Guid>();
        var pageNumber = 1;
        while (true)
        {
            var page = await contentHierarchyItemRepository.QueryAsync(
                new ContentHierarchyItemCriteria
                {
                    ParentId = new CriteriaSet<PropertyCriterion<Guid>>
                    {
                        And = [new() { Operator = CriteriaValueOperator.Equals, Value = item.Id }],
                    },
                    HasMetadataExternalIds = true,
                },
                new() { PageNumber = pageNumber, PageSize = pageSize },
                sort: null,
                cancellationToken).ConfigureAwait(false);
            var pageItems = page.Items.ToList();
            childIds.AddRange(pageItems.Select(child => child.Id));
            if (pageItems.Count < pageSize)
            {
                break;
            }

            pageNumber++;
        }

        foreach (var childId in childIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await jobManager.ExecuteAsync(
                new JobEnqueueRequest
                {
                    JobType = Spec.JobType,
                    Parameters = new Dictionary<string, string>
                    {
                        [MagpieJobs.Parameters.ContentHierarchyItemId] = childId.ToString("D")
                    },
                    CorrelationId = correlationId,
                    UserId = userId,
                    Mode = JobExecutionMode.Queue,
                },
                cancellationToken).ConfigureAwait(false);
        }

        if (childIds.Count > 0)
        {
            logger.LogInformation("The refresh cascaded to {ChildCount} existing children", childIds.Count);
        }
    }

    private async Task MarkRefreshedAsync(
        ContentHierarchyItem item,
        string? resolvedTitle,
        int? resolvedYear,
        Guid? userId,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;

        var shouldWriteName = !string.IsNullOrWhiteSpace(resolvedTitle) &&
            (string.IsNullOrWhiteSpace(item.Name) || item.PendingMetadataRefresh);
        static string NormalizeSegment(string source)
        {
            var characters = source.Select(character =>
                character is '\\' or '/' or ':' or '*' or '?' or '"' or '<' or '>' or '|' ||
                    char.IsControl(character)
                    ? ' '
                    : character).ToArray();
            return string.Join(' ', new string(characters).Split(' ', StringSplitOptions.RemoveEmptyEntries))
                .TrimEnd('.');
        }

        var origin = item.Settings.TryGetValue("content.path.origin", out var pathOrigin) ? pathOrigin : null;
        var isAbsolute = item.Path is not null && Path.IsPathFullyQualified(
            item.Path.Trim().Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar));
        var shouldReplacePlaceholderPath = shouldWriteName &&
            item.PendingMetadataRefresh &&
            string.Equals(origin, "Generated", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(
                item.Path,
                NormalizeSegment(item.Slug.Trim()),
                StringComparison.Ordinal) &&
            !isAbsolute &&
            item.ContentItem is not { MediaFiles.Count: > 0 };
        if (shouldWriteName)
        {
            item.Name = resolvedTitle!;
        }

        if (shouldReplacePlaceholderPath)
        {
            var level = contentTypeRegistry.GetDefinitionFor(item.ContentType)?.LevelDefinitions
                .FirstOrDefault(definition => definition.Slug == item.Level);
            if (level?.Subfolder is not null)
            {
                var segment = NormalizeSegment(item.Name.Trim());
                item.Path = resolvedYear is null
                    ? segment
                    : $"{segment} ({resolvedYear.Value.ToString(CultureInfo.InvariantCulture)})";
                item.Settings["content.path.origin"] = "Generated";
            }
        }

        item.PendingMetadataRefresh = false;
        item.DateLastRefreshed = now;

        await contentHierarchyItemRepository
            .UpdateAsync(item, userId, cancellationToken)
            .ConfigureAwait(false);
        await metadataContainerRepository
            .UpdateLastRefreshedAsync(item.MetadataContainerId, now, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<(IReadOnlyList<string>? LanguagePreferences, IReadOnlyList<MetadataContribution> Metadata)>
        WalkAncestorsAsync(ContentHierarchyItem item, CancellationToken cancellationToken)
    {
        IReadOnlyList<string>? languagePreferences = null;
        var metadata = new List<MetadataContribution>();

        var visited = new HashSet<Guid> { item.Id };

        var parentId = item.ParentId;
        while (parentId is not null && visited.Add(parentId.Value))
        {
            var parent = await contentHierarchyItemRepository.GetByIdAsync(parentId.Value, cancellationToken)
                .ConfigureAwait(false);
            if (parent is null)
            {
                break;
            }

            if (languagePreferences is null && parent.LanguagePreferences.Count > 0)
            {
                languagePreferences = parent.LanguagePreferences;
            }

            var container = parent.MetadataContainer ??
                await metadataContainerRepository.GetByIdAsync(parent.MetadataContainerId, cancellationToken)
                    .ConfigureAwait(false);
            if (container is not null)
            {
                metadata.AddRange(container.Contributions.OrderBy(c => c.Ordinal));
            }

            parentId = parent.ParentId;
        }

        return (languagePreferences, metadata);
    }

    private sealed record ContributionReplacement(
        IReadOnlyList<MetadataContribution> ExistingProviderContributions,
        MetadataContribution Contribution,
        IReadOnlyList<SubjectRelationshipDefinition> SubjectRelationships);

    private sealed record ProviderFetchResult(
        IContentHierarchyMetadataProvider Provider,
        ContentHierarchyMetadataResult? Result,
        Exception? Exception);
}
