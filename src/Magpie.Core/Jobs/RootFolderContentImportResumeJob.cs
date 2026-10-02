using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Repositories;
using Magpie.Models;
using Magpie.Models.Arguments;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Jobs;
using Magpie.Sdk.Jobs;
using Magpie.Sdk.Services;

using Microsoft.Extensions.Logging;

namespace Magpie.Core.Jobs;

internal sealed class RootFolderContentImportResumeJob(
    ILibraryImportCandidateRepository libraryImportCandidateRepository,
    IJobManager jobManager,
    IProgressReporter progressReporter,
    ILogger<RootFolderContentImportResumeJob> logger) : IJob
{
    public static readonly JobSpec Spec = new()
    {
        JobType = MagpieJobs.Types.RootFolderContentImportResume,
        DisplayName = "Root folder content import resume",
        Description = "Resumes the library imports that waited for the metadata refresh of an item after that " +
            "refresh ends.",
        Category = "Content",
        Scope = JobScope.Content,
        Enabled = true,
        OnDemand = true,
        SupportedTriggerModes = [JobTriggerMode.Manual],
        ConcurrencyScope = ConcurrencyScope.PerTarget,
        LockPartitionFields = [MagpieJobs.Parameters.ContentHierarchyItemId],
        Parameters =
        [
            new(
                MagpieJobs.Parameters.ContentHierarchyItemId,
                "Required. The content hierarchy item whose metadata refresh the imports wait for.",
                DataType.String,
                true,
                null)
            {
                ValidateValue = value => string.IsNullOrWhiteSpace(value) || Guid.TryParse(value, out _)
                    ? null : "must be a valid GUID."
            }
        ],
    };

    private const int ImportExecutionPageSize = 500;

    public async Task ExecuteAsync(JobContext context, CancellationToken cancellationToken = default)
    {
        if (!context.Parameters.TryGetValue(MagpieJobs.Parameters.ContentHierarchyItemId, out var rawItemId) ||
            !Guid.TryParse(rawItemId, out var itemId))
        {
            throw new ArgumentException("The content hierarchy item ID is missing or has an invalid format.");
        }

        var queuedRefreshes = await jobManager.QueryExecutionsAsync(
            new JobExecutionQuery
            {
                JobType = MagpieJobs.Types.ContentMetadataRefreshItem,
                Status = JobExecutionStatus.Queued,
                ParameterValueReferences = [itemId.ToString("D")]
            },
            new() { PageNumber = 1, PageSize = 1 },
            null,
            cancellationToken).ConfigureAwait(false);
        if (queuedRefreshes.Items.Any())
        {
            progressReporter.Report(1, "The resume stopped because a metadata refresh of the item is still queued.");
            return;
        }

        var waiting = await libraryImportCandidateRepository.QueryAllAsync(
            new LibraryImportCandidateQuery
            {
                Status = LibraryImportCandidateStatus.Importing,
                ContentHierarchyItemIds = [itemId]
            },
            cancellationToken: cancellationToken).ConfigureAwait(false);
        var latestImports = await FindLatestImportsAsync(
            [.. waiting.Select(candidate => candidate.Id)],
            cancellationToken).ConfigureAwait(false);

        var resumed = 0;
        foreach (var candidate in waiting)
        {
            if (!latestImports.TryGetValue(candidate.Id, out var latest))
            {
                await libraryImportCandidateRepository
                    .MarkImportFailedAsync(
                        candidate.Id,
                        "The import execution that waited for the metadata refresh no longer exists.",
                        context.ManualTriggerUserId,
                        cancellationToken)
                    .ConfigureAwait(false);
                continue;
            }

            if (latest.Status is JobExecutionStatus.Queued or JobExecutionStatus.Running or
                JobExecutionStatus.CancellationRequested)
            {
                continue;
            }

            await jobManager.ExecuteAsync(
                new JobEnqueueRequest
                {
                    JobType = MagpieJobs.Types.RootFolderContentImportItem,
                    Parameters = new Dictionary<string, string>(latest.Parameters)
                    {
                        [MagpieJobs.Parameters.MetadataRefreshEndedItemId] = itemId.ToString("D")
                    },
                    UserId = latest.CreatedByUserId,
                    CorrelationId = latest.CorrelationId,
                    IsSuccessor = true
                },
                cancellationToken).ConfigureAwait(false);
            resumed++;
        }

        logger.LogInformation(
            "The import resume for item {ContentHierarchyItemId} resumed {Resumed} of {Waiting} waiting imports",
            itemId,
            resumed,
            waiting.Count);
        progressReporter.Report(1, $"The resume queued {resumed} of {waiting.Count} waiting imports.");
    }

    private async Task<Dictionary<Guid, JobExecution>> FindLatestImportsAsync(
        HashSet<Guid> candidateIds,
        CancellationToken cancellationToken)
    {
        var latest = new Dictionary<Guid, JobExecution>();
        for (var pageNumber = 1; latest.Count < candidateIds.Count; pageNumber++)
        {
            var executions = (await jobManager.QueryExecutionsAsync(
                new JobExecutionQuery { JobType = MagpieJobs.Types.RootFolderContentImportItem },
                new() { PageNumber = pageNumber, PageSize = ImportExecutionPageSize },
                null,
                cancellationToken).ConfigureAwait(false)).Items.ToList();
            foreach (var execution in executions)
            {
                if (execution.Parameters.TryGetValue(MagpieJobs.Parameters.CandidateId, out var rawCandidateId) &&
                    Guid.TryParse(rawCandidateId, out var candidateId) &&
                    candidateIds.Contains(candidateId))
                {
                    latest.TryAdd(candidateId, execution);
                }
            }

            if (executions.Count < ImportExecutionPageSize)
            {
                break;
            }
        }

        return latest;
    }
}
