using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace Magpie.Core.Jobs.Runtime;

internal sealed record JobShutdownSignal
{
    public CancellationTokenSource Cancellation { get; } = new();

    public ConcurrentDictionary<Guid, TaskCompletionSource> Outstanding { get; } = new();
}
