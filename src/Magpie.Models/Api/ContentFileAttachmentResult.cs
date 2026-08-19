using Magpie.Models.Data;

namespace Magpie.Models.Api;

/// <summary>
/// The file attachment and its role in content fulfillment.
/// </summary>
public sealed record ContentFileAttachmentResult
{
    /// <summary>
    /// The attached media file.
    /// </summary>
    public required MediaFile MediaFile { get; init; }

    /// <summary>
    /// Whether the file is primary content instead of a sidecar.
    /// </summary>
    public bool IsPrimary { get; init; }
}
