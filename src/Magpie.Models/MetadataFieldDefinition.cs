using Magpie.Models.Enums;

namespace Magpie.Models;

/// <summary>
/// The definition of a standard metadata field.
/// </summary>
public sealed record MetadataFieldDefinition
{
    /// <summary>
    /// The metadata field that this definition applies to.
    /// </summary>
    public MetadataField Field { get; init; }

    /// <summary>
    /// The stable key of the field.
    /// </summary>
    public required string Key { get; init; }

    /// <summary>
    /// The display name of the field.
    /// </summary>
    public required string DisplayName { get; init; }

    /// <summary>
    /// Whether Magpie can check advanced-search candidates locally for this field.
    /// </summary>
    public bool CandidateResultCheckable { get; init; }

    /// <summary>
    /// Whether routine candidate trimming stays out of the user warnings.
    /// </summary>
    public bool SuppressRoutineCandidateTrimWarning { get; init; }

    /// <summary>
    /// The phrase that names the filter for this field in messages.
    /// </summary>
    public required string FilterDescription { get; init; }

    /// <summary>
    /// The phrase that names a missing value of this field in messages.
    /// </summary>
    public required string MissingValueDescription { get; init; }
}
