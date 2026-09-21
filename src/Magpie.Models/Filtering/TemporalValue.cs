using System;
using System.Globalization;
using System.Linq;

namespace Magpie.Models.Filtering;

/// <summary>
/// A normalized instant in UTC with the calendar period of that instant.
/// </summary>
public sealed record TemporalValue
{
    /// <summary>
    /// The start instant of the period, in UTC.
    /// </summary>
    public required DateTime Value { get; set; }

    /// <summary>
    /// The calendar period of this value.
    /// </summary>
    public required TemporalPrecision Precision { get; set; }

    /// <summary>
    /// Parses an unambiguous temporal text form into a value with its calendar period.
    /// </summary>
    /// <param name="text">The text in year, month, day, or exact instant form</param>
    /// <param name="value">The parsed value, or null when the text matches no supported form</param>
    public static bool TryParse(string? text, out TemporalValue? value)
    {
        value = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var s = text.Trim();

        if (s.Length == 4 && s.All(char.IsDigit))
        {
            if (int.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out var year) &&
                year is >= 1 and <= 9999)
            {
                value = new TemporalValue
                {
                    Value = new DateTime(year, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                    Precision = TemporalPrecision.Year
                };
                return true;
            }

            return false;
        }

        if (DateTime.TryParseExact(
            s,
            "yyyy-MM",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var month))
        {
            value = new TemporalValue
            {
                Value = new DateTime(month.Year, month.Month, 1, 0, 0, 0, DateTimeKind.Utc),
                Precision = TemporalPrecision.Month
            };
            return true;
        }

        if (DateTime.TryParseExact(
            s,
            "yyyy-MM-dd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var day))
        {
            value = new TemporalValue
            {
                Value = new DateTime(day.Year, day.Month, day.Day, 0, 0, 0, DateTimeKind.Utc),
                Precision = TemporalPrecision.Day
            };
            return true;
        }

        if (HasExplicitOffset(s) &&
            DateTimeOffset.TryParse(
                s,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var instant))
        {
            value = new TemporalValue
            {
                Value = instant.UtcDateTime,
                Precision = TemporalPrecision.Instant
            };
            return true;
        }

        return false;
    }

    private static bool HasExplicitOffset(string s)
    {
        var separator = s.IndexOf('T');
        if (separator < 0)
        {
            separator = s.IndexOf(' ');
        }

        if (separator < 0)
        {
            return false;
        }

        var tail = s[(separator + 1)..];
        var offset = tail.IndexOfAny(['Z', 'z', '+', '-']);
        return offset > 0 && tail[..offset].Contains(':');
    }
}
