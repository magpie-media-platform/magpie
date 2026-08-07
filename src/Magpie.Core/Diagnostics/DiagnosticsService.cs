using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Repositories;
using Magpie.Models;
using Magpie.Models.Api;
using Magpie.Models.Arguments;
using Magpie.Models.Data;
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
    private const string SettingsGroupName = "diagnostics";
    private const string LastScannedAtSettingKey = "last-scanned-at";

    public async Task<DiagnosticFindings> GetFindingsAsync(CancellationToken cancellationToken = default)
    {
        var findings = (await findingRepository.ListAllAsync(cancellationToken).ConfigureAwait(false)).ToList();
        var lastScannedAt = await ReadLastScannedAtAsync(cancellationToken).ConfigureAwait(false);

        return new DiagnosticFindings
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
            return DiagnosticRepairResult.Failed("The finding no longer exists; re-run the doctor scan.");
        }

        if (string.IsNullOrWhiteSpace(finding.RepairId))
        {
            return DiagnosticRepairResult.Failed("The finding has no repair.");
        }

        return await RunRepairAsync(finding.RepairId, finding, cancellationToken).ConfigureAwait(false);
    }

    private async Task<DiagnosticRepairResult> RunRepairAsync(
        string repairId,
        DiagnosticFinding finding,
        CancellationToken cancellationToken)
    {
        var repair = repairs.LastOrDefault(r => string.Equals(r.RepairId, repairId, StringComparison.Ordinal));
        if (repair is null)
        {
            logger.LogWarning("No diagnostic repair is registered for repair id {RepairId}", repairId);
            return DiagnosticRepairResult.Failed($"No repair {repairId} is registered.");
        }

        var result = await repair.RunAsync(finding, cancellationToken).ConfigureAwait(false);

        logger.LogInformation(
            "The diagnostics service ran repair {RepairId} on finding {FindingId} with success {Success}",
            repairId,
            finding.Id,
            result.Success);

        return result;
    }

    private async Task<DateTime?> ReadLastScannedAtAsync(CancellationToken cancellationToken)
    {
        var group = await settingsRepository
            .GetByIdAsync(new SettingsGroupLookupKey(null, SettingsGroupName), cancellationToken)
            .ConfigureAwait(false);
        var settings = group?.Settings.ToDictionary(s => s.Key, s => s.Value, StringComparer.Ordinal)

            ?? new Dictionary<string, string>();
        if (!settings.TryGetValue(LastScannedAtSettingKey, out var raw) || string.IsNullOrWhiteSpace(raw))
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
