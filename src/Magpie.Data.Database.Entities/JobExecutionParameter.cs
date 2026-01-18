using System;

namespace Magpie.Data.Database.Entities;

public class JobExecutionParameter
{
    public Guid JobExecutionId { get; set; }
    public required string Key { get; set; }
    public string? Value { get; set; }

    public required JobExecution JobExecution { get; set; }
}
