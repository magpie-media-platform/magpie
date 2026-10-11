using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Repositories;
using Magpie.Models;
using Magpie.Models.Enums;
using Magpie.Sdk.Jobs;
using Magpie.Sdk.Services;

using Microsoft.Extensions.Logging;

namespace Magpie.Core.Jobs;

internal sealed class IntegrationHealthCheckJob(
    IIndexerConnectionRepository indexerConnectionRepository,
    IIndexerService indexerService,
    IDownloadClientService downloadClientService,
    IMediaServerService mediaServerService,
    TimeProvider timeProvider,
    ILogger<IntegrationHealthCheckJob> logger,
    IProgressReporter progressReporter) : IJob
{
    public static readonly JobSpec Spec = new()
    {
        JobType = MagpieJobs.Types.IntegrationHealthCheck,
        DisplayName = "Integration health check",
        Description = "Checks indexers, download clients, and media servers whose health status may be stale.",
        Category = "Integrations",
        Scope = JobScope.Administrative,
        Enabled = true,
        OnDemand = true,
        SupportedTriggerModes = [JobTriggerMode.Manual, JobTriggerMode.Schedule, JobTriggerMode.Event],
        ConcurrencyScope = ConcurrencyScope.PerType,
        Parameters =
        [
            new(
                MagpieJobs.Parameters.StaleAfterMinutes,
                "Minutes before an integration health check is stale.",
                DataType.Number,
                false,
                DefaultStaleAfterMinutes.ToString(CultureInfo.InvariantCulture))
            {
                ValidateScheduleValue = (value, _, utcNow) =>
                    int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var minutes) &&
                    minutes > utcNow.Ticks / TimeSpan.TicksPerMinute
                        ? "exceeds the supported date range." : null
            },
            new(
                MagpieJobs.Parameters.TimeoutSeconds,
                "Seconds allowed for each connection test.",
                DataType.Number,
                false,
                DefaultTimeoutSeconds.ToString(CultureInfo.InvariantCulture))
            {
                ValidateScheduleValue = (value, _, _) =>
                    int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds) &&
                    seconds > MaximumTimeoutSeconds
                        ? "exceeds the supported timer range." : null
            }
        ],
    };

    private const int DefaultStaleAfterMinutes = 60;
    private const int DefaultTimeoutSeconds = 10;

    private const uint MaximumTimeoutSeconds = (uint.MaxValue - 1) / 1000;

    private static bool ShouldProbe(DateTime? lastChecked, DateTime staleCutoff)
    {
        return lastChecked is null || lastChecked <= staleCutoff;
    }

    private static int ReadPositiveIntParameter(
        IReadOnlyDictionary<string, string> parameters,
        string name,
        int defaultValue)
    {
        if (!parameters.TryGetValue(name, out var raw) || string.IsNullOrWhiteSpace(raw))
        {
            return defaultValue;
        }

        return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) && value > 0
            ? value
            : defaultValue;
    }

    public async Task ExecuteAsync(JobContext jobContext, CancellationToken cancellationToken = default)
    {
        var staleAfter = TimeSpan.FromMinutes(ReadPositiveIntParameter(
            jobContext.Parameters,
            MagpieJobs.Parameters.StaleAfterMinutes,
            DefaultStaleAfterMinutes));
        var timeout = TimeSpan.FromSeconds(ReadPositiveIntParameter(
            jobContext.Parameters,
            MagpieJobs.Parameters.TimeoutSeconds,
            DefaultTimeoutSeconds));

        progressReporter.Report(0, "The health check will examine the integrations.");

        var staleCutoff = timeProvider.GetUtcNow().UtcDateTime - staleAfter;
        var checkedCount = 0;
        var failures = 0;
        var skippedFresh = 0;

        foreach (var connection in (await indexerConnectionRepository.ListAllAsync(cancellationToken)
            .ConfigureAwait(false)).Where(connection => connection.Enabled))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!ShouldProbe(connection.LastChecked, staleCutoff))
            {
                skippedFresh++;
                continue;
            }

            var result = await indexerService.TestConnectionAsync(
                connection.IndexerId,
                connection.Id,
                timeout,
                cancellationToken).ConfigureAwait(false);
            checkedCount++;
            failures += result.Success ? 0 : 1;
        }

        foreach (var client in (await downloadClientService.ListAsync(cancellationToken).ConfigureAwait(false))
            .Where(client => client.Enabled))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!ShouldProbe(client.LastChecked, staleCutoff))
            {
                skippedFresh++;
                continue;
            }

            var result = await downloadClientService.TestAsync(client.Id, timeout, cancellationToken)
                .ConfigureAwait(false);
            checkedCount++;
            failures += result.Success ? 0 : 1;
        }

        foreach (var server in (await mediaServerService.ListAsync(cancellationToken).ConfigureAwait(false))
            .Where(server => server.Enabled))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!ShouldProbe(server.LastChecked, staleCutoff))
            {
                skippedFresh++;
                continue;
            }

            var result = await mediaServerService.TestAsync(server.Id, timeout, cancellationToken)
                .ConfigureAwait(false);
            checkedCount++;
            failures += result.Success ? 0 : 1;
        }

        logger.LogInformation(
            "The health check checked {CheckedCount} integration(s), found {FailureCount} failure(s), and " +
            "skipped {SkippedFreshCount} current integration(s)",
            checkedCount,
            failures,
            skippedFresh);

        progressReporter.Report(1, "The integration health check completed.");
    }
}
