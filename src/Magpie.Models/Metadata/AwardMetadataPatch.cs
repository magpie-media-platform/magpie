using System;

namespace Magpie.Models.Metadata;

/// <summary>
/// Patch for <see cref="AwardMetadata"/>.
/// </summary>
public sealed record AwardMetadataPatch
{
    /// <inheritdoc cref="AwardMetadata.Id" />
    public Guid Id { get; init; }
    /// <inheritdoc cref="AwardMetadata.Ordinal" />
    public FieldPatch<int>? Ordinal { get; init; }
    /// <inheritdoc cref="AwardMetadata.AwardBody" />
    public FieldPatch<string>? AwardBody { get; init; }
    /// <inheritdoc cref="AwardMetadata.Name" />
    public FieldPatch<string>? Name { get; init; }
    /// <inheritdoc cref="AwardMetadata.Category" />
    public FieldPatch<string?>? Category { get; init; }
    /// <inheritdoc cref="AwardMetadata.Status" />
    public FieldPatch<string?>? Status { get; init; }
    /// <inheritdoc cref="AwardMetadata.Year" />
    public FieldPatch<int?>? Year { get; init; }
    /// <inheritdoc cref="AwardMetadata.Subject" />
    public FieldPatch<string?>? Subject { get; init; }
    /// <inheritdoc cref="AwardMetadata.Language" />
    public FieldPatch<string?>? Language { get; init; }
    /// <inheritdoc cref="AwardMetadata.Country" />
    public FieldPatch<string?>? Country { get; init; }
}
