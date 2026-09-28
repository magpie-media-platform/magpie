using System.Collections.Concurrent;

namespace Magpie.Extensions.Cardigann.Engine.Session;

internal sealed class CardigannSessionState
{
    public ConcurrentDictionary<string, TrackerSession> Sessions { get; } = new();

    public ConcurrentDictionary<string, SolverQueue> LoginGates { get; } = new();
}
