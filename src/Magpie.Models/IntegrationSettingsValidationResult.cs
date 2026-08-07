using System.Collections.Generic;

namespace Magpie.Models;

/// <summary>
/// The result of a settings validation for an integration.
/// </summary>
public sealed record IntegrationSettingsValidationResult
{
    /// <summary>
    /// Whether the settings are valid.
    /// </summary>
    public required bool IsValid { get; init; }

    /// <summary>
    /// The validation issues, empty when the settings are valid.
    /// </summary>
    public IReadOnlyList<IntegrationSettingsValidationIssue> Issues { get; init; } = [];
}
