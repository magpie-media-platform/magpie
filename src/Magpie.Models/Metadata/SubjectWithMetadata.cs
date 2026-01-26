using Magpie.Models.Content;

namespace Magpie.Models.Metadata;

/// <summary>
/// A subject with its materialized metadata.
/// </summary>
/// <param name="Subject">The subject.</param>
/// <param name="MaterializedMetadata">The materialized metadata for the subject.</param>
public sealed record SubjectWithMetadata(
    Subject Subject,
    MaterializedMetadata MaterializedMetadata);
