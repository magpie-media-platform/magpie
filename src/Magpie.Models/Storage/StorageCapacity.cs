namespace Magpie.Models.Storage;

/// <summary>
/// The available bytes, total bytes and volume identity at a file-system path.
/// </summary>
public sealed record StorageCapacity
{
    /// <summary>
    /// The identity of the resolved volume, or null when it cannot be read.
    /// </summary>
    public string? VolumeId { get; set; }

    /// <summary>
    /// The bytes available to the current process.
    /// </summary>
    public long AvailableBytes { get; set; }

    /// <summary>
    /// The total bytes of the resolved volume.
    /// </summary>
    public long TotalBytes { get; set; }
}
