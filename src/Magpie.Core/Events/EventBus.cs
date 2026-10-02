using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Jobs;
using Magpie.Core.Observability;
using Magpie.Models;
using Magpie.Sdk.Services;

namespace Magpie.Core.Events;

internal sealed class EventBus(
    MagpieHostEnvironment hostEnvironment,
    IJobExecutionScopeFactory jobExecutionScopes,
    TimeProvider timeProvider) : IEventBus, IDisposable
{
    private readonly Subject<MagpieEvent> _events = new();
    private readonly Lock _gate = new();

    public IObservable<MagpieEvent> Events => _events.AsObservable();

    public IEventListener CreateListener(string? eventName, bool localOnly, string callSite)
    {
        return new EventListener(Events, eventName, localOnly ? hostEnvironment.NodeId : null, callSite);
    }

    public Task PublishAsync(MagpieEvent magpieEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(magpieEvent);
        cancellationToken.ThrowIfCancellationRequested();

        var isRemote = !string.IsNullOrWhiteSpace(magpieEvent.NodeId) &&
            !string.Equals(magpieEvent.NodeId, hostEnvironment.NodeId, StringComparison.Ordinal);
        if (isRemote)
        {
            Emit(magpieEvent);
            return Task.CompletedTask;
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var published = magpieEvent with
        {
            Id = magpieEvent.Id == Guid.Empty ? Guid.NewGuid() : magpieEvent.Id,
            OccurredAt = magpieEvent.OccurredAt == default ? now : magpieEvent.OccurredAt,
            PublishedAt = magpieEvent.PublishedAt == default ? now : magpieEvent.PublishedAt,
            NodeId = hostEnvironment.NodeId,
            JobExecutionId = magpieEvent.JobExecutionId ?? jobExecutionScopes.CurrentExecutionId,
            CorrelationId = magpieEvent.CorrelationId ?? jobExecutionScopes.CurrentCorrelationId,
            TraceContext = magpieEvent.TraceContext ?? CaptureTraceContext()
        };

        Emit(published);
        return Task.CompletedTask;
    }

    private static IReadOnlyDictionary<string, string>? CaptureTraceContext()
    {
        if (Activity.Current is not { } activity)
        {
            return null;
        }

        var carrier = new Dictionary<string, string>(StringComparer.Ordinal);
        DistributedContextPropagator.Current.Inject(
            activity,
            carrier,
            static (target, key, value) => ((IDictionary<string, string>)target!)[key] = value);

        return carrier.Count == 0 ? null : carrier;
    }

    private static string GetNamePrefix(string name)
    {
        var separator = name.IndexOf('.');
        return separator < 0 ? name : name[..separator];
    }

    private void Emit(MagpieEvent magpieEvent)
    {
        lock (_gate)
        {
            _events.OnNext(magpieEvent);
        }

        EventsTelemetry.PublishedTotal.Add(
            1,
            new KeyValuePair<string, object?>("name_prefix", GetNamePrefix(magpieEvent.Name)));
    }

    public void Dispose()
    {
        _events.Dispose();
    }
}
