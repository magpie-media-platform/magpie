using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

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
        Description = "Queues media information extraction for the files of a target that the media info policy " +
            "selects.",
        Category = "Metadata",
        Scope = JobScope.Content,
        Enabled = true,
        OnDemand = true,
        FailurePolicy = JobFailurePolicy.Requeue,
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
                "The content hierarchy item whose files to scan. Leave this parameter empty to scan the library.",
                DataType.String,
                false,
                null)
            {
                ValidateValue = value => string.IsNullOrWhiteSpace(value) || Guid.TryParse(value, out _)
                    ? null : "must be a valid GUID."
            },
            new(
                MagpieJobs.Parameters.MediaInfoPolicy,
                "The files to extract: Missing (no media info), MissingOrStale (no media info or older than " +
                "maxAgeHours) or All.",
                DataType.String,
                false,
                MagpieJobs.ParameterValues.MissingOrStaleMediaInfoPolicy)
            {
                ValidateValue = value => string.IsNullOrWhiteSpace(value) || ParseMediaInfoPolicy(value) is not null
                    ? null : "must be Missing, MissingOrStale or All."
            },
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

    private static string? ParseMediaInfoPolicy(string value) =>
        new[]
        {
            MagpieJobs.ParameterValues.MissingMediaInfoPolicy,
            MagpieJobs.ParameterValues.MissingOrStaleMediaInfoPolicy,
            MagpieJobs.ParameterValues.AllMediaInfoPolicy
        }.FirstOrDefault(policy => string.Equals(policy, value.Trim(), StringComparison.OrdinalIgnoreCase));

    private static string ResolveMediaInfoPolicy(JobContext context)
    {
        if (context.Parameters.TryGetValue(MagpieJobs.Parameters.MediaInfoPolicy, out var rawPolicy) &&
            !string.IsNullOrWhiteSpace(rawPolicy))
        {
            return ParseMediaInfoPolicy(rawPolicy) ?? throw new ArgumentException(
                $"The media info policy {rawPolicy} is not Missing, MissingOrStale or All.");
        }

        return MagpieJobs.ParameterValues.MissingOrStaleMediaInfoPolicy;
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

        return null;
    }

    public async Task ExecuteAsync(JobContext context, CancellationToken cancellationToken = default)
    {
        var contentHierarchyItemId = ResolveContentHierarchyItemId(context);
        var policy = ResolveMediaInfoPolicy(context);

        progressReporter.Report(0, "The scan will load media files that need media information.");

        if (extractor is null)
        {
            logger.LogInformation("No media info extractor is registered, so the scan skipped this run");
            progressReporter.Report(1, NoExtractorMessage);
            return;
        }

        var candidates = await LoadCandidatesAsync(
            contentHierarchyItemId,
            policy,
            context.Parameters,
            cancellationToken).ConfigureAwait(false);
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

            var requireMissingMediaInfo = policy switch
            {
                MagpieJobs.ParameterValues.MissingMediaInfoPolicy => true,
                MagpieJobs.ParameterValues.MissingOrStaleMediaInfoPolicy => candidate.MediaInfo is null,
                _ => false
            };
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
                                requireMissingMediaInfo ? bool.TrueString : bool.FalseString,
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

    private async Task<IReadOnlyList<MediaFile>> LoadCandidatesAsync(
        Guid? contentHierarchyItemId,
        string policy,
        IReadOnlyDictionary<string, string> parameters,
        CancellationToken cancellationToken)
    {
        var query = new MediaFileQuery
        {
            ContentHierarchyItemId = contentHierarchyItemId,
            MediaInfoStaleBefore = policy switch
            {
                MagpieJobs.ParameterValues.MissingMediaInfoPolicy => DateTime.MinValue,
                MagpieJobs.ParameterValues.MissingOrStaleMediaInfoPolicy =>
                    timeProvider.GetUtcNow().UtcDateTime - TimeSpan.FromHours(ReadPositiveIntParameter(parameters)),
                _ => null
            }
        };
        if (contentHierarchyItemId is not null || query.MediaInfoStaleBefore is null)
        {
            return await mediaFileRepository.QueryAllAsync(query, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }

        return (await mediaFileRepository.QueryAsync(
            query,
            new() { PageNumber = 1, PageSize = MaxFilesPerRun },
            null,
            cancellationToken).ConfigureAwait(false)).Items.ToList();
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
