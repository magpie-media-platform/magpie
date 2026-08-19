using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

using Magpie.Models;
using Magpie.Sdk.Services;

namespace Magpie.Core.Events;

internal sealed class EventListener : IEventListener
{
    internal const string UnlabelledCallSite = "other";

    private static readonly ConcurrentDictionary<EventListener, string> CallSites = new();

    private readonly Channel<MagpieEvent> _channel;
    private readonly IDisposable _subscription;

    public EventListener(
        IObservable<MagpieEvent> events,
        string? eventName,
        string? localNodeId = null,
        string? callSite = null)
    {
        _channel = Channel.CreateUnbounded<MagpieEvent>();
        _subscription = events.Subscribe(new FilteredObserver(_channel, eventName, localNodeId));
        CallSites[this] = callSite ?? UnlabelledCallSite;
    }

    internal static IEnumerable<Measurement<long>> MeasureChannelDepths()
    {
        var depthByCallSite = new Dictionary<string, long>(StringComparer.Ordinal)
        {
            [UnlabelledCallSite] = 0
        };

        foreach (var (listener, callSite) in CallSites)
        {
            depthByCallSite.TryGetValue(callSite, out var depth);
            depthByCallSite[callSite] = depth + listener._channel.Reader.Count;
        }

        return
        [
            .. depthByCallSite.Select(entry =>
                new Measurement<long>(entry.Value, new KeyValuePair<string, object?>("listener", entry.Key)))
        ];
    }

    public IAsyncEnumerable<MagpieEvent> ReadAllAsync(CancellationToken cancellationToken = default)
    {
        return _channel.Reader.ReadAllAsync(cancellationToken);
    }

    public void Dispose()
    {
        CallSites.TryRemove(this, out _);
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
