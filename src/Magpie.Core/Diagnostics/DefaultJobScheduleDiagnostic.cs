using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Jobs;
using Magpie.Abstractions.Repositories;
using Magpie.Core.Jobs;
using Magpie.Core.Jobs.Runtime;
using Magpie.Models;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Sdk.Diagnostics;

using Microsoft.Extensions.Configuration;

namespace Magpie.Core.Diagnostics;

internal sealed class DefaultJobScheduleDiagnostic(
    IScheduleTriggerRepository scheduleTriggers,
    IEventTriggerRepository eventTriggers,
    IJobRegistry jobRegistry,
    IConfiguration configuration) : IDiagnosticCheck
{
    private static readonly DateTime CadenceSampleStart = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan CadenceSampleWindow = TimeSpan.FromDays(400);
    private static readonly TimeSpan DefaultMinimumSearchAge = TimeSpan.FromHours(24);
    private const string Id = "core.config.missing-default-job";

    private const int MaximumCadenceIntervals = 10000;
    private const string ReleaseAutoGrabScanJobType = "ReleaseAutoGrabScan";
    private const string ReleaseSearchScanJobType = "ReleaseSearchScan";
    private const string DownloadImportScanJobType = "DownloadImportScan";
    private const string RetentionCleanupJobType = "RetentionCleanup";
    private const string DownloadStuckDetectionJobType = "DownloadStuckDetection";
    private const string DatabaseMaintenanceJobType = "DatabaseMaintenance";
    private const string IntegrationHealthCheckJobType = "IntegrationHealthCheck";
    private const string MediaInfoExtractScanJobType = "MediaInfoExtractScan";

    private enum DurationUnit
    {
        Days,
        Hours,
        Minutes
    }

    private enum ParameterFloor
    {
        NonNegative,
        Positive
    }

    public string DiagnosticCheckId => Id;

    public string Category => "Schedules";

    private TimeSpan MinimumSearchAge
    {
        get
        {
            var raw = configuration["Magpie:Search:MinimumSearchAgeHours"];
            return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var hours) && hours > 0
                ? TimeSpan.FromHours(hours)
                : DefaultMinimumSearchAge;
        }
    }

    private static void ValidateReleaseSearchScan(
        ScheduleTrigger trigger,
        CadenceMeasurement? cadence,
        TimeSpan minimumSearchAge,
        List<DiagnosticFinding> findings)
    {
        if (cadence is not { } measured)
        {
            return;
        }

        AddMoreOftenFindingIfNeeded(trigger, measured, TimeSpan.FromMinutes(15), findings);
        AddLessOftenFindingIfNeeded(
            trigger,
            measured,
            minimumSearchAge,
            "Magpie:Search:MinimumSearchAgeHours",
            findings);
    }

    private static void ValidateReleaseAutoGrabScan(
        ScheduleTrigger trigger,
        JobSpec spec,
        CadenceMeasurement? cadence,
        List<DiagnosticFinding> findings)
    {
        _ = ReadIntegerParameter(trigger, spec, "retryCooldownHours", ParameterFloor.Positive, findings);

        if (cadence is not { } measured)
        {
            return;
        }

        AddMoreOftenFindingIfNeeded(trigger, measured, TimeSpan.FromMinutes(30), findings);
        AddLessOftenFindingIfNeeded(trigger, measured, TimeSpan.FromHours(12), "12 hours", findings);
    }

    private static void ValidateRetentionCleanup(
        ScheduleTrigger trigger,
        JobSpec spec,
        CadenceMeasurement? cadence,
        List<DiagnosticFinding> findings)
    {
        var jobExecutionRetentionDays = ReadIntegerParameter(
            trigger,
            spec,
            "jobExecutionRetentionDays",
            ParameterFloor.NonNegative,
            findings);
        var auditRetentionDays = ReadIntegerParameter(
            trigger,
            spec,
            "auditRetentionDays",
            ParameterFloor.NonNegative,
            findings);
        var releaseObservationRetentionDays = ReadIntegerParameter(
            trigger,
            spec,
            "releaseObservationRetentionDays",
            ParameterFloor.NonNegative,
            findings);
        var failedDownloadRetentionDays = ReadIntegerParameter(
            trigger,
            spec,
            "failedDownloadRetentionDays",
            ParameterFloor.Positive,
            findings);

        if (releaseObservationRetentionDays is { } releaseDays &&
            failedDownloadRetentionDays is { } failedDays &&
            releaseDays > failedDays)
        {
            findings.Add(Warning(
                "Release observations are retained longer than failed downloads",
                "Release observations are kept longer than the failed downloads that use them. " +
                "Release observation retention should be reduced, or failed download retention increased.",
                JobDiagnosticAffectedEntityPrefixes.ScheduleTrigger + trigger.Id));
        }

        if (cadence is not { } measured)
        {
            return;
        }

        var validRetentionWindows = new[]
            {
                jobExecutionRetentionDays,
                auditRetentionDays,
                releaseObservationRetentionDays,
                failedDownloadRetentionDays,
            }
            .OfType<int>()
            .ToArray();
        if (validRetentionWindows.Length != 4)
        {
            return;
        }

        var shortestRetentionDays = validRetentionWindows.Min();
        if (shortestRetentionDays >= 7)
        {
            AddMoreOftenFindingIfNeeded(trigger, measured, TimeSpan.FromHours(6), findings);
        }

        if (shortestRetentionDays > 0 &&
            TryCreateDuration(shortestRetentionDays, DurationUnit.Days, trigger, "retentionDays", findings)
                is { } maximumCadence)
        {
            AddLessOftenFindingIfNeeded(trigger, measured, maximumCadence, "shortest retention window", findings);
        }
    }

    private static void ValidateDownloadStuckDetection(
        ScheduleTrigger trigger,
        JobSpec spec,
        CadenceMeasurement? cadence,
        List<DiagnosticFinding> findings)
    {
        var fetchingMinutes = ReadIntegerParameter(trigger, spec, "fetchingMinutes", ParameterFloor.Positive, findings);
        _ = ReadIntegerParameter(trigger, spec, "stalledMinutes", ParameterFloor.Positive, findings);

        if (cadence is not { } measured ||
            fetchingMinutes is not { } minutes ||
            TryCreateDuration(minutes / 2d, DurationUnit.Minutes, trigger, "fetchingMinutes", findings)
                is not { } maximumCadence)
        {
            return;
        }

        AddLessOftenFindingIfNeeded(trigger, measured, maximumCadence, "half of fetchingMinutes", findings);
    }

    private static void ValidateDownloadImportScan(
        ScheduleTrigger trigger,
        JobSpec spec,
        CadenceMeasurement? cadence,
        List<DiagnosticFinding> findings)
    {
        var settleMinutes = ReadIntegerParameter(trigger, spec, "settleMinutes", ParameterFloor.Positive, findings);

        if (cadence is not { } measured ||
            settleMinutes is not { } minutes ||
            TryCreateDuration(minutes * 6d, DurationUnit.Minutes, trigger, "settleMinutes", findings)
                is not { } maximumCadence)
        {
            return;
        }

        AddLessOftenFindingIfNeeded(trigger, measured, maximumCadence, "six times settleMinutes", findings);
    }

    private static void ValidateDatabaseMaintenance(
        ScheduleTrigger trigger,
        JobSpec spec,
        List<DiagnosticFinding> findings)
    {
        var backupEnabled = ReadBooleanParameter(trigger, spec, "backupEnabled", findings);
        var optimizeEnabled = ReadBooleanParameter(trigger, spec, "optimizeEnabled", findings);

        if (backupEnabled == true)
        {
            _ = ReadIntegerParameter(trigger, spec, "backupIntervalDays", ParameterFloor.Positive, findings);
            _ = ReadIntegerParameter(trigger, spec, "backupRetentionCount", ParameterFloor.Positive, findings);
        }

        if (optimizeEnabled == true)
        {
            _ = ReadIntegerParameter(trigger, spec, "optimizeIntervalDays", ParameterFloor.Positive, findings);
        }
    }

    private static void ValidateIntegrationHealthCheck(
        ScheduleTrigger trigger,
        JobSpec spec,
        CadenceMeasurement? cadence,
        List<DiagnosticFinding> findings)
    {
        var staleAfterMinutes = ReadIntegerParameter(
            trigger,
            spec,
            MagpieJobs.Parameters.StaleAfterMinutes,
            ParameterFloor.Positive,
            findings);
        var timeoutSeconds = ReadIntegerParameter(
            trigger,
            spec,
            MagpieJobs.Parameters.TimeoutSeconds,
            ParameterFloor.Positive,
            findings);

        if (staleAfterMinutes is { } staleMinutes && timeoutSeconds is { } seconds && seconds > staleMinutes * 60)
        {
            findings.Add(Warning(
                "IntegrationHealthCheck timeout exceeds stale window",
                "The integration health check timeout is longer than the time before a connection " +
                "is considered stale. The timeout should be reduced, or the stale window increased.",
                JobDiagnosticAffectedEntityPrefixes.ScheduleTrigger + trigger.Id));
        }

        if (cadence is not { } measured)
        {
            return;
        }

        AddMoreOftenFindingIfNeeded(trigger, measured, TimeSpan.FromMinutes(10), findings);
        if (staleAfterMinutes is not { } minutes ||
            TryCreateDuration(
                minutes,
                DurationUnit.Minutes,
                trigger,
                MagpieJobs.Parameters.StaleAfterMinutes,
                findings) is not { } maximumCadence)
        {
            return;
        }

        AddLessOftenFindingIfNeeded(
            trigger,
            measured,
            maximumCadence,
            MagpieJobs.Parameters.StaleAfterMinutes,
            findings);
    }

    private static void ValidateMediaInfoExtractScan(
        ScheduleTrigger trigger,
        JobSpec spec,
        CadenceMeasurement? cadence,
        List<DiagnosticFinding> findings)
    {
        var maxAgeHours = ReadIntegerParameter(trigger, spec, "maxAgeHours", ParameterFloor.Positive, findings);

        if (cadence is not { } measured ||
            maxAgeHours is not { } hours ||
            TryCreateDuration(hours, DurationUnit.Hours, trigger, "maxAgeHours", findings)
                is not { } maximumCadence)
        {
            return;
        }

        AddLessOftenFindingIfNeeded(trigger, measured, maximumCadence, "maxAgeHours", findings);
    }

    private static bool? ReadBooleanParameter(
        ScheduleTrigger trigger,
        JobSpec spec,
        string parameterName,
        List<DiagnosticFinding> findings)
    {
        var parameter = spec.Parameters
            .FirstOrDefault(p => string.Equals(p.Name, parameterName, StringComparison.Ordinal));
        if (parameter is null)
        {
            findings.Add(Error(
                $"Job spec for {spec.JobType} is missing timing parameter {parameterName}",
                $"Doctor schedule checks expect {spec.JobType}.{parameterName} so cadence can be validated.",
                JobDiagnosticAffectedEntityPrefixes.Job + spec.JobType));
            return null;
        }

        if (parameter.Type != DataType.Boolean)
        {
            findings.Add(Error(
                $"Job spec for {spec.JobType} has invalid timing parameter {parameterName}",
                $"{spec.JobType}.{parameterName} must be declared as a boolean parameter.",
                JobDiagnosticAffectedEntityPrefixes.Job + spec.JobType));
            return null;
        }

        var hasConfiguredValue = TryGetParameterValue(trigger.Parameters, parameterName, out var configuredValue);
        var raw = hasConfiguredValue ? configuredValue : parameter.DefaultValue;
        if (string.IsNullOrWhiteSpace(raw))
        {
            findings.Add(Error(
                $"Schedule {trigger.Name} is missing timing parameter {parameterName}",
                $"The schedule does not set {trigger.JobType}.{parameterName}, and the job spec does not define a " +
                "default.",
                JobDiagnosticAffectedEntityPrefixes.ScheduleTrigger + trigger.Id));
            return null;
        }

        if (bool.TryParse(raw, out var value))
        {
            return value;
        }

        var source = hasConfiguredValue ? "configured" : "default";
        findings.Add(Error(
            $"Schedule {trigger.Name} has invalid timing parameter {parameterName}",
            $"The {source} value {raw} for {trigger.JobType}.{parameterName} must be true or false.",
            JobDiagnosticAffectedEntityPrefixes.ScheduleTrigger + trigger.Id));
        return null;
    }

    private static int? ReadIntegerParameter(
        ScheduleTrigger trigger,
        JobSpec spec,
        string parameterName,
        ParameterFloor floor,
        List<DiagnosticFinding> findings)
    {
        var parameter = spec.Parameters
            .FirstOrDefault(p => string.Equals(p.Name, parameterName, StringComparison.Ordinal));
        if (parameter is null)
        {
            findings.Add(Error(
                $"Job spec for {spec.JobType} is missing timing parameter {parameterName}",
                $"Doctor schedule checks expect {spec.JobType}.{parameterName} so cadence can be validated.",
                JobDiagnosticAffectedEntityPrefixes.Job + spec.JobType));
            return null;
        }

        if (parameter.Type != DataType.Number)
        {
            findings.Add(Error(
                $"Job spec for {spec.JobType} has invalid timing parameter {parameterName}",
                $"{spec.JobType}.{parameterName} must be declared as a numeric parameter.",
                JobDiagnosticAffectedEntityPrefixes.Job + spec.JobType));
            return null;
        }

        var hasConfiguredValue = TryGetParameterValue(trigger.Parameters, parameterName, out var configuredValue);
        var raw = hasConfiguredValue ? configuredValue : parameter.DefaultValue;
        if (string.IsNullOrWhiteSpace(raw))
        {
            findings.Add(Error(
                $"Schedule {trigger.Name} is missing timing parameter {parameterName}",
                $"The schedule does not set {trigger.JobType}.{parameterName}, and the job spec does not define a " +
                "default.",
                JobDiagnosticAffectedEntityPrefixes.ScheduleTrigger + trigger.Id));
            return null;
        }

        if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ||
            !IsAllowed(value, floor))
        {
            var source = hasConfiguredValue ? "configured" : "default";
            findings.Add(Error(
                $"Schedule {trigger.Name} has invalid timing parameter {parameterName}",
                $"The {source} value {raw} for {trigger.JobType}.{parameterName} must be " + $"{DescribeFloor(floor)}.",
                JobDiagnosticAffectedEntityPrefixes.ScheduleTrigger + trigger.Id));
            return null;
        }

        return value;
    }

    private static void AddMoreOftenFindingIfNeeded(
        ScheduleTrigger trigger,
        CadenceMeasurement cadence,
        TimeSpan minimumInterval,
        List<DiagnosticFinding> findings)
    {
        if (cadence.MinimumInterval >= minimumInterval)
        {
            return;
        }

        findings.Add(Warning(
            $"{trigger.JobType} schedule runs more often than {FormatDuration(minimumInterval)}",
            $"Schedule {trigger.Name} uses {trigger.Schedule}, which can run every " +
            $"{FormatDuration(cadence.MinimumInterval)}. The supported minimum interval is " +
            $"{FormatDuration(minimumInterval)}.",
            JobDiagnosticAffectedEntityPrefixes.ScheduleTrigger + trigger.Id));
    }

    private static void AddLessOftenFindingIfNeeded(
        ScheduleTrigger trigger,
        CadenceMeasurement cadence,
        TimeSpan maximumInterval,
        string ruleName,
        List<DiagnosticFinding> findings)
    {
        if (cadence.MaximumInterval <= maximumInterval)
        {
            return;
        }

        findings.Add(Warning(
            $"{trigger.JobType} schedule runs less often than {ruleName}",
            $"Schedule {trigger.Name} uses {trigger.Schedule}, which can wait " +
            $"{FormatDuration(cadence.MaximumInterval)} between runs. The supported maximum interval is " +
            $"{FormatDuration(maximumInterval)}.",
            JobDiagnosticAffectedEntityPrefixes.ScheduleTrigger + trigger.Id));
    }

    private static CadenceMeasurement? MeasureCadence(string schedule)
    {
        if (!CronSchedule.TryParse(schedule, out var cronSchedule, out _))
        {
            return null;
        }

        var first = cronSchedule.NextOccurrence(CadenceSampleStart);
        if (first is null)
        {
            return null;
        }

        var previous = first.Value;
        var sampledUntil = CadenceSampleStart + CadenceSampleWindow;
        TimeSpan? minimumInterval = null;
        TimeSpan? maximumInterval = null;
        var intervals = 0;

        while (intervals < MaximumCadenceIntervals)
        {
            var next = cronSchedule.NextOccurrence(previous.AddSeconds(1));
            if (next is null)
            {
                break;
            }

            var interval = next.Value - previous;
            if (interval <= TimeSpan.Zero)
            {
                break;
            }

            minimumInterval = minimumInterval is null || interval < minimumInterval.Value
                ? interval
                : minimumInterval.Value;
            maximumInterval = maximumInterval is null || interval > maximumInterval.Value
                ? interval
                : maximumInterval.Value;

            previous = next.Value;
            intervals++;

            if (previous >= sampledUntil)
            {
                break;
            }
        }

        return minimumInterval is { } min && maximumInterval is { } max
            ? new CadenceMeasurement(min, max)
            : null;
    }

    private static TimeSpan? TryCreateDuration(
        double value,
        DurationUnit unit,
        ScheduleTrigger trigger,
        string parameterName,
        List<DiagnosticFinding> findings)
    {
        var maximum = unit switch
        {
            DurationUnit.Days => TimeSpan.MaxValue.TotalDays,
            DurationUnit.Hours => TimeSpan.MaxValue.TotalHours,
            DurationUnit.Minutes => TimeSpan.MaxValue.TotalMinutes,
            _ => throw new ArgumentOutOfRangeException(nameof(unit), unit, null)
        };

        if (value > maximum)
        {
            findings.Add(Error(
                $"Schedule {trigger.Name} has invalid timing parameter {parameterName}",
                $"{trigger.JobType}.{parameterName} is too large to evaluate as a schedule duration.",
                JobDiagnosticAffectedEntityPrefixes.ScheduleTrigger + trigger.Id));
            return null;
        }

        return unit switch
        {
            DurationUnit.Days => TimeSpan.FromDays(value),
            DurationUnit.Hours => TimeSpan.FromHours(value),
            DurationUnit.Minutes => TimeSpan.FromMinutes(value),
            _ => throw new ArgumentOutOfRangeException(nameof(unit), unit, null)
        };
    }

    private static DiagnosticFinding MissingScheduleFinding(ScheduleTrigger entry)
    {
        return Warning(
            $"Schedule for {entry.Name} is missing",
            $"The built-in {entry.JobType} schedule is missing. Fix restores the default " +
            $"{entry.Schedule} schedule, or it can remain removed if that was intentional.",
            JobDiagnosticAffectedEntityPrefixes.Job + entry.JobType,
            MissingDefaultJobRepair.Id);
    }

    private static DiagnosticFinding DisabledScheduleFinding(ScheduleTrigger trigger)
    {
        return Warning(
            $"Schedule {trigger.Name} is disabled",
            $"The built-in {trigger.JobType} schedule exists but is disabled.",
            JobDiagnosticAffectedEntityPrefixes.ScheduleTrigger + trigger.Id);
    }

    private static DiagnosticFinding MissingEventTriggerFinding(EventTrigger entry)
    {
        return Warning(
            $"Event trigger for {entry.Name} is missing",
            $"The built-in {entry.JobType} event trigger is missing. Fix restores it, " +
            "or it can remain removed if that was intentional.",
            JobDiagnosticAffectedEntityPrefixes.EventTriggerJob + entry.JobType,
            MissingDefaultJobRepair.Id);
    }

    private static DiagnosticFinding DisabledEventTriggerFinding(EventTrigger trigger)
    {
        return Warning(
            $"Event trigger {trigger.Name} is disabled",
            $"The built-in {trigger.JobType} event trigger exists but is disabled.",
            JobDiagnosticAffectedEntityPrefixes.EventTrigger + trigger.Id);
    }

    private static DiagnosticFinding InvalidCronFinding(ScheduleTrigger trigger, string? cronError)
    {
        return Error(
            $"Schedule {trigger.Name} has invalid cron",
            $"Schedule {trigger.Schedule} is not valid: {cronError ?? "Unknown cron validation error."}",
            JobDiagnosticAffectedEntityPrefixes.ScheduleTrigger + trigger.Id);
    }

    private static DiagnosticFinding MissingJobSpecFinding(ScheduleTrigger trigger)
    {
        return Error(
            $"Schedule {trigger.Name} has no registered job spec",
            $"The {trigger.JobType} schedule cannot be checked because its job definition is missing.",
            JobDiagnosticAffectedEntityPrefixes.Job + trigger.JobType);
    }

    private static DiagnosticFinding Warning(
        string title,
        string detail,
        string affectedEntity,
        string? repairId = null)
    {
        return new DiagnosticFinding
        {
            DiagnosticCheckId = Id,
            Severity = DiagnosticFindingSeverity.Warning,
            Title = title,
            Detail = detail,
            AffectedEntity = affectedEntity,
            RepairId = repairId,
        };
    }

    private static DiagnosticFinding Error(string title, string detail, string affectedEntity)
    {
        return new DiagnosticFinding
        {
            DiagnosticCheckId = Id,
            Severity = DiagnosticFindingSeverity.Error,
            Title = title,
            Detail = detail,
            AffectedEntity = affectedEntity,
        };
    }

    private static bool TryGetParameterValue(
        IReadOnlyDictionary<string, string> parameters,
        string parameterName,
        out string value)
    {
        foreach (var (key, candidate) in parameters)
        {
            if (string.Equals(key, parameterName, StringComparison.OrdinalIgnoreCase))
            {
                value = candidate;
                return true;
            }
        }

        value = string.Empty;
        return false;
    }

    private static bool IsAllowed(int value, ParameterFloor floor)
    {
        return floor switch
        {
            ParameterFloor.NonNegative => value >= 0,
            ParameterFloor.Positive => value > 0,
            _ => throw new ArgumentOutOfRangeException(nameof(floor), floor, null)
        };
    }

    private static string DescribeFloor(ParameterFloor floor)
    {
        return floor switch
        {
            ParameterFloor.NonNegative => "a non-negative integer",
            ParameterFloor.Positive => "a positive integer",
            _ => throw new ArgumentOutOfRangeException(nameof(floor), floor, null)
        };
    }

    private static string FormatDuration(TimeSpan value)
    {
        return value.ToString("c", CultureInfo.InvariantCulture);
    }

    public async Task<IReadOnlyList<DiagnosticFinding>> RunAsync(
        DiagnosticContext context,
        CancellationToken cancellationToken = default)
    {
        var triggersByJobType = (await scheduleTriggers.ListAllAsync(cancellationToken).ConfigureAwait(false))
            .GroupBy(trigger => trigger.JobType, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.OrderBy(trigger => trigger.CreatedAt).ThenBy(trigger => trigger.Id).ToList(),
                StringComparer.Ordinal);

        var findings = new List<DiagnosticFinding>();
        foreach (var entry in DefaultJobTriggerCatalog.ScheduleTriggers)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!triggersByJobType.TryGetValue(entry.JobType, out var triggers))
            {
                findings.Add(MissingScheduleFinding(entry));
                continue;
            }

            if (!triggers.Any(trigger => trigger.Enabled))
            {
                findings.Add(DisabledScheduleFinding(triggers[0]));
            }

            foreach (var trigger in triggers)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var hasValidCron = CronSchedule.TryParse(trigger.Schedule, out _, out var cronError);
                if (!hasValidCron)
                {
                    findings.Add(InvalidCronFinding(trigger, cronError));
                }

                ValidateTimingShape(entry, trigger, hasValidCron, findings);
            }
        }

        var eventTriggersByJobType = (await eventTriggers.ListAllAsync(cancellationToken).ConfigureAwait(false))
            .GroupBy(trigger => trigger.JobType, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.OrderBy(trigger => trigger.CreatedAt).ThenBy(trigger => trigger.Id).ToList(),
                StringComparer.Ordinal);

        foreach (var entry in DefaultJobTriggerCatalog.EventTriggers)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!eventTriggersByJobType.TryGetValue(entry.JobType, out var triggers))
            {
                findings.Add(MissingEventTriggerFinding(entry));
                continue;
            }

            if (!triggers.Any(trigger => trigger.Enabled))
            {
                findings.Add(DisabledEventTriggerFinding(triggers[0]));
            }
        }

        return findings;
    }

    private void ValidateTimingShape(
        ScheduleTrigger entry,
        ScheduleTrigger trigger,
        bool hasValidCron,
        List<DiagnosticFinding> findings)
    {
        if (entry.JobType is not (ReleaseAutoGrabScanJobType
            or ReleaseSearchScanJobType
            or DownloadImportScanJobType
            or DatabaseMaintenanceJobType
            or IntegrationHealthCheckJobType
            or MediaInfoExtractScanJobType
            or RetentionCleanupJobType
            or DownloadStuckDetectionJobType))
        {
            return;
        }

        if (!jobRegistry.TryGetJobSpec(entry.JobType, out var spec))
        {
            findings.Add(MissingJobSpecFinding(trigger));
            return;
        }

        var cadence = hasValidCron ? MeasureCadence(trigger.Schedule) : null;

        switch (entry.JobType)
        {
            case ReleaseSearchScanJobType:
                ValidateReleaseSearchScan(trigger, cadence, MinimumSearchAge, findings);
                break;
            case ReleaseAutoGrabScanJobType:
                ValidateReleaseAutoGrabScan(trigger, spec, cadence, findings);
                break;
            case RetentionCleanupJobType:
                ValidateRetentionCleanup(trigger, spec, cadence, findings);
                break;
            case DownloadStuckDetectionJobType:
                ValidateDownloadStuckDetection(trigger, spec, cadence, findings);
                break;
            case DownloadImportScanJobType:
                ValidateDownloadImportScan(trigger, spec, cadence, findings);
                break;
            case DatabaseMaintenanceJobType:
                ValidateDatabaseMaintenance(trigger, spec, findings);
                break;
            case IntegrationHealthCheckJobType:
                ValidateIntegrationHealthCheck(trigger, spec, cadence, findings);
                break;
            case MediaInfoExtractScanJobType:
                ValidateMediaInfoExtractScan(trigger, spec, cadence, findings);
                break;
        }
    }

    private readonly record struct CadenceMeasurement(TimeSpan MinimumInterval, TimeSpan MaximumInterval);
}
