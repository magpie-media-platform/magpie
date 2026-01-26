using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Models.Result;
using Magpie.Models.Triggers;

namespace Magpie.Abstractions.Data;

/// <summary>
/// Repository for managing event triggers.
/// </summary>
public interface IEventTriggerRepository :
    ICrudRepository<Guid, EventTrigger>,
    IPatchRepository<Guid, EventTrigger, EventTriggerPatch>
{
    /// <summary>
    /// Get an event trigger by name.
    /// </summary>
    /// <param name="name">The trigger name.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task<Result<EventTrigger?>> GetByNameAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get all event triggers for a specific job template.
    /// </summary>
    /// <param name="jobTemplateId">The job template identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task<Result<IReadOnlyList<EventTrigger>>> GetByJobTemplateIdAsync(
        Guid jobTemplateId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get all enabled event triggers.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task<Result<IReadOnlyList<EventTrigger>>> GetEnabledAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Get all enabled event triggers that match a specific event name.
    /// </summary>
    /// <param name="eventName">The event name to match.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task<Result<IReadOnlyList<EventTrigger>>> GetByEventNameAsync(
        string eventName,
        CancellationToken cancellationToken = default);
}
