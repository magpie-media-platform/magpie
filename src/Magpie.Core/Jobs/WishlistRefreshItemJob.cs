using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Repositories;
using Magpie.Core.Events;
using Magpie.Models;
using Magpie.Models.Arguments;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Jobs;
using Magpie.Models.MetadataSearch;
using Magpie.Models.Patch;
using Magpie.Sdk.Jobs;
using Magpie.Sdk.Services;

using Microsoft.Extensions.Logging;

namespace Magpie.Core.Jobs;

internal sealed class WishlistRefreshItemJob(
    IWishlistRepository wishlistRepository,
    IContentFilterRepository contentFilterRepository,
    IMetadataSearchService metadataSearchService,
    IContentService contentService,
    IWishlistDiscoveredRepository wishlistDiscoveredRepository,
    IUserRepository userRepository,
    IEventBus eventBus,
    IProgressReporter progressReporter,
    TimeProvider timeProvider,
    ILogger<WishlistRefreshItemJob> logger) : IJob
{
    public static readonly JobSpec Spec = new()
    {
        JobType = MagpieJobs.Types.WishlistRefreshItem,
        DisplayName = "Wishlist refresh item",
        Description = "Searches all metadata sources for one wishlist and adds new matches to the library.",
        Category = "Content",
        Scope = JobScope.Administrative,
        Enabled = true,
        OnDemand = true,
        FailurePolicy = JobFailurePolicy.Requeue,
        SupportedTriggerModes = [JobTriggerMode.Manual, JobTriggerMode.Schedule, JobTriggerMode.Event],
        ConcurrencyScope = ConcurrencyScope.PerTypeAndTarget,
        LockPartitionFields = [MagpieJobs.Parameters.WishlistId],
        Parameters =
        [
            new(
                MagpieJobs.Parameters.WishlistId,
                "The wishlist to refresh. Its filter is dispatched to every advanced metadata " +
                "searcher and newly-matched items are added to the library.",
                DataType.String,
                true,
                null)
            {
                ValidateValue = value => string.IsNullOrWhiteSpace(value) || Guid.TryParse(value, out _)
                    ? null : "must be a valid GUID."
            }
        ],
    };

    public Task ExecuteAsync(JobContext context, CancellationToken cancellationToken = default)
    {
        if (!context.Parameters.TryGetValue(MagpieJobs.Parameters.WishlistId, out var rawWishlistId) ||
            string.IsNullOrWhiteSpace(rawWishlistId))
        {
            throw new ArgumentException("A wishlist ID is required.");
        }

        if (!Guid.TryParse(rawWishlistId, out var wishlistId))
        {
            throw new ArgumentException("The wishlist ID has an invalid format.");
        }

        return RefreshWishlistAsync(wishlistId, context.ManualTriggerUserId, cancellationToken);
    }

    private static string FormatCandidateIdentity(MetadataSearchCandidate candidate)
    {
        return string.IsNullOrWhiteSpace(candidate.Title)
            ? $"{candidate.ExternalIdSource}={candidate.ExternalId}"
            : $"{candidate.ExternalIdSource}={candidate.ExternalId} ({candidate.Title})";
    }

    private async Task RefreshWishlistAsync(
        Guid wishlistId,
        Guid? triggerUserId,
        CancellationToken cancellationToken = default)
    {
        progressReporter.Report(0, "The refresh will load the wishlist.");

        var wishlist = await wishlistRepository.GetByIdAsync(wishlistId, cancellationToken).ConfigureAwait(false);
        if (wishlist is null)
        {
            logger.LogInformation("Wishlist {WishlistId} no longer exists, so the job skipped the refresh", wishlistId);
            progressReporter.Report(1, "The refresh stopped because the wishlist does not exist.");
            return;
        }

        if (!wishlist.Enabled)
        {
            logger.LogInformation(
                "Wishlist {WishlistId} ({WishlistName}) is disabled, so the job skipped the one-off refresh",
                wishlist.Id,
                wishlist.Name);
            progressReporter.Report(1, "The refresh stopped because the wishlist is disabled.");
            return;
        }

        await RefreshOneAsync(wishlist, triggerUserId, cancellationToken).ConfigureAwait(false);

        progressReporter.Report(1, "The wishlist refresh completed.");
    }

    private async Task RefreshOneAsync(Wishlist wishlist, Guid? triggerUserId, CancellationToken cancellationToken)
    {
        var contentFilter = await contentFilterRepository
            .GetByIdAsync(wishlist.ContentFilterId, cancellationToken)
            .ConfigureAwait(false);
        if (contentFilter is null)
        {
            logger.LogWarning(
                "Wishlist {WishlistId} references missing content filter {ContentFilterId}, so " +
                "the job skipped the refresh",
                wishlist.Id,
                wishlist.ContentFilterId);
            return;
        }

        var effectiveUserId = wishlist.OwnerUserId
            ?? triggerUserId
            ?? (await userRepository.QueryAsync(
                new UserQuery { Username = "system" },
                new() { PageNumber = 1, PageSize = 1 },
                null,
                cancellationToken).ConfigureAwait(false)).Items.SingleOrDefault()?.Id;

        var discoveredKeys = await wishlistDiscoveredRepository.ListAsync(wishlist.Id, cancellationToken)
                .ConfigureAwait(false);

        var seenKeys = new HashSet<(string ExternalIdSource, string ExternalId)>(
            discoveredKeys.Select(key => (
                key.ExternalIdSource.NormalizeExternalIdentifierName(),
                key.ExternalId.NormalizeExternalIdentifierValue())));

        var hydrated = 0;
        var matchesSeen = 0;
        var skippedSeen = 0;
        var page = 1;
        string? continuation = null;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            progressReporter.Report(null, $"The refresh will search page {page} of wishlist {wishlist.Name}.");

            var searchResult = await metadataSearchService
                .SearchCandidatesAsync(contentFilter.Criteria, string.Empty, page, continuation, cancellationToken)
                .ConfigureAwait(false);

            foreach (var warning in searchResult.Warnings)
            {
                logger.LogWarning("Wishlist {WishlistId} candidate search warning: {Warning}", wishlist.Id, warning);
            }

            var matches = searchResult.Matches;
            matchesSeen += matches.Count;
            foreach (var match in matches)
            {
                var candidate = match.Candidate;
                cancellationToken.ThrowIfCancellationRequested();

                var key = (
                    candidate.ExternalIdSource.NormalizeExternalIdentifierName(),
                    candidate.ExternalId.NormalizeExternalIdentifierValue());

                if (!seenKeys.Add(key))
                {
                    skippedSeen++;
                    continue;
                }

                try
                {
                    progressReporter.Report(
                        null,
                        $"The wishlist refresh will hydrate content for {FormatCandidateIdentity(candidate)}.");

                    var candidateHydrated = await HydrateAsync(
                        wishlist,
                        candidate,
                        effectiveUserId,
                        cancellationToken).ConfigureAwait(false);

                    if (candidateHydrated)
                    {
                        hydrated++;
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    logger.LogWarning(
                        ex,
                        "The job failed to hydrate {Source}={Value} from provider {Provider} " +
                        "for wishlist {WishlistId}",
                        candidate.ExternalIdSource,
                        candidate.ExternalId,
                        candidate.ProviderType,
                        wishlist.Id);
                    continue;
                }

                await wishlistDiscoveredRepository
                    .RecordDiscoveredAsync(
                        wishlist.Id,
                        [
                            new WishlistDiscovered
                            {
                                ExternalIdSource = candidate.ExternalIdSource,
                                ExternalId = candidate.ExternalId,
                                Title = candidate.Title
                            }
                        ],
                        CancellationToken.None)
                    .ConfigureAwait(false);
            }

            if (searchResult.Warnings.Count > 0)
            {
                throw new InvalidOperationException(
                    "The wishlist search was incomplete. Its completed matches were retained.");
            }

            if (!searchResult.PageInfo.HasMore)
            {
                break;
            }

            continuation = searchResult.NextContinuation ??
                throw new InvalidOperationException(
                    "The metadata search did not return a continuation for the next page.");
            page++;
        }

        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            await wishlistRepository.PatchAsync(
                new()
                {
                    Id = wishlist.Id,
                    LastSyncedAt = new ScalarPatch<DateTime?> { Value = timeProvider.GetUtcNow().UtcDateTime }
                },
                null,
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "The job failed to update LastSyncedAt for wishlist {WishlistId} ({WishlistName})",
                wishlist.Id,
                wishlist.Name);
        }

        logger.LogInformation(
            "The wishlist refresh for {WishlistId} hydrated {Count} new item(s) from {Candidates} " +
            "provider-search candidate(s) across {Pages} page(s), skipping {SkippedSeen} already-seen candidate(s)",
            wishlist.Id,
            hydrated,
            matchesSeen,
            page,
            skippedSeen);
    }

    private async Task<bool> HydrateAsync(
        Wishlist wishlist,
        MetadataSearchCandidate candidate,
        Guid? effectiveUserId,
        CancellationToken cancellationToken)
    {
        if (await ExistsInLibraryAsync(
            candidate.ExternalIdSource,
            candidate.ExternalId,
            candidate.ContentType,
            cancellationToken).ConfigureAwait(false))
        {
            return false;
        }

        var hydration = await contentService.HydrateFromExternalIdAsync(
            candidate.ProviderType,
            candidate.ExternalIdSource,
            candidate.ExternalId,
            candidate.ContentType,
            candidate.Level,
            wishlist.RootFolderId,
            wishlist.Monitoring,
            wishlist.QualityTierListId,
            wishlist.Id,
            [.. wishlist.Tags.Select(t => t.Name)],
            effectiveUserId,
            cancellationToken).ConfigureAwait(false);

        if (hydration is null)
        {
            logger.LogDebug(
                "Wishlist {WishlistId} match {Key}={Value} resolved to not-found, so the job created no content",
                wishlist.Id,
                candidate.ExternalIdSource,
                candidate.ExternalId);
            return false;
        }

        await eventBus
            .PublishAsync(
                WishlistEventBuilder.BuildWishlistItemMatchedEvent(wishlist.Id, hydration.Id),
                cancellationToken)
            .ConfigureAwait(false);

        return true;
    }

    private async Task<bool> ExistsInLibraryAsync(
        string externalIdSource,
        string externalIdValue,
        string contentType,
        CancellationToken cancellationToken)
    {
        var matches = await contentService.QueryAsync(
            new ContentQuery
            {
                Criteria = new()
                {
                    ContentHierarchyItem = new()
                    {
                        And =
                        [
                            new ContentHierarchyItemCriteria
                            {
                                ContentType = new()
                                {
                                    And = [new() { Value = contentType }]
                                },
                                Metadata = new()
                                {
                                    And =
                                    [
                                        new MetadataCriteria
                                        {
                                            ExternalIds = new()
                                            {
                                                Some =
                                                [
                                                    [
                                                        new ExternalIdCriteria
                                                        {
                                                            Source = new()
                                                            {
                                                                And = [new() { Value = externalIdSource }]
                                                            },
                                                            Value = new()
                                                            {
                                                                And = [new() { Value = externalIdValue }]
                                                            }
                                                        }
                                                    ]
                                                ]
                                            }
                                        }
                                    ]
                                }
                            }
                        ]
                    }
                }
            },
            new() { PageNumber = 1, PageSize = 1 },
            sort: null,
            cancellationToken)
            .ConfigureAwait(false);
        return matches.TotalItems > 0;
    }
}
