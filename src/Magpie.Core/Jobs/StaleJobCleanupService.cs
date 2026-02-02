using System;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Configuration;
using Magpie.Abstractions.Repositories;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Magpie.Core.Jobs;

internal sealed class StaleJobCleanupService(
    IJobExecutionRepository jobExecutionRepository,
    ISystemSettingsProvider systemSettingsProvider,
    ILogger<StaleJobCleanupService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);
    private static readonly TimeSpan CutoffAge = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var jobManagementSettings = await systemSettingsProvider.GetSettingsGroupAsync("job-management")
            .ConfigureAwait(false);

        logger.LogDebug("Starting stale job cleanup service.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                logger.LogDebug("Cleaning up stale job executions.");

                var staleJobs = await jobExecutionRepository.QueryAsync(
                    query: new(LastHeartbeat: new(Min: null, Max: DateTime.UtcNow - CutoffAge)),
                    paging: null,
                    sort: null,
                    cancellationToken: stoppingToken).ConfigureAwait(false);

                foreach (var job in staleJobs.Items)
                {
                    try
                    {
                        logger.LogDebug(
                            "Deleting stale job execution {JobExecutionId} with last heartbeat at {LastHeartbeat}.",
                            job.Id,
                            job.LastHeartbeat);

                        await jobExecutionRepository.DeleteAsync(job.Id, null, stoppingToken).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(
                            ex,
                            "Error deleting stale job execution {JobExecutionId}.",
                            job.Id);
                    }
                }

                await jobManagementSettings.ReloadAsync(stoppingToken).ConfigureAwait(false);

                var cleanupInterval = jobManagementSettings.Settings.TryGetValue(
                    "stale-job-cleanup-interval",
                    out var staleJobCleanupIntervalValue) && TimeSpan.TryParse(
                        staleJobCleanupIntervalValue,
                        out var staleCleanupInterval) ? staleCleanupInterval : Interval;

                await Task.Delay(cleanupInterval, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error occurred during stale job execution cleanup.");
            }
        }
    }
}
