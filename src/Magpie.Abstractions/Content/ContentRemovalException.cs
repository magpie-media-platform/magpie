using System;

namespace Magpie.Abstractions.Content;

/// <summary>
/// The error for a content item that Magpie cannot remove.
/// </summary>
public sealed class ContentRemovalException(
    string message,
    Exception innerException) : Exception(message, innerException)
{
}
