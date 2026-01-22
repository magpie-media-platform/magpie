using System;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Models.Events;

namespace Magpie.Abstractions.Events;

/// <summary>
/// Provides event publishing and subscription capabilities.
/// </summary>
public interface IEventProvider
{
    /// <summary>
    /// The observable stream of events.
    /// </summary>
    IObservable<MagpieEvent> Events { get; }

    /// <summary>
    /// Publishes an event.
    /// </summary>
    /// <param name="magpieEvent">The event to publish.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task PublishAsync(MagpieEvent magpieEvent, CancellationToken cancellationToken);
}
