namespace Magpie.Models;

/// <summary>
/// The numbers of rows that an audit-history purge removes.
/// </summary>
public sealed record AuditHistoryPurgeCounts
{
    /// <summary>
    /// The number of audit records.
    /// </summary>
    public required int AuditRecords { get; init; }

    /// <summary>
    /// The number of job executions.
    /// </summary>
    public required int JobExecutions { get; init; }
}
