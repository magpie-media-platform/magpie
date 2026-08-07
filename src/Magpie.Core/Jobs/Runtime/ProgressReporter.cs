using System;
using System.Threading.Tasks;

using Magpie.Abstractions.Jobs;
using Magpie.Abstractions.Repositories;
using Magpie.Sdk.Jobs;

using Microsoft.Extensions.Logging;

namespace Magpie.Core.Jobs.Runtime;

internal sealed class ProgressReporter(
    IJobExecutionScopeFactory jobExecutionScopes,
    IJobExecutionRepository executions,
    ILogger<ProgressReporter> logger) : IProgressReporter
{
    public void Report(double? percentage, string? message)
    {
        _ = UpdateProgressAsync(percentage, message);
    }

    private async Task UpdateProgressAsync(double? percentage, string? message)
    {
        var executionId = jobExecutionScopes.CurrentExecutionId;
        if (executionId is null)
        {
            return;
        }

        try
        {
            await executions.UpdateProgressAsync(
                executionId.Value,
                percentage.HasValue ? percentage.Value * 100 : null,
                message).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "The reporter failed to update the progress of execution {ExecutionId}", executionId);
        }
    }
}
