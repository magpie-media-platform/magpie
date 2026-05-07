namespace Magpie.Auth;

internal static class Constants
{
    public const string ApiKeyHeader = "X-Api-Key";
}

/// <summary>
/// Public auth-related identifiers exposed to the host composition root.
/// </summary>
public static class MagpieAuthSchemes
{
    /// <summary>
    /// The name of the bundled Magpie authentication scheme.
    /// </summary>
    public const string Magpie = "Magpie";
}
