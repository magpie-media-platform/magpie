namespace Magpie.Models.Enums;

/// <summary>
/// The role of a quality rule in release evaluation.
/// </summary>
public enum RuleKind
{
    /// <summary>
    /// A release must match the rule to be eligible.
    /// </summary>
    Accept = 1,

    /// <summary>
    /// The rule rejects a matching release.
    /// </summary>
    Exclude = 2
}
