namespace Magpie.Sdk.Releases;

/// <summary>
/// The confidence bounds and signal weights for release matching.
/// </summary>
public static class ReleaseMatchConfidence
{
    /// <summary>
    /// The lowest confidence for an automatic match.
    /// </summary>
    public const double AcceptThreshold = 0.5;

    /// <summary>
    /// The weight of the title signal.
    /// </summary>
    public const double TitleWeight = 0.6;

    /// <summary>
    /// The weight of the year signal.
    /// </summary>
    public const double YearWeight = 0.25;

    /// <summary>
    /// Full confidence.
    /// </summary>
    public const double Certain = 1.0;

    /// <summary>
    /// No confidence.
    /// </summary>
    public const double None = 0.0;
}
