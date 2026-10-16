using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Repositories;
using Magpie.Models;
using Magpie.Models.Arguments;
using Magpie.Models.Enums;
using Magpie.Models.Jobs;
using Magpie.Sdk.Content;
using Magpie.Sdk.Jobs;
using Magpie.Sdk.Services;

using Microsoft.Extensions.Logging;

namespace Magpie.Core.Jobs.Metadata;

internal sealed class SubjectMetadataRefreshScanJob(
    IEnumerable<ISubjectMetadataProvider> metadataProviders,
    IJobManager jobManager,
    ISubjectRepository subjectRepository,
    TimeProvider timeProvider,
    ILogger<SubjectMetadataRefreshScanJob> logger,
    IProgressReporter progressReporter) : IJob
{
    public static readonly JobSpec Spec = new()
    {
        JobType = MagpieJobs.Types.SubjectMetadataRefreshScan,
        DisplayName = "Subject metadata refresh scan",
        Description = "Finds and queues subjects that need a metadata refresh.",
        Category = "Content",
        Scope = JobScope.Administrative,
        Enabled = true,
        OnDemand = true,
        FailurePolicy = JobFailurePolicy.Requeue,
        SupportedTriggerModes = [JobTriggerMode.Manual, JobTriggerMode.Schedule, JobTriggerMode.Event],
        ConcurrencyScope = ConcurrencyScope.PerType,
        Parameters =
        [
            new(
                MagpieJobs.Parameters.SubjectStalenessDays,
                "Days before refreshed subject metadata is considered stale.",
                DataType.Number,
                false,
                DefaultSubjectStalenessDays.ToString(CultureInfo.InvariantCulture))
        ],
    };

    private const int DefaultSubjectStalenessDays = 30;

    private static double? GetRefreshProgress(int completed, int total)
    {
        return total == 0 ? 1 : completed / (double)total;
    }

    public async Task ExecuteAsync(JobContext jobContext, CancellationToken cancellationToken = default)
    {
        var userId = jobContext.ManualTriggerUserId;
        var correlationId = jobContext.CorrelationId;

        progressReporter.Report(0, "The scan will load subjects that need a refresh.");

        var subjectStalenessDays = ReadPositiveIntParameter(jobContext.Parameters);
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var cutoff = subjectStalenessDays >= (now - DateTime.MinValue).TotalDays
            ? DateTime.MinValue
            : now.AddDays(-subjectStalenessDays);
        var (supportedKinds, includeAllKinds) = ResolveSupportedKinds();
        if (!includeAllKinds && supportedKinds.Count == 0)
        {
            progressReporter.Report(1, "The subject metadata refresh scan completed.");
            return;
        }

        const int pageSize = 500;
        var staleSubjectIds = new List<Guid>();
        var pageNumber = 1;
        while (true)
        {
            var page = await subjectRepository.QueryAsync(
                new SubjectQuery
                {
                    Kinds = includeAllKinds ? null : supportedKinds,
                    LastRefreshedBefore = cutoff,
                    HasExternalIds = true
                },
                new() { PageNumber = pageNumber, PageSize = pageSize },
                null,
                cancellationToken).ConfigureAwait(false);
            var pageItems = page.Items.ToList();
            staleSubjectIds.AddRange(pageItems.Select(subject => subject.Id));
            if (pageItems.Count < pageSize)
            {
                break;
            }

            pageNumber++;
        }

        var total = staleSubjectIds.Count;
        var completed = 0;
        foreach (var subjectId in staleSubjectIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progressReporter.Report(GetRefreshProgress(completed, total), null);

            try
            {
                await EnqueueRefreshAsync(subjectId, userId, correlationId, cancellationToken)
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
                    "The subject metadata refresh scan failed to enqueue a refresh for subject {SubjectId}",
                    subjectId);
            }

            completed++;
        }

        progressReporter.Report(1, "The subject metadata refresh scan completed.");
    }

    private (IReadOnlyCollection<string> SupportedKinds, bool IncludeAllKinds) ResolveSupportedKinds()
    {
        var supportedKinds = new HashSet<string>();
        foreach (var provider in metadataProviders)
        {
            foreach (var kind in provider.SupportedSubjectKinds)
            {
                if (kind == "*")
                {
                    return ([], true);
                }

                supportedKinds.Add(kind);
            }
        }

        return (supportedKinds, false);
    }

    private int ReadPositiveIntParameter(IReadOnlyDictionary<string, string> parameters)
    {
        if (!parameters.TryGetValue(MagpieJobs.Parameters.SubjectStalenessDays, out var raw))
        {
            return DefaultSubjectStalenessDays;
        }

        if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var configured) && configured > 0)
        {
            return configured;
        }

        logger.LogWarning(
            "Parameter {Parameter} value {Raw} is not positive, so the job will use the default {Default}",
            MagpieJobs.Parameters.SubjectStalenessDays,
            raw,
            DefaultSubjectStalenessDays);

        return DefaultSubjectStalenessDays;
    }

    private async Task EnqueueRefreshAsync(
        Guid subjectId,
        Guid? userId,
        Guid? correlationId,
        CancellationToken cancellationToken)
    {
        await jobManager.ExecuteAsync(
            new JobEnqueueRequest
            {
                JobType = MagpieJobs.Types.SubjectMetadataRefreshItem,
                Parameters = new Dictionary<string, string>
                {
                    [MagpieJobs.Parameters.SubjectId] = subjectId.ToString()
                },
                Mode = JobExecutionMode.Queue,
                UserId = userId,
                CorrelationId = correlationId,
            },
            cancellationToken).ConfigureAwait(false);

        logger.LogDebug("The subject metadata refresh scan enqueued a refresh for subject {SubjectId}", subjectId);
    }
}
