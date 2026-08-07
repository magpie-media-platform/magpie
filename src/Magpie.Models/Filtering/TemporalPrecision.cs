namespace Magpie.Models.Filtering;

/// <summary>
/// The calendar period of a parsed temporal value.
/// </summary>
public enum TemporalPrecision
{
    /// <summary>
    /// The value covers one whole year.
    /// </summary>
    Year,

    /// <summary>
    /// The value covers one whole month.
    /// </summary>
    Month,

    /// <summary>
    /// The value covers one whole day.
    /// </summary>
    Day,

    /// <summary>
    /// The value is one exact instant in time.
    /// </summary>
    Instant
}
