using System;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions;
using Magpie.Abstractions.Repositories;
using Magpie.Models;
using Magpie.Sdk.Services;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Magpie.Core;

internal sealed class MagpieSystem(
    HostStatusInfo hostStatus,
    MagpieHostEnvironment hostEnvironment,
    IHostApplicationLifetime lifetime,
    IAuditRecordRepository auditRecordRepository,
    TimeProvider timeProvider,
    ILogger<MagpieSystem> logger) : IMagpieSystem
{
    public string NodeId => hostEnvironment.NodeId;

    public bool IsClustered => hostEnvironment.IsClustered;

    public SystemInfo GetInfo()
    {
        return new()
        {
            Version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0",
            Runtime = RuntimeInformation.FrameworkDescription,
            OperatingSystem = RuntimeInformation.OSDescription,
            Architecture = RuntimeInformation.ProcessArchitecture.ToString(),
            SystemRoot = hostEnvironment.SystemRoot,
            NodeId = hostEnvironment.NodeId,
            StartedAt = hostEnvironment.StartedAt,
            RestartSupported = hostEnvironment.IsSupervised
        };
    }

    public Task<SystemHealth> CheckHealthAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new SystemHealth { Status = "Healthy" });
    }

    public Task RequestShutdownAsync(Guid? actingUserId, CancellationToken cancellationToken = default)
    {
        return StopAsync(MagpieExitCodes.Shutdown, "Shutdown", actingUserId, cancellationToken);
    }

    public Task RequestRestartAsync(Guid? actingUserId, CancellationToken cancellationToken = default)
    {
        return StopAsync(MagpieExitCodes.Restart, "Restart", actingUserId, cancellationToken);
    }

    private async Task StopAsync(
        int exitCode,
        string operation,
        Guid? actingUserId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!hostEnvironment.IsSupervised)
        {
            throw new MagpieShutdownException("The host is not supervised.");
        }

        lock (hostStatus)
        {
            if (hostStatus.IsShutdownRequested)
            {
                throw new MagpieShutdownException("A shutdown is already in progress.");
            }

            hostStatus.IsShutdownRequested = true;
        }

        try
        {
            await auditRecordRepository.CreateAsync(
                new()
                {
                    Category = AuditCategories.System,
                    Operation = operation,
                    EntityType = "Host",
                    Detail = $"Host {operation.ToLowerInvariant()} requested.",
                    Timestamp = timeProvider.GetUtcNow().UtcDateTime,
                    UserId = actingUserId
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "The {Operation} audit record could not be written", operation);
        }

        try
        {
            if (hostStatus.Mode is HostMode.Sentinel or HostMode.WindowsService or HostMode.Systemd)
            {
                hostStatus.ShutdownExitCode = exitCode;
            }

            lifetime.StopApplication();
        }
        catch
        {
            lock (hostStatus)
            {
                hostStatus.IsShutdownRequested = false;
            }

            throw;
        }
    }
}
