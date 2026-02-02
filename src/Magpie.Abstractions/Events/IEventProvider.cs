using System;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Models;

namespace Magpie.Abstractions.Events;

/// <summary>
/// Provides event publishing and subscription capabilities.
/// </summary>
public interface IEventProvider
{
    /// <summary>
    /// An observable stream of events.
    /// </summary>
    IObservable<MagpieEvent> Events { get; }

    /// <summary>
    /// Publish an event.
    /// </summary>
    /// <param name="magpieEvent">The event to publish.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task PublishAsync(MagpieEvent magpieEvent, CancellationToken cancellationToken = default);
}
