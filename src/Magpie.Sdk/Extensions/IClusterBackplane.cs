using System.Threading;
using System.Threading.Tasks;

using Magpie.Models;
using Magpie.Sdk.Services;

namespace Magpie.Sdk.Extensions;

/// <summary>
/// The transport for <see cref="MagpieEvent"/> delivery between clustered Magpie nodes.
/// </summary>
public interface IClusterBackplane
{
    /// <summary>
    /// Broadcasts a locally raised event to peer nodes.
    /// </summary>
    /// <param name="magpieEvent">The event to broadcast</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task BroadcastEventAsync(MagpieEvent magpieEvent, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a listener for the events from peer nodes.
    /// </summary>
    IEventListener CreateListener();
}
