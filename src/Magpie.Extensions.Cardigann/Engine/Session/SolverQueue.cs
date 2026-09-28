using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Magpie.Extensions.Cardigann.Engine.Session;

internal sealed class SolverQueue
{
    private readonly object _sync = new();
    private readonly LinkedList<TaskCompletionSource<bool>> _interactive = new();
    private readonly LinkedList<TaskCompletionSource<bool>> _background = new();
    private bool _occupied;

    public TimeSpan Wait { get; init; } = TimeSpan.FromSeconds(20);

    public bool WaitForBackground { get; init; }

    public async Task<bool> EnterAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var kind = SolverRequestScope.Current;
        LinkedListNode<TaskCompletionSource<bool>> node;
        lock (_sync)
        {
            if (!_occupied)
            {
                _occupied = true;
                return true;
            }

            var waiting = kind == SolverRequestKind.Interactive ? _interactive : _background;
            node = waiting.AddLast(new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously));
        }

        var timeout = kind == SolverRequestKind.Interactive ||
            (WaitForBackground && kind == SolverRequestKind.Background) ? Timeout.InfiniteTimeSpan : Wait;
        try
        {
            return await node.Value.Task.WaitAsync(timeout, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is TimeoutException or OperationCanceledException)
        {
            lock (_sync)
            {
                if (node.List is { } waiting)
                {
                    waiting.Remove(node);
                }
                else
                {
                    ReleaseNext();
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            return false;
        }
    }

    public void Leave()
    {
        lock (_sync)
        {
            ReleaseNext();
        }
    }

    private void ReleaseNext()
    {
        var waiting = _interactive.Count > 0 ? _interactive : _background;
        if (waiting.First is { } next)
        {
            waiting.RemoveFirst();
            next.Value.SetResult(true);
        }
        else
        {
            _occupied = false;
        }
    }
}
