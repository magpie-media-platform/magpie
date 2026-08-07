using System.Threading;
using System.Threading.Tasks;

using Magpie.Models;

namespace Magpie.Sdk.Services;

/// <summary>
/// The bus that carries Magpie domain events.
/// </summary>
public interface IEventBus
{
    /// <summary>
    /// Publishes an event to the bus.
    /// </summary>
    /// <param name="magpieEvent">The event to publish</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task PublishAsync(MagpieEvent magpieEvent, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a listener over the event stream.
    /// </summary>
    /// <param name="eventName">The name of the only event to receive, or <see langword="null"/> for all</param>
    /// <param name="localOnly">Whether the listener receives only the events raised on this node</param>
    IEventListener CreateListener(string? eventName = null, bool localOnly = false);
}
