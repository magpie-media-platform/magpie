using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions;
using Magpie.Models;
using Magpie.Models.Enums;
using Magpie.Models.Import;
using Magpie.Sdk.Jobs;
using Magpie.Sdk.Services;

using Microsoft.Extensions.Logging;

namespace Magpie.Core.Jobs;

internal sealed class RootFolderContentImportItemJob(
    ILibraryImportService libraryImportService,
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
                MatchIndexParameter,
                "The selected match index to import.",
                DataType.Number,
                false,
                "0")
        ],
    };

    private const string MatchIndexParameter = "matchIndex";

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

        var matchIndex = 0;
        if (context.Parameters.TryGetValue(MatchIndexParameter, out var rawMatchIndex) &&
            !string.IsNullOrWhiteSpace(rawMatchIndex))
        {
            if (!int.TryParse(rawMatchIndex, NumberStyles.Integer, CultureInfo.InvariantCulture, out matchIndex) ||
                matchIndex < 0)
            {
                throw new ArgumentException("Match index must be zero or greater.");
            }
        }

        progressReporter.Report(0, "The import will process the file.");

        var result = await libraryImportService
            .ImportCandidateAsync(
                candidateId,
                matchIndex,
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
}
