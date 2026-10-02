using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Repositories;
using Magpie.Core.Jobs;
using Magpie.Models;
using Magpie.Models.Api;
using Magpie.Models.Arguments;
using Magpie.Sdk.Diagnostics;
using Magpie.Sdk.Services;

using Microsoft.Extensions.Logging;

namespace Magpie.Core.Diagnostics;

internal sealed class DiagnosticsService(
    IEnumerable<IDiagnosticRepair> repairs,
    IDiagnosticFindingRepository findingRepository,
    ISettingsRepository settingsRepository,
    ILogger<DiagnosticsService> logger) : IDiagnosticsService
{
    public async Task<DiagnosticFindings> GetFindingsAsync(CancellationToken cancellationToken = default)
    {
        var findings = (await findingRepository.ListAllAsync(cancellationToken).ConfigureAwait(false)).ToList();
        var lastScannedAt = await ReadLastScannedAtAsync(cancellationToken).ConfigureAwait(false);

        return new()
        {
            Findings = findings,
            LastScannedAt = lastScannedAt,
        };
    }

    public async Task<DiagnosticRepairResult> RepairAsync(Guid findingId, CancellationToken cancellationToken = default)
    {
        var finding = await findingRepository.GetByIdAsync(findingId, cancellationToken).ConfigureAwait(false);
        if (finding is null)
        {
            return DiagnosticRepairResult.Failed("The finding no longer exists.");
        }

        if (string.IsNullOrWhiteSpace(finding.RepairId))
        {
            return DiagnosticRepairResult.Failed("The finding has no repair.");
        }

        var repair = repairs.LastOrDefault(r => string.Equals(r.RepairId, finding.RepairId, StringComparison.Ordinal));
        if (repair is null)
        {
            logger.LogWarning("No diagnostic repair is registered for repair id {RepairId}", finding.RepairId);
            return DiagnosticRepairResult.Failed($"No repair {finding.RepairId} is registered.");
        }

        var result = await repair.RunAsync(finding, cancellationToken).ConfigureAwait(false);

        logger.LogInformation(
            "The diagnostics service ran repair {RepairId} on finding {FindingId} with success {Success}",
            finding.RepairId,
            finding.Id,
            result.Success);

        return result;
    }

    private async Task<DateTime?> ReadLastScannedAtAsync(CancellationToken cancellationToken)
    {
        var group = await settingsRepository
            .GetByIdAsync(new SettingsGroupLookupKey(null, MagpieJobs.Settings.DiagnosticsGroup), cancellationToken)
            .ConfigureAwait(false);
        var raw = group?.Settings.FirstOrDefault(s => s.Key == MagpieJobs.Settings.LastScannedAt)?.Value;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        return DateTimeOffset.TryParse(
            raw,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal,
            out var lastScannedAt)
            ? lastScannedAt.UtcDateTime
            : null;
    }
}
