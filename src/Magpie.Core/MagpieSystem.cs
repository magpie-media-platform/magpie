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

namespace Magpie.Core;

internal sealed class MagpieSystem(
    HostStatusInfo hostStatus,
    MagpieHostEnvironment hostEnvironment,
    IHostApplicationLifetime lifetime,
    IAuditRecordRepository auditRecordRepository,
    TimeProvider timeProvider) : IMagpieSystem
{
    private readonly StateManager<LifecycleState> _lifecycle = new(LifecycleState.Running);

    private enum LifecycleState
    {
        Running,
        ShuttingDown,
    }

    public string NodeId => hostEnvironment.NodeId;

    public bool IsClustered => hostEnvironment.IsClustered;

    public SystemInfo GetInfo()
    {
        return new SystemInfo
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

        if (!_lifecycle.TryTransition(LifecycleState.ShuttingDown, LifecycleState.Running))
        {
            throw new MagpieShutdownException(
                "A shutdown is already in progress.",
                hostStatus.ShutdownRequestedByUserId,
                hostStatus.ShutdownRequestedAtUtc);
        }

        try
        {
            lifetime.StopApplication();
            hostStatus.IsShutdownRequested = true;
            hostStatus.ShutdownRequestedByUserId = actingUserId;
            hostStatus.ShutdownRequestedAtUtc = timeProvider.GetUtcNow().UtcDateTime;
            if (hostStatus.Mode == HostMode.Sentinel)
            {
                hostStatus.ShutdownExitCode = exitCode;
            }
        }
        catch
        {
            _lifecycle.TryTransition(LifecycleState.Running, LifecycleState.ShuttingDown);
            throw;
        }

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
}
