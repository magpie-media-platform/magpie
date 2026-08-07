namespace Magpie.Models;

/// <summary>
/// One validation issue for an integration setting.
/// </summary>
/// <param name="Key">The setting key that the issue applies to, or null for a form-level issue</param>
/// <param name="Message">The message that the UI shows to the user</param>
public sealed record IntegrationSettingsValidationIssue(string? Key, string Message);
