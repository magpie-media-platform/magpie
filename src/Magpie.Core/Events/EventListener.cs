using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

using Magpie.Models;
using Magpie.Sdk.Services;

namespace Magpie.Core.Events;

internal sealed class EventListener : IEventListener
{
    private readonly Channel<MagpieEvent> _channel;
    private readonly IDisposable _subscription;

    public EventListener(IObservable<MagpieEvent> events, string? eventName, string? localNodeId = null)
    {
        _channel = Channel.CreateUnbounded<MagpieEvent>();
        _subscription = events.Subscribe(new FilteredObserver(_channel, eventName, localNodeId));
    }

    public IAsyncEnumerable<MagpieEvent> ReadAllAsync(CancellationToken cancellationToken = default)
    {
        return _channel.Reader.ReadAllAsync(cancellationToken);
    }

    public void Dispose()
    {
        _subscription.Dispose();
        _channel.Writer.TryComplete();
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

    private sealed class FilteredObserver(Channel<MagpieEvent> channel, string? eventName, string? localNodeId)
        : IObserver<MagpieEvent>
    {
        public void OnNext(MagpieEvent value)
        {
            if (eventName is not null && !string.Equals(value.Name, eventName, StringComparison.Ordinal))
            {
                return;
            }

            if (localNodeId is not null && !string.Equals(value.NodeId, localNodeId, StringComparison.Ordinal))
            {
                return;
            }

            channel.Writer.TryWrite(value);
        }

        public void OnError(Exception error)
        {
            channel.Writer.TryComplete(error);
        }

        public void OnCompleted()
        {
            channel.Writer.TryComplete();
        }
    }
}
