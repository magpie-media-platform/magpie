using System;
using System.Collections.Generic;
using System.Globalization;
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
using Magpie.Models.Jobs;
using Magpie.Models.Patch;
using Magpie.Sdk.Diagnostics;
using Magpie.Sdk.Jobs;
using Magpie.Sdk.Services;

using Microsoft.Extensions.Logging;

namespace Magpie.Core.Jobs;

internal sealed class DiagnosticCheckJob(
    IEnumerable<IDiagnosticCheck> diagnostics,
    IDiagnosticFindingRepository findingRepository,
    IMediaServerRepository mediaServers,
    IRootFolderRepository rootFolders,
    IDownloadClientRepository downloadClients,
    IIndexerFeedRepository indexerFeeds,
    ISettingsRepository settingsRepository,
    ITransactionManager transactionManager,
    IEventBus eventBus,
    TimeProvider timeProvider,
    ILogger<DiagnosticCheckJob> logger) : IJob
{
    public static readonly JobSpec Spec = new()
    {
        JobType = MagpieJobs.Types.DiagnosticCheck,
        DisplayName = "Diagnostic check",
        Description = "Runs the system diagnostics and saves the results.",
        Category = "System",
        Scope = JobScope.System,
        Enabled = true,
        OnDemand = true,
        FailurePolicy = JobFailurePolicy.Requeue,
        SupportedTriggerModes = [JobTriggerMode.Manual, JobTriggerMode.Schedule, JobTriggerMode.Event],
        ConcurrencyScope = ConcurrencyScope.PerType,
        Parameters = [],
    };

    private static string FindingKey(DiagnosticFinding finding) =>
        $"{finding.DiagnosticCheckId}|{finding.AffectedEntity}|{finding.Title}";

    private static bool HasMaterialChange(DiagnosticFinding previous, DiagnosticFinding current) =>
        previous.Severity != current.Severity ||
        !string.Equals(previous.Detail, current.Detail, StringComparison.Ordinal) ||
        !string.Equals(previous.RepairId, current.RepairId, StringComparison.Ordinal);

    public async Task ExecuteAsync(JobContext context, CancellationToken cancellationToken = default)
    {
        var detectedAt = timeProvider.GetUtcNow().UtcDateTime;
        var collected = new List<DiagnosticFinding>();
        var diagnosticContext = await BuildContextAsync(cancellationToken).ConfigureAwait(false);

        var existingFindings = (await findingRepository.ListAllAsync(cancellationToken).ConfigureAwait(false))
            .ToDictionary(FindingKey, StringComparer.Ordinal);

        foreach (var diagnostic in diagnostics)
        {
            cancellationToken.ThrowIfCancellationRequested();

            IReadOnlyList<DiagnosticFinding> findings;
            try
            {
                findings = await diagnostic.RunAsync(diagnosticContext, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "Diagnostic {DiagnosticId} threw an exception, so the job skipped this diagnostic",
                    diagnostic.DiagnosticCheckId);

                continue;
            }

            var newlyRaised = new List<DiagnosticFinding>();
            var updated = new List<DiagnosticFinding>();
            foreach (var finding in findings)
            {
                finding.Id = Guid.NewGuid();
                finding.DiagnosticCheckId = diagnostic.DiagnosticCheckId;
                finding.DetectedAt = detectedAt;
                collected.Add(finding);

                if (!existingFindings.TryGetValue(FindingKey(finding), out var previous))
                {
                    newlyRaised.Add(finding);
                }
                else if (HasMaterialChange(previous, finding))
                {
                    updated.Add(finding);
                }
            }

            await SaveFindingsAsync(diagnostic.DiagnosticCheckId, findings, cancellationToken).ConfigureAwait(false);

            foreach (var finding in newlyRaised)
            {
                await eventBus
                    .PublishAsync(
                        DiagnosticEventBuilder.BuildDiagnosticFindingRaisedEvent(
                            finding.Id,
                            finding.DiagnosticCheckId,
                            finding.Severity.ToString()),
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            foreach (var finding in updated)
            {
                await eventBus.PublishAsync(
                    DiagnosticEventBuilder.BuildDiagnosticFindingUpdatedEvent(
                        finding.Id,
                        finding.DiagnosticCheckId,
                        finding.Severity.ToString()),
                    cancellationToken).ConfigureAwait(false);
            }
        }

        var completedAt = timeProvider.GetUtcNow().UtcDateTime;
        await SaveLastScannedAtAsync(completedAt, cancellationToken).ConfigureAwait(false);
        await eventBus.PublishAsync(
            DiagnosticEventBuilder.BuildDiagnosticScanCompletedEvent(collected.Count),
            cancellationToken)
            .ConfigureAwait(false);

        logger.LogInformation("The diagnostic check completed with {FindingCount} finding(s)", collected.Count);
    }

    private Task SaveFindingsAsync(
        string diagnosticCheckId,
        IReadOnlyList<DiagnosticFinding> findings,
        CancellationToken cancellationToken)
    {
        return transactionManager.ExecuteWithTransactionAsync(
            async ct =>
            {
                var existing = await findingRepository.QueryAllAsync(
                    new DiagnosticFindingQuery { DiagnosticCheckId = diagnosticCheckId },
                    cancellationToken: ct).ConfigureAwait(false);

                foreach (var finding in existing)
                {
                    await findingRepository.DeleteAsync(finding.Id, null, ct).ConfigureAwait(false);
                }

                foreach (var finding in findings)
                {
                    await findingRepository.CreateAsync(finding, null, ct).ConfigureAwait(false);
                }
            },
            cancellationToken);
    }

    private async Task<DiagnosticContext> BuildContextAsync(CancellationToken cancellationToken)
    {
        return new DiagnosticContext
        {
            MediaServers = [.. await mediaServers.ListAllAsync(cancellationToken).ConfigureAwait(false)],
            RootFolders = [.. await rootFolders.ListAllAsync(cancellationToken).ConfigureAwait(false)],
            DownloadClients = [.. await downloadClients.ListAllAsync(cancellationToken).ConfigureAwait(false)],
            IndexerFeeds = [.. await indexerFeeds.ListAllAsync(cancellationToken).ConfigureAwait(false)],
        };
    }

    private async Task SaveLastScannedAtAsync(DateTime lastScannedAtUtc, CancellationToken cancellationToken)
    {
        var value = lastScannedAtUtc.ToString("O", CultureInfo.InvariantCulture);
        var group = await settingsRepository
            .GetByIdAsync(new SettingsGroupLookupKey(null, MagpieJobs.Settings.DiagnosticsGroup), cancellationToken)
            .ConfigureAwait(false);
        var exists = group?.Settings.Any(s => s.Key == MagpieJobs.Settings.LastScannedAt) ?? false;

        var entry = new Dictionary<string, string>() { [MagpieJobs.Settings.LastScannedAt] = value };
        await settingsRepository.PatchAsync(
            new SettingsGroupPatch
            {
                ExtensionId = null,
                Name = MagpieJobs.Settings.DiagnosticsGroup,
                Settings = new DictionaryPatch<string, string, string>
                {
                    Add = exists ? null : entry,
                    Update = exists ? entry : null
                }
            },
            modifiedByUserId: null,
            cancellationToken).ConfigureAwait(false);
    }
}
