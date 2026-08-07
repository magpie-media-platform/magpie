namespace Magpie.Models.Enums;

/// <summary>
/// The source kind of an audit-history entry.
/// </summary>
public enum AuditHistoryEntryKind
{
    /// <summary>
    /// The entry comes from a job execution row.
    /// </summary>
    JobExecution = 0,

    /// <summary>
    /// The entry comes from an audit record row.
    /// </summary>
    AuditRecord = 1
}
