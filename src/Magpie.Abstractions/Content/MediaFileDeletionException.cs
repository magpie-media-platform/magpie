using System;

namespace Magpie.Abstractions.Content;

/// <summary>
/// The error for a media file that Magpie cannot delete from disk.
/// </summary>
public sealed class MediaFileDeletionException(string message, Exception innerException)
    : Exception(message, innerException)
{
}
