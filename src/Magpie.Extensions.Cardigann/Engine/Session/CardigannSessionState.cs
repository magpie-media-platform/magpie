using System.Collections.Concurrent;
using System.Threading;

namespace Magpie.Extensions.Cardigann.Engine.Session;

internal sealed class CardigannSessionState
{
    public ConcurrentDictionary<string, TrackerSession> Sessions { get; } = new();

    public ConcurrentDictionary<string, SemaphoreSlim> LoginGates { get; } = new();
}
