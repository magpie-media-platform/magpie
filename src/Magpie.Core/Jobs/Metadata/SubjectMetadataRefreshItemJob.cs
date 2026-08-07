using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions;
using Magpie.Abstractions.Repositories;
using Magpie.Models;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Jobs;
using Magpie.Sdk.Content;
using Magpie.Sdk.Jobs;

using Microsoft.Extensions.Logging;

namespace Magpie.Core.Jobs.Metadata;

internal sealed class SubjectMetadataRefreshItemJob(
    IEnumerable<ISubjectMetadataProvider> metadataProviders,
    ISubjectRepository subjectRepository,
    IMetadataContainerRepository metadataContainerRepository,
    ITransactionManager transactionManager,
    TimeProvider timeProvider,
    ILogger<SubjectMetadataRefreshItemJob> logger,
    IProgressReporter? progressReporter = null) : IJob
{
    public static readonly JobSpec Spec = new()
    {
        JobType = "SubjectMetadataRefreshItem",
        DisplayName = "Subject metadata refresh item",
        Description = "Refreshes metadata for a subject.",
        Category = "Content",
        Scope = JobScope.Content,
        Enabled = true,
        OnDemand = true,
        FailurePolicy = JobFailurePolicy.Retry,
        Timeout = TimeSpan.FromMinutes(5),
        SupportedTriggerModes = [JobTriggerMode.Manual, JobTriggerMode.Schedule, JobTriggerMode.Event],
        RequesterTriggerModes = [JobTriggerMode.Schedule, JobTriggerMode.Event],
        ConcurrencyScope = ConcurrencyScope.PerTypeAndTarget,
        LockPartitionFields = [SubjectIdParameter],
        Parameters = [new(SubjectIdParameter, "The subject id to refresh.", DataType.String, true, null)],
    };

    private const string SubjectIdParameter = "subjectId";

    private const string UserProviderType = "user";

    private static async Task<ProviderFetchResult> InvokeProviderAsync(
        ISubjectMetadataProvider provider,
        Subject subject,
        IReadOnlyList<MetadataContribution> existingContributions,
        CancellationToken cancellationToken)
    {
        try
        {
            var contribution = await provider.TryGetMetadataAsync(
                subject,
                existingContributions,
                string.Empty,
                cancellationToken).ConfigureAwait(false);
            return new ProviderFetchResult(contribution, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new ProviderFetchResult(null, ex);
        }
    }

    private static MetadataContribution[] FindProviderContributions(
        IReadOnlyList<MetadataContribution> existingContributions,
        MetadataContribution contribution)
    {
        var providerTypeMatches = existingContributions
            .Where(c => c.ProviderType == contribution.ProviderType)
            .ToArray();
        if (providerTypeMatches.Length > 0)
        {
            return providerTypeMatches;
        }

        return [.. existingContributions
            .Where(c => c.ProviderType != UserProviderType)
            .Where(c => HasMatchingProviderIdentity(c, contribution))];
    }

    private static bool HasMatchingProviderIdentity(MetadataContribution existing, MetadataContribution incoming)
    {
        if (SameValue(existing.Source, incoming.Source))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(incoming.Source) &&
            existing.ExternalIds.Any(e => SameSource(e.Source, incoming.Source)))
        {
            return true;
        }

        foreach (var incomingExternalId in incoming.ExternalIds)
        {
            if (string.IsNullOrWhiteSpace(incomingExternalId.Source) ||
                string.IsNullOrWhiteSpace(incomingExternalId.Value))
            {
                continue;
            }

            if (SourceEmbedsValue(existing.Source, incomingExternalId.Source, incomingExternalId.Value))
            {
                return true;
            }

            if (existing.ExternalIds.Any(e =>
                SameSource(e.Source, incomingExternalId.Source) &&
                SameIdentifierValue(e.Value, incomingExternalId.Value)))
            {
                return true;
            }

            if (existing.ExternalIds.Any(e =>
                SourceEmbedsValue(e.Source, incomingExternalId.Source, incomingExternalId.Value)))
            {
                return true;
            }
        }

        return false;
    }

    private static bool SourceEmbedsValue(string? source, string prefix, string value)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return false;
        }

        return string.Equals(source.Trim(), string.Concat(prefix, ":", value), StringComparison.OrdinalIgnoreCase);
    }

    private static bool SameValue(string? left, string? right)
    {
        return !string.IsNullOrWhiteSpace(left) &&
            !string.IsNullOrWhiteSpace(right) &&
            string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private static bool SameSource(string? left, string? right)
    {
        return !string.IsNullOrWhiteSpace(left) &&
            !string.IsNullOrWhiteSpace(right) &&
            string.Equals(
                left.NormalizeExternalIdentifierName(),
                right.NormalizeExternalIdentifierName(),
                StringComparison.Ordinal);
    }

    private static bool SameIdentifierValue(string? left, string? right)
    {
        return !string.IsNullOrWhiteSpace(left) &&
            !string.IsNullOrWhiteSpace(right) &&
            string.Equals(
                left.NormalizeExternalIdentifierValue(),
                right.NormalizeExternalIdentifierValue(),
                StringComparison.Ordinal);
    }

    public async Task ExecuteAsync(JobContext context, CancellationToken cancellationToken = default)
    {
        if (!context.Parameters.TryGetValue(SubjectIdParameter, out var rawId) || string.IsNullOrWhiteSpace(rawId))
        {
            throw new ArgumentException("A subject ID is required.");
        }

        if (!Guid.TryParse(rawId, out var subjectId))
        {
            throw new ArgumentException("The subject ID has an invalid format.");
        }

        await RefreshAsync(
            subjectId,
            context.ManualTriggerUserId,
            context.CorrelationId,
            progressReporter,
            cancellationToken).ConfigureAwait(false);
    }

    internal async Task RefreshAsync(
        Guid subjectId,
        Guid? userId,
        Guid? correlationId,
        IProgressReporter? progress,
        CancellationToken cancellationToken = default)
    {
        progress?.Report(0, "The refresh will load the subject.");

        var subject = await subjectRepository.GetByIdAsync(subjectId, cancellationToken).ConfigureAwait(false);
        if (subject is null)
        {
            throw new InvalidOperationException($"Subject {subjectId} does not exist.");
        }

        var container = subject.MetadataContainer ??
            await metadataContainerRepository.GetByIdAsync(subject.MetadataContainerId, cancellationToken)
                .ConfigureAwait(false);
        if (container is null)
        {
            throw new InvalidOperationException(
                $"Subject {subject.Id} references missing metadata container {subject.MetadataContainerId}.");
        }

        var providers = metadataProviders
            .Where(p => p.SupportedSubjectKinds.Any(supported => supported == "*" || supported == subject.Kind))
            .ToArray();

        if (providers.Length == 0)
        {
            logger.LogDebug(
                "No subject metadata provider supports kind {Kind}, so the refresh skipped subject {SubjectId}",
                subject.Kind,
                subject.Id);
            progress?.Report(1, "The refresh stopped because the subject kind has no provider.");
            return;
        }

        var existingContributions = container.Contributions
            .OrderBy(c => c.Ordinal)
            .ThenBy(c => c.ProviderType, StringComparer.Ordinal)
            .ToList();

        progress?.Report(0, "The refresh will get metadata from the providers.");

        var providerTasks = new List<Task<ProviderFetchResult>>(providers.Length);
        foreach (var provider in providers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            providerTasks.Add(InvokeProviderAsync(provider, subject, existingContributions, cancellationToken));
        }

        var fetchResults = await Task.WhenAll(providerTasks).ConfigureAwait(false);

        var replacements = new List<ContributionReplacement>();
        var completed = 0;
        foreach (var fetch in fetchResults)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(providers.Length == 0 ? 0.5 : Math.Min(0.95, completed / (double)providers.Length), null);
            completed++;

            if (fetch.Exception is not null)
            {
                logger.LogWarning(
                    fetch.Exception,
                    "A subject metadata provider failed for subject {SubjectId}." +
                    " The refresh continued with the other providers",
                    subject.Id);
                continue;
            }

            var contribution = fetch.Contribution;
            if (contribution is null)
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(contribution.ProviderType))
            {
                logger.LogWarning(
                    "A subject metadata contribution for subject {SubjectId} has no ProviderType." +
                    " The upsert key is not available, so the refresh skipped the contribution",
                    subject.Id);
                continue;
            }

            var providerContributions = FindProviderContributions(existingContributions, contribution);
            var ordinal = providerContributions.Length > 0
                ? providerContributions.Min(c => c.Ordinal)
                : (existingContributions.Count == 0 ? 0 : existingContributions.Max(c => c.Ordinal) + 1);

            contribution.MetadataContainerId = container.Id;
            contribution.Ordinal = ordinal;
            contribution.DateLastRefreshed = timeProvider.GetUtcNow().UtcDateTime;

            replacements.Add(new(providerContributions, contribution));

            existingContributions.RemoveAll(c => providerContributions.Any(p => p.Id == c.Id));
            existingContributions.Add(contribution);
            existingContributions = [.. existingContributions
                .OrderBy(c => c.Ordinal)
                .ThenBy(c => c.ProviderType, StringComparer.Ordinal)];
        }

        cancellationToken.ThrowIfCancellationRequested();
        await ApplyRefreshWritesAsync(container.Id, replacements, subject, userId, correlationId, cancellationToken)
            .ConfigureAwait(false);

        progress?.Report(1, "The subject metadata refresh completed.");
    }

    private async Task ApplyRefreshWritesAsync(
        Guid containerId,
        IReadOnlyList<ContributionReplacement> replacements,
        Subject subject,
        Guid? userId,
        Guid? correlationId,
        CancellationToken cancellationToken)
    {
        await transactionManager.ExecuteWithTransactionAsync(
            async _ =>
            {
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
                            cancellationToken).ConfigureAwait(false);
                        replacement.Contribution.Id = updated.Id;
                    }
                    else
                    {
                        if (replacement.ExistingProviderContributions.Count > 1)
                        {
                            var existingIds = replacement.ExistingProviderContributions.Select(c => c.Id).ToArray();
                            var primaryImageUrls = await metadataContainerRepository
                                .ListPrimaryImageUrlsAsync(existingIds, cancellationToken)
                                .ConfigureAwait(false);
                            var primaryImage = replacement.Contribution.Images
                                .Where(i => primaryImageUrls.Contains(i.Url))
                                .OrderBy(i => i.Ordinal)
                                .ThenBy(i => i.Url.AbsoluteUri, StringComparer.Ordinal)
                                .FirstOrDefault();
                            foreach (var image in replacement.Contribution.Images)
                            {
                                image.IsPrimary = ReferenceEquals(image, primaryImage);
                            }

                            await metadataContainerRepository.RemoveContributionsAsync(
                                replacement.ExistingProviderContributions,
                                userId,
                                correlationId,
                                cancellationToken).ConfigureAwait(false);
                        }

                        await metadataContainerRepository.AddContributionAsync(
                            containerId,
                            replacement.Contribution,
                            userId,
                            cancellationToken).ConfigureAwait(false);
                    }
                }

                await MarkRefreshedAsync(subject, userId, correlationId, cancellationToken).ConfigureAwait(false);
            },
            cancellationToken).ConfigureAwait(false);
    }

    private async Task MarkRefreshedAsync(
        Subject subject,
        Guid? userId,
        Guid? correlationId,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;

        subject.DateLastRefreshed = now;
        await subjectRepository.UpdateAsync(subject, userId, cancellationToken).ConfigureAwait(false);
        await metadataContainerRepository
            .UpdateLastRefreshedAsync(subject.MetadataContainerId, now, cancellationToken)
            .ConfigureAwait(false);

        logger.LogDebug(
            "The refresh updated the metadata for subject {SubjectId} of kind {Kind}",
            subject.Id,
            subject.Kind);
    }

    private sealed record ProviderFetchResult(MetadataContribution? Contribution, Exception? Exception);

    private sealed record ContributionReplacement(
        IReadOnlyList<MetadataContribution> ExistingProviderContributions,
        MetadataContribution Contribution);
}
