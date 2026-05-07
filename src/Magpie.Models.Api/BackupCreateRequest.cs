namespace Magpie.Models.Api;

/// <summary>
/// Request to create a database backup.
/// </summary>
public sealed record BackupCreateRequest
{
    /// <summary>
    /// Optional name for the backup. If not provided, a timestamp-based name is generated.
    /// </summary>
    public string? Name { get; init; }
}
