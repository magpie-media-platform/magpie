using System;
using System.Collections.Generic;

using Magpie.Models.Jobs;

namespace Magpie.Data.Database.Entities;

public record JobExecution
{
    public Guid Id { get; set; }
    public Guid JobTemplateId { get; set; }
    public Guid? ScheduleTriggerId { get; set; }
    public Guid? EventTriggerId { get; set; }
    public Guid? EventId { get; set; }
    public JobExecutionStatus Status { get; set; }
    public double? Progress { get; set; }
    public string? Message { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public DateTimeOffset? ModifiedAt { get; set; }
    public Guid? ModifiedByUserId { get; set; }

    public ScheduleTrigger? ScheduleTrigger { get; set; }
    public EventTrigger? EventTrigger { get; set; }
    public ICollection<JobExecutionParameter> Parameters { get; } = [];
}
