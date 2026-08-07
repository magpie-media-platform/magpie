namespace Magpie.Models.Arguments;

/// <summary>
/// The query filters for extensions.
/// </summary>
public sealed record ExtensionQuery
{
    /// <summary>
    /// The NuGet package id to match.
    /// </summary>
    public string? PackageId { get; init; }
}
