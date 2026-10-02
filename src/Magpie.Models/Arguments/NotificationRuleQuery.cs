namespace Magpie.Models.Arguments;

/// <summary>
/// The query filters for notification rules.
/// </summary>
public sealed record NotificationRuleQuery
{
    /// <summary>
    /// The enabled state to match.
    /// </summary>
    public bool? Enabled { get; init; }
}
