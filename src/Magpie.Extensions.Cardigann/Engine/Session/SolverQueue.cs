using System;
using System.Threading;
using System.Threading.Tasks;

namespace Magpie.Extensions.Cardigann.Engine.Session;

internal sealed class SolverQueue
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public TimeSpan Wait { get; init; } = TimeSpan.FromSeconds(20);

    public Task<bool> EnterAsync(CancellationToken cancellationToken) => _gate.WaitAsync(Wait, cancellationToken);

    public void Leave() => _gate.Release();
}
