namespace Magpie.Models.Api;

/// <summary>
/// Response from creating a database backup.
/// </summary>
/// <param name="Id">The backup identifier.</param>
public sealed record BackupCreateResponse(string Id);
