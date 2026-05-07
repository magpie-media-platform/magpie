using System;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Jobs;

using Microsoft.Extensions.Logging;

namespace Magpie.Core.Jobs;

/// <summary>
/// Placeholder <see cref="IJobRuntime"/> that performs no work. Allows endpoint groups
/// that depend on <see cref="IJobRuntime"/> to resolve so the host can boot before a
/// real runtime is wired in.
/// </summary>
/// <remarks>
/// This is a stub. Job executions submitted through this runtime are not persisted as
/// running, not dispatched to any worker, and never complete on their own. It exists
/// solely so the API surface can come up without crashing while the real runtime is
/// still under development. Replace this registration with a real implementation
/// (in-process, queue-backed, or distributed) before relying on jobs at runtime.
/// </remarks>
internal sealed class NoOpJobRuntime : IJobRuntime
{
    private readonly ILogger<NoOpJobRuntime> _logger;

    public NoOpJobRuntime(ILogger<NoOpJobRuntime> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);

        _logger = logger;
        _logger.LogWarning(
            "JobRuntime stub active: '{RuntimeName}' will not execute or schedule jobs. "
            + "Replace IJobRuntime with a real runtime before relying on job execution.",
            Name);
    }

    /// <inheritdoc />
    public string Name => "noop";

    /// <inheritdoc />
    public Task InitializeAsync(string nodeId, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("NoOpJobRuntime initialized for node '{NodeId}'.", nodeId);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task ExecuteAsync(Guid executionId, CancellationToken cancellationToken = default)
    {
        _logger.LogWarning(
            "NoOpJobRuntime ignoring execution '{ExecutionId}' -- no real runtime is registered.",
            executionId);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<bool> TryCancelAsync(Guid executionId, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("NoOpJobRuntime cancel request for '{ExecutionId}' (no-op).", executionId);
        return Task.FromResult(false);
    }

    /// <inheritdoc />
    public void Dispose()
    {
    }
}
