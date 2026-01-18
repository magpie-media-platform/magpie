using System;
using System.Collections.Generic;

namespace Magpie.Models.Triggers;

/// <summary>
/// A trigger that fires a job on a schedule.
/// </summary>
/// <param name="Id">The unique identifier.</param>
/// <param name="JobTemplateId">The job template to execute.</param>
/// <param name="Name">The display name for this trigger.</param>
/// <param name="Schedule">The job schedule as a cron expression.</param>
/// <param name="Enabled">Whether the trigger is enabled.</param>
/// <param name="Parameters">Parameters to pass to the job.</param>
/// <param name="LastRunAt">When this trigger last fired.</param>
/// <param name="NextRunAt">When this trigger will next fire.</param>
/// <param name="CreatedAt">When the record was created.</param>
/// <param name="CreatedByUserId">The user who created the record.</param>
/// <param name="ModifiedAt">When the record was last modified.</param>
/// <param name="ModifiedByUserId">The user who last modified the record.</param>
public sealed record ScheduleTrigger(
    Guid Id,
    Guid JobTemplateId,
    string Name,
    string Schedule,
    bool Enabled,
    IReadOnlyDictionary<string, string?>? Parameters,
    DateTimeOffset? LastRunAt,
    DateTimeOffset? NextRunAt,
    DateTimeOffset CreatedAt,
    Guid? CreatedByUserId,
    DateTimeOffset? ModifiedAt,
    Guid? ModifiedByUserId);
