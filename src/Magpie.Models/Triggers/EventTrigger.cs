using System;
using System.Collections.Generic;

namespace Magpie.Models.Triggers;

/// <summary>
/// A trigger that fires a job when matching events occur.
/// </summary>
/// <param name="Id">The unique identifier.</param>
/// <param name="JobTemplateId">The job template to execute when triggered.</param>
/// <param name="Name">The display name for this trigger.</param>
/// <param name="EventName">The event name to match (exact or pattern).</param>
/// <param name="Filter">Criteria to match against event context, if any.</param>
/// <param name="Enabled">Whether the trigger is enabled.</param>
/// <param name="Parameters">Parameters to pass to the job. May reference event context.</param>
/// <param name="CreatedAt">When the record was created.</param>
/// <param name="CreatedByUserId">The user who created the record.</param>
/// <param name="ModifiedAt">When the record was last modified.</param>
/// <param name="ModifiedByUserId">The user who last modified the record.</param>
public sealed record EventTrigger(
    Guid Id,
    Guid JobTemplateId,
    string Name,
    string EventName,
    IReadOnlyDictionary<string, string?>? Filter,
    bool Enabled,
    IReadOnlyDictionary<string, string?>? Parameters,
    DateTimeOffset CreatedAt,
    Guid? CreatedByUserId,
    DateTimeOffset? ModifiedAt,
    Guid? ModifiedByUserId);
