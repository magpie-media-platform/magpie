using System;
using System.Threading;
using System.Threading.Tasks;

namespace Magpie.Core;

internal static class TaskHelpers
{
    private static readonly TimeSpan MaxDelay = TimeSpan.FromMilliseconds(uint.MaxValue - 1);

    public static async Task SleepAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(Clamp(delay), cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    public static async Task SleepAsync(TimeSpan delay, TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(Clamp(delay), timeProvider, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private static TimeSpan Clamp(TimeSpan delay)
    {
        return delay > MaxDelay ? MaxDelay : delay;
    }
}
