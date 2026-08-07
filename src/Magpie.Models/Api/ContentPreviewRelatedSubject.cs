namespace Magpie.Models.Api;

/// <summary>
/// A related subject entry in a <see cref="ContentPreview"/>.
/// </summary>
public sealed record ContentPreviewRelatedSubject
{
    /// <summary>
    /// The display name of the subject.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// The kind of the subject.
    /// </summary>
    public required string Kind { get; init; }

    /// <summary>
    /// The kind of relationship between the subject and the content.
    /// </summary>
    public required string RelationshipKind { get; init; }

    /// <summary>
    /// The qualifier that refines the relationship kind.
    /// </summary>
    public string? RelationshipQualifier { get; init; }

    /// <summary>
    /// The position of the subject in the display order.
    /// </summary>
    public int Ordinal { get; init; }

    /// <summary>
    /// The URL of the image for the subject.
    /// </summary>
    public string? ImageUrl { get; init; }
}
