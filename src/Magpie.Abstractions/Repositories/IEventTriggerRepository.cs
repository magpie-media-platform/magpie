using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Models.Arguments;
using Magpie.Models.Data;

namespace Magpie.Abstractions.Repositories;

/// <summary>
/// Repository for managing event triggers.
/// </summary>
public interface IEventTriggerRepository :
    ICrudRepository<Guid, EventTrigger>,
    IPatchRepository<Guid, EventTrigger, EventTriggerPatch>
{
    /// <summary>
    /// List event triggers for a specific extension.
    /// </summary>
    /// <param name="extensionId">The extension id.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task<IEnumerable<EventTrigger>> ListByExtensionIdAsync(
        Guid extensionId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// List all enabled event triggers.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task<IEnumerable<EventTrigger>> ListEnabledAsync(CancellationToken cancellationToken = default);
}
