using System;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions;
using Magpie.Models;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Magpie.App.Hosting;

internal sealed class DatabaseStartupBackgroundService(
    DatabaseStartupService startupService,
    DatabaseMigrationState migrationState,
    HostStatusInfo hostStatus,
    IHostApplicationLifetime applicationLifetime,
    ILogger<DatabaseStartupBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield();

        try
        {
            await startupService.RunAsync(stoppingToken).ConfigureAwait(false);

            migrationState.Complete();
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            logger.LogCritical(ex, "The database startup work failed, so the host stops");

            migrationState.Fail();
            hostStatus.ShutdownExitCode = MagpieExitCodes.StartupFailed;
            applicationLifetime.StopApplication();
        }
    }
}
