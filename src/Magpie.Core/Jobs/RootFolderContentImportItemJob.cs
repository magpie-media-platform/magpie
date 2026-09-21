using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions;
using Magpie.Abstractions.Repositories;
using Magpie.Models;
using Magpie.Models.Enums;
using Magpie.Models.Import;
using Magpie.Sdk.Jobs;
using Magpie.Sdk.Services;

using Microsoft.Extensions.Logging;

namespace Magpie.Core.Jobs;

internal sealed class RootFolderContentImportItemJob(
    ILibraryImportService libraryImportService,
    ILibraryImportCandidateRepository libraryImportCandidateRepository,
    IProgressReporter progressReporter,
    ILogger<RootFolderContentImportItemJob> logger) : IJob
{
    public static readonly JobSpec Spec = new()
    {
        JobType = MagpieJobs.Types.RootFolderContentImportItem,
        DisplayName = "Root folder content import item",
        Description = "Imports one discovered file into its content library.",
        Category = "Content",
        Scope = JobScope.Content,
        Enabled = true,
        OnDemand = true,
        Timeout = TimeSpan.FromMinutes(30),
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
                nameof(LibraryImportMonitoringMode.Inherit))
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

            var result = await libraryImportService
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
                    progressReporter.Report(1, "The import completed.");
                    return;
                case LibraryImportCompletionStatus.Skipped:
                    logger.LogInformation(
                        "The job skipped import candidate {LibraryImportCandidateId}: {Reason}",
                        candidateId,
                        result.Message);
                    progressReporter.Report(1, result.Message ?? "The import found no file to process.");
                    return;
                default:
                    throw new MagpieException(result.Message ?? "Import failed.");
            }
        }
        catch (OperationCanceledException)
        {
            await libraryImportCandidateRepository
                .TryResetImportAsync(candidateId, context.ManualTriggerUserId, CancellationToken.None)
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
