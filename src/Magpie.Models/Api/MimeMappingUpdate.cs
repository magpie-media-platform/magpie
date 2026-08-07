namespace Magpie.Models.Api;

/// <summary>
/// The request to set the MIME type for a file extension.
/// </summary>
public sealed record MimeMappingUpdate
{
    /// <summary>
    /// The MIME type for the file extension.
    /// </summary>
    public required string MimeType { get; init; }
}
