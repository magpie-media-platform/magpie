using System;
using System.Threading.Tasks;

using Magpie.Abstractions.Jobs;
using Magpie.Abstractions.Repositories;
using Magpie.Models.Enums;

using Microsoft.Extensions.Logging;

namespace Magpie.Core.Jobs;

/// <summary>
/// Reports job progress by updating the execution record directly.
/// </summary>
public sealed class ProgressReporter(
    Guid executionId,
    IJobExecutionRepository repository,
    ILogger<ProgressReporter> logger) : IProgressReporter
{
    /// <inheritdoc />
    public void Report(double? percentage, string? message)
    {
        _ = UpdateProgressAsync(percentage, message);
    }

    private async Task UpdateProgressAsync(double? percentage, string? message)
    {
        try
        {
            var execution = await repository.GetByIdAsync(executionId)
                .ConfigureAwait(false);

            if (execution is null || execution.Status != JobExecutionStatus.Running)
            {
                return;
            }

            execution.Progress = percentage.HasValue ? percentage.Value * 100 : null;
            execution.StatusMessage = message;
            execution.LastHeartbeat = DateTime.UtcNow;

            await repository.UpdateAsync(execution, null).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Failed to update job execution progress for execution {ExecutionId}", executionId);
        }
    }
}
