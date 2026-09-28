using System;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Jobs;
using Magpie.Abstractions.Repositories;
using Magpie.Core.Config;
using Magpie.Sdk.Jobs;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Magpie.Core.Jobs.Runtime;

internal sealed class ProgressReporter(
    IJobExecutionScopeFactory jobExecutionScopes,
    IServiceScopeFactory scopeFactory,
    ILogger<ProgressReporter> logger,
    JobRuntimeOptions options) : IProgressReporter, IAsyncDisposable
{
    private readonly Lock _gate = new();

    private readonly TimeSpan _flushTimeout = options.ProgressFlushTimeout;

    private Task _writes = Task.CompletedTask;

    private (Guid ExecutionId, double? Percentage, string? Message)? _pending;

    private bool _draining;

    public void Report(double? percentage, string? message)
    {
        if (jobExecutionScopes.CurrentExecutionId is not { } executionId)
        {
            return;
        }

        lock (_gate)
        {
            _pending = (executionId, percentage, message);
            if (_draining)
            {
                return;
            }

            _draining = true;
            _writes = DrainAsync();
        }
    }

    /// <inheritdoc />
    public Task FlushAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            return _writes.WaitAsync(cancellationToken);
        }
    }

    public async ValueTask DisposeAsync()
    {
        Task writes;
        lock (_gate)
        {
            writes = _writes;
        }

        try
        {
            await writes.WaitAsync(_flushTimeout).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            logger.LogWarning(
                "The reporter stopped waiting for the progress writes after {FlushTimeout}",
                _flushTimeout);
        }
    }

    private async Task DrainAsync()
    {
        while (true)
        {
            (Guid ExecutionId, double? Percentage, string? Message) next;
            lock (_gate)
            {
                if (_pending is not { } pending)
                {
                    _draining = false;
                    return;
                }

                next = pending;
                _pending = null;
            }

            await WriteAsync(next.ExecutionId, next.Percentage, next.Message).ConfigureAwait(false);
        }
    }

    private async Task WriteAsync(Guid executionId, double? percentage, string? message)
    {
        try
        {
            var scope = scopeFactory.CreateAsyncScope();
            await using var scopeHandle = scope.ConfigureAwait(false);

            var applied = await scope.ServiceProvider.GetRequiredService<IJobExecutionRepository>()
                .UpdateProgressAsync(
                    executionId,
                    percentage.HasValue ? percentage.Value * 100 : null,
                    message)
                .ConfigureAwait(false);
            if (!applied)
            {
                logger.LogWarning(
                    "The reporter dropped a progress report because execution {ExecutionId} is not running",
                    executionId);
            }
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "The reporter failed to update the progress of execution {ExecutionId}", executionId);
        }
    }
}
