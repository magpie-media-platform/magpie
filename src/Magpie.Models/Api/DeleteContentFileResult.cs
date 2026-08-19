namespace Magpie.Models.Api;

/// <summary>
/// The result of an attempt to delete a content file.
/// </summary>
public sealed record DeleteContentFileResult
{
    /// <summary>
    /// Whether the host file or its association was deleted.
    /// </summary>
    public bool Deleted { get; init; }

    /// <summary>
    /// Whether the stored fulfillment changed.
    /// </summary>
    public bool FulfillmentChanged { get; init; }
}
