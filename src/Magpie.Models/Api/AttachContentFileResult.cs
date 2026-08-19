namespace Magpie.Models.Api;

/// <summary>
/// The result of an attempt to attach a content file.
/// </summary>
public sealed record AttachContentFileResult
{
    /// <summary>
    /// The attachment result, or null if the content does not exist.
    /// </summary>
    public ContentFileAttachmentResult? Result { get; init; }

    /// <summary>
    /// Whether the stored fulfillment changed.
    /// </summary>
    public bool FulfillmentChanged { get; init; }
}
