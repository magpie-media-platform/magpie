using System;

namespace Magpie.Data.Database.Entities;

public record JobExecutionParameter
{
    public Guid JobExecutionId { get; set; }
    public required string Key { get; set; }
    public string? Value { get; set; }

    public JobExecution? JobExecution { get; set; }
}
