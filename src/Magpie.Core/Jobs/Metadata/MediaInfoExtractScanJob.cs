using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions;
using Magpie.Abstractions.Repositories;
using Magpie.Models;
using Magpie.Models.Arguments;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Jobs;
using Magpie.Sdk.Events;
using Magpie.Sdk.Jobs;
using Magpie.Sdk.Media;
using Magpie.Sdk.Services;

using Microsoft.Extensions.Logging;

namespace Magpie.Core.Jobs.Metadata;

internal sealed class MediaInfoExtractScanJob(
    IMediaFileRepository mediaFileRepository,
    IJobManager jobManager,
    TimeProvider timeProvider,
    ILogger<MediaInfoExtractScanJob> logger,
    IProgressReporter progressReporter,
    IMediaInfoExtractor? extractor = null) : IJob
{
    public static readonly JobSpec Spec = new()
    {
        JobType = MagpieJobs.Types.MediaInfoExtractScan,
        DisplayName = "Media info extract scan",
        Description = "Scans for missing or stale media information and queues them inspection.",
        Category = "Metadata",
        Scope = JobScope.Content,
        Enabled = true,
        OnDemand = true,
        FailurePolicy = JobFailurePolicy.Requeue,
        Timeout = TimeSpan.FromMinutes(5),
        SupportedTriggerModes = [JobTriggerMode.Manual, JobTriggerMode.Schedule, JobTriggerMode.Event],
        ConcurrencyScope = ConcurrencyScope.PerTypeAndTarget,
        LockPartitionFields = [MagpieJobs.Parameters.ContentHierarchyItemId],
        LockPartitionEventContextKeys = new Dictionary<string, string>
        {
            [MagpieJobs.Parameters.ContentHierarchyItemId] = MagpieEvents.Keys.ContentHierarchyItemId
        },
        Parameters =
        [
            new(
                MagpieJobs.Parameters.ContentHierarchyItemId,
                "The content hierarchy item to scan again. Leave this parameter empty to scan missing and stale " +
                "library content.",
                DataType.String,
                false,
                null),
            new(
                MagpieJobs.Parameters.MaxAgeHours,
                "Hours before extracted media info is considered stale.",
                DataType.Number,
                false,
                DefaultMaxAgeHours.ToString(CultureInfo.InvariantCulture))
        ],
    };

    private const int DefaultMaxAgeHours = 24;
    private const int MaximumMaxAgeHours = 36500 * 24;
    private const int MaxFilesPerRun = 500;

    private const string NoExtractorMessage = "The scan stopped because no media information extractor is registered.";

    private static bool IsAdministrativeTrigger(JobContext context)
    {
        return context.ScheduleTriggerId is not null ||
            (context.EventTriggerId is null && context.ManualTriggerUserId is null);
    }

    private static Guid? ResolveContentHierarchyItemId(JobContext context)
    {
        if (context.Parameters.TryGetValue(MagpieJobs.Parameters.ContentHierarchyItemId, out var rawParameter) &&
            !string.IsNullOrWhiteSpace(rawParameter))
        {
            if (!Guid.TryParse(rawParameter, out var parsed))
            {
                throw new ArgumentException("The content hierarchy item ID has an invalid format.");
            }

            return parsed;
        }

        if (context.Event is { Context: var eventContext } &&
            eventContext.TryGetValue(MagpieEvents.Keys.ContentHierarchyItemId, out var rawEventValue) &&
            !string.IsNullOrWhiteSpace(rawEventValue))
        {
            if (!Guid.TryParse(rawEventValue, out var parsed))
            {
                throw new ArgumentException("The job received an invalid content hierarchy item ID.");
            }

            return parsed;
        }

        return null;
    }

    public async Task ExecuteAsync(JobContext context, CancellationToken cancellationToken = default)
    {
        var contentHierarchyItemId = ResolveContentHierarchyItemId(context);

        if (contentHierarchyItemId is null && !IsAdministrativeTrigger(context))
        {
            throw new MagpieException(
                "A library-wide media info extract scan requires a scheduled or system trigger; supply a content " +
                "hierarchy item id to scan a single content target on demand.");
        }

        progressReporter.Report(0, "The scan will load media files that need media information.");

        if (extractor is null)
        {
            logger.LogInformation("No media info extractor is registered, so the scan skipped this run");
            progressReporter.Report(1, NoExtractorMessage);
            return;
        }

        var candidates = contentHierarchyItemId is { } targetId
            ? await LoadTargetCandidatesAsync(targetId, cancellationToken).ConfigureAwait(false)
            : await LoadLibraryCandidatesAsync(context.Parameters, cancellationToken).ConfigureAwait(false);
        if (candidates.Count == 0)
        {
            progressReporter.Report(1, "The media information scan completed because no file needs media information.");
            return;
        }

        logger.LogInformation(
            "The media info extract scan will enqueue up to {Count} media info extract jobs",
            candidates.Count);

        var total = candidates.Count;
        var enqueued = 0;
        for (var i = 0; i < candidates.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progressReporter.ReportFraction(i, total);

            var candidate = candidates[i];
            try
            {
                await jobManager.ExecuteAsync(
                    new JobEnqueueRequest
                    {
                        JobType = MagpieJobs.Types.MediaInfoExtractItem,
                        Parameters = new Dictionary<string, string>()
                        {
                            [MagpieJobs.Parameters.MediaFileId] = candidate.Id.ToString("D"),
                            [MagpieJobs.Parameters.FilePath] = candidate.FilePath,
                            [MagpieJobs.Parameters.RequireMissingMediaInfo] =
                                contentHierarchyItemId is null && candidate.MediaInfo is null
                                    ? bool.TrueString
                                    : bool.FalseString,
                        },
                        Mode = JobExecutionMode.Queue,
                        UserId = context.ManualTriggerUserId,
                        CorrelationId = context.CorrelationId,
                    },
                    cancellationToken).ConfigureAwait(false);
                enqueued++;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogWarning(
                    ex,
                    "The media info extract scan failed to enqueue an extract for media file {MediaFileId}",
                    candidate.Id);
            }
        }

        progressReporter.Report(1, $"The media information scan completed and queued {enqueued} of {total} files.");
    }

    private async Task<IReadOnlyList<MediaFile>> LoadLibraryCandidatesAsync(
        IReadOnlyDictionary<string, string> parameters,
        CancellationToken cancellationToken)
    {
        var maxAgeHours = ReadPositiveIntParameter(parameters);
        var cutoff = timeProvider.GetUtcNow().UtcDateTime - TimeSpan.FromHours(maxAgeHours);
        return (await mediaFileRepository.QueryAsync(
            new MediaFileQuery { MediaInfoStaleBefore = cutoff },
            new() { PageNumber = 1, PageSize = MaxFilesPerRun },
            null,
            cancellationToken).ConfigureAwait(false)).Items.ToList();
    }

    private async Task<IReadOnlyList<MediaFile>> LoadTargetCandidatesAsync(
        Guid contentHierarchyItemId,
        CancellationToken cancellationToken)
    {
        var files = await mediaFileRepository.QueryAllAsync(
            new MediaFileQuery { ContentHierarchyItemId = contentHierarchyItemId },
            cancellationToken: cancellationToken).ConfigureAwait(false);

        return files;
    }

    private int ReadPositiveIntParameter(IReadOnlyDictionary<string, string> parameters)
    {
        if (!parameters.TryGetValue(MagpieJobs.Parameters.MaxAgeHours, out var raw) || string.IsNullOrWhiteSpace(raw))
        {
            return DefaultMaxAgeHours;
        }

        if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var configured) &&
            configured is > 0 and <= MaximumMaxAgeHours)
        {
            return configured;
        }

        logger.LogWarning(
            "Parameter {Parameter} value {Raw} is not between 1 and {Maximum}, so the job will use the default" +
            " {Default}",
            MagpieJobs.Parameters.MaxAgeHours,
            raw,
            MaximumMaxAgeHours,
            DefaultMaxAgeHours);

        return DefaultMaxAgeHours;
    }
}
