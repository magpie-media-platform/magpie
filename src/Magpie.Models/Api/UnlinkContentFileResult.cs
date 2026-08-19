namespace Magpie.Models.Api;

/// <summary>
/// The result of an attempt to unlink a content file.
/// </summary>
public sealed record UnlinkContentFileResult
{
    /// <summary>
    /// Whether the file association was removed.
    /// </summary>
    public bool Removed { get; init; }

    /// <summary>
    /// Whether the stored fulfillment changed.
    /// </summary>
    public bool FulfillmentChanged { get; init; }
}
