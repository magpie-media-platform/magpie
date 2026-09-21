using System.Diagnostics.CodeAnalysis;

namespace Magpie.Models;

/// <summary>
/// One validation issue for an integration setting.
/// </summary>
/// <param name="Key">The setting key that the issue applies to, or null for a form-level issue</param>
/// <param name="Message">The message that the UI shows to the user</param>
[method: SetsRequiredMembers]
public sealed record IntegrationSettingsValidationIssue(string? Key, string Message)
{
    /// <summary>
    /// The setting key that the issue applies to, or null for a form-level issue.
    /// </summary>
    public string? Key { get; set; } = Key;

    /// <summary>
    /// The message that the UI shows to the user.
    /// </summary>
    public string Message { get; set; } = Message;
}
