namespace Magpie.Core.Config;

/// <summary>
/// The extension loading options for a Magpie host.
/// </summary>
public sealed record ExtensionHostOptions
{
    /// <summary>
    /// The filesystem path to the extensions directory.
    /// </summary>
    public string? Path { get; init; }
}
