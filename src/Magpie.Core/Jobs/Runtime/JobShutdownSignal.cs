using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Hosting;

namespace Magpie.Core.Jobs.Runtime;

internal sealed class JobShutdownSignal(IHostApplicationLifetime lifetime)
{
    public CancellationTokenSource Cancellation { get; } =
        CancellationTokenSource.CreateLinkedTokenSource(lifetime.ApplicationStopping);

    public ConcurrentDictionary<Guid, TaskCompletionSource> Outstanding { get; } = new();
}
