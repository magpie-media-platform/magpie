using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions;
using Magpie.Abstractions.Repositories;
using Magpie.Models;
using Magpie.Models.Api;
using Magpie.Models.Enums;
using Magpie.Models.Import;
using Magpie.Models.Jobs;
using Magpie.Sdk.Jobs;
using Magpie.Sdk.Services;

using Microsoft.Extensions.Logging;

namespace Magpie.Core.Jobs;

internal sealed class RootFolderContentImportItemJob(
    ILibraryImportService libraryImportService,
    ILibraryImportCandidateRepository libraryImportCandidateRepository,
    IJobManager jobManager,
    IProgressReporter progressReporter,
    TimeProvider timeProvider,
    ILogger<RootFolderContentImportItemJob> logger) : IJob
{
    private static readonly TimeSpan WaitingRetryDelay = TimeSpan.FromMinutes(1);

    public static readonly JobSpec Spec = new()
    {
        JobType = MagpieJobs.Types.RootFolderContentImportItem,
        DisplayName = "Root folder content import item",
        Description = "Imports one discovered file into its content library.",
        Category = "Content",
        Scope = JobScope.Content,
        Enabled = true,
        OnDemand = true,
        SupportedTriggerModes = [JobTriggerMode.Manual],
        ConcurrencyScope = ConcurrencyScope.PerTypeAndTarget,
        LockPartitionFields = [MagpieJobs.Parameters.CandidateId],
        Parameters =
        [
            new(
                MagpieJobs.Parameters.CandidateId,
                "Required. The claimed library import candidate to import.",
                DataType.String,
                true,
                null),
            new(
                MagpieJobs.Parameters.MatchIndex,
                "The selected match index to import.",
                DataType.Number,
                false,
                "0"),
            new(
                MagpieJobs.Parameters.MonitoringMode,
                "The monitoring setup to apply during import.",
                DataType.String,
                false,
                nameof(LibraryImportMonitoringMode.Inherit)),
            new(
                "renamePreview",
                "The explicit file rename preview accepted by the user.",
                DataType.String,
                false,
                null),
            new(
                MagpieJobs.Parameters.Associate,
                "Whether to attach the file to existing library content only, without creating content.",
                DataType.Boolean,
                false,
                bool.FalseString)
        ],
    };

    private static string ExceptionMessages(Exception exception) =>
        string.Join(" -> ", GenerateExceptionMessages(exception));

    private static IEnumerable<string> GenerateExceptionMessages(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            yield return current.Message;
        }
    }

    public async Task ExecuteAsync(JobContext context, CancellationToken cancellationToken = default)
    {
        if (!context.Parameters.TryGetValue(
            MagpieJobs.Parameters.CandidateId,
            out var rawCandidateId) ||
            string.IsNullOrWhiteSpace(rawCandidateId))
        {
            throw new ArgumentException("An import candidate ID is required.");
        }

        if (!Guid.TryParse(rawCandidateId, out var candidateId))
        {
            throw new ArgumentException("The import candidate ID has an invalid format.");
        }

        try
        {
            var matchIndex = 0;
            if (context.Parameters.TryGetValue(MagpieJobs.Parameters.MatchIndex, out var rawMatchIndex) &&
                !string.IsNullOrWhiteSpace(rawMatchIndex))
            {
                if (!int.TryParse(rawMatchIndex, NumberStyles.Integer, CultureInfo.InvariantCulture, out matchIndex) ||
                    matchIndex < 0)
                {
                    throw new ArgumentException("Match index must be zero or greater.");
                }
            }

            var rawMonitoringMode = context.Parameters.TryGetValue(
                MagpieJobs.Parameters.MonitoringMode,
                out var configuredMonitoringMode)
                ? configuredMonitoringMode
                : nameof(LibraryImportMonitoringMode.Inherit);
            if (!Enum.TryParse<LibraryImportMonitoringMode>(
                rawMonitoringMode,
                ignoreCase: true,
                out var monitoringMode))
            {
                throw new ArgumentException("A valid monitoring mode is required.");
            }

            progressReporter.Report(0, "The import will process the file.");

            var associate = context.Parameters.TryGetValue(MagpieJobs.Parameters.Associate, out var rawAssociate) &&
                bool.TryParse(rawAssociate, out var parsedAssociate) && parsedAssociate;

            var result = associate
                ? await libraryImportService
                    .AssociateCandidateAsync(
                        candidateId,
                        context.ManualTriggerUserId,
                        context.CorrelationId,
                        cancellationToken)
                    .ConfigureAwait(false)
                : context.Parameters.TryGetValue("renamePreview", out var rawPreview) &&
                !string.IsNullOrWhiteSpace(rawPreview)
                ? await libraryImportService.ImportWithRenameAsync(candidateId, matchIndex, monitoringMode,
                    JsonSerializer.Deserialize<FileRenamePreview>(rawPreview)
                        ?? throw new ArgumentException("The rename preview is invalid."),
                    context.ManualTriggerUserId, context.CorrelationId, cancellationToken).ConfigureAwait(false)
                : await libraryImportService
                .ImportCandidateAsync(
                    candidateId,
                    matchIndex,
                    monitoringMode,
                    context.ManualTriggerUserId,
                    context.CorrelationId,
                    cancellationToken)
                .ConfigureAwait(false);

            switch (result.Status)
            {
                case LibraryImportCompletionStatus.Completed:
                    progressReporter.Report(1, result.Message ?? "The import completed.");
                    return;
                case LibraryImportCompletionStatus.Skipped:
                    logger.LogInformation(
                        "The job skipped import candidate {LibraryImportCandidateId}: {Reason}",
                        candidateId,
                        result.Message);
                    progressReporter.Report(1, result.Message ?? "The import found no file to process.");
                    return;
                case LibraryImportCompletionStatus.Waiting:
                    await jobManager.ExecuteAsync(
                        new JobEnqueueRequest
                        {
                            JobType = Spec.JobType,
                            Parameters = context.Parameters,
                            Mode = JobExecutionMode.Queue,
                            UserId = context.ManualTriggerUserId,
                            CorrelationId = context.CorrelationId,
                            IsSuccessor = true,
                            NotBeforeAt = timeProvider.GetUtcNow().UtcDateTime.Add(WaitingRetryDelay)
                        },
                        cancellationToken).ConfigureAwait(false);
                    progressReporter.Report(1, $"{result.Message} The import will try again in 1 minute.");
                    return;
                default:
                    throw new MagpieException(result.Message ?? "Import failed.");
            }
        }
        catch (OperationCanceledException)
        {
            await libraryImportCandidateRepository
                .TryResetImportAsync(candidateId, null, context.ManualTriggerUserId, CancellationToken.None)
                .ConfigureAwait(false);
            throw;
        }
        catch (Exception ex)
        {
            await libraryImportCandidateRepository
                .MarkImportFailedAsync(
                    candidateId,
                    ExceptionMessages(ex),
                    context.ManualTriggerUserId,
                    CancellationToken.None)
                .ConfigureAwait(false);
            throw;
        }
    }
}
