using System;

namespace Magpie.Sdk.Content;

/// <summary>
/// Indicates that a metadata search checkpoint is invalid or incompatible.
/// </summary>
/// <param name="message">The reason that the checkpoint cannot be used</param>
public sealed class MetadataSearchContinuationException(string message) : Exception(message)
{
}
