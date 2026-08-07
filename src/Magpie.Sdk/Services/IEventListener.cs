using System;
using System.Collections.Generic;
using System.Threading;

using Magpie.Models;

namespace Magpie.Sdk.Services;

/// <summary>
/// A disposable subscription to the events on the bus.
/// </summary>
public interface IEventListener : IDisposable, IAsyncDisposable
{
    /// <summary>
    /// Reads events as they arrive, until the caller disposes the listener or cancels the token.
    /// </summary>
    /// <param name="cancellationToken">The token that stops the enumeration</param>
    IAsyncEnumerable<MagpieEvent> ReadAllAsync(CancellationToken cancellationToken = default);
}
