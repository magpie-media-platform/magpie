using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Magpie.Extensions.Cardigann.Engine.Filters;

internal static class CardigannFilterExtensions
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(2);

    public static string ApplyCardigannFilter(
        this string input,
        string filterName,
        object? args,
        TimeProvider timeProvider)
    {
        var data = input ?? string.Empty;

        if (string.IsNullOrEmpty(filterName))
        {
            return data;
        }

        try
        {
            switch (filterName)
            {
                case "querystring":
                    return QueryString(data, ArgString(args));

                case "timeparse":
                case "dateparse":
                    return DateParse(data, ArgString(args), timeProvider);

                case "regexp":
                    return Regexp(data, ArgString(args));

                case "re_replace":
                    return ReReplace(data, ArgAt(args, 0), ArgAt(args, 1));

                case "split":
                    return Split(data, ArgAt(args, 0), ArgAt(args, 1));

                case "replace":
                    return Replace(data, ArgAt(args, 0), ArgAt(args, 1));

                case "replacechars":
                    return ReplaceChars(data, ArgAt(args, 0), ArgAt(args, 1));

                case "trim":
                    return Trim(data, ArgString(args));

                case "prepend":
                    return (ArgString(args) ?? string.Empty) + data;

                case "append":
                    return data + (ArgString(args) ?? string.Empty);

                case "tolower":
                    return data.ToLowerInvariant();

                case "toupper":
                    return data.ToUpperInvariant();

                case "whitespace":
                case "normalize":
                    return NormalizeWhitespace(data);

                case "urldecode":
                    return WebUtility.UrlDecode(data);

                case "urlencode":
                    return WebUtility.UrlEncode(data) ?? string.Empty;

                case "htmldecode":
                    return WebUtility.HtmlDecode(data);

                case "htmlencode":
                    return WebUtility.HtmlEncode(data);

                case "timeago":
                case "reltime":
                    return FromTimeAgo(data, timeProvider);

                case "fuzzytime":
                    return FromUnknown(data, timeProvider);

                case "validfilename":
                    return MakeValidFileName(data);

                case "diacritics":
                    return Diacritics(data, ArgString(args));

                case "jsonjoinarray":
                    return JsonJoinArray(data, ArgAt(args, 0), ArgAt(args, 1));

                case "validate":
                    return Validate(data, ArgString(args));

                default:
                    return data;
            }
        }
        catch (RegexMatchTimeoutException)
        {
            throw;
        }
        catch (Exception ex) when (ex is ArgumentException
            or FormatException
            or OverflowException
            or IndexOutOfRangeException
            or JsonException)
        {
            return data;
        }
    }

    private static string QueryString(string data, string? argument)
    {
        if (argument == null)
        {
            return data;
        }

        var queryStart = data.IndexOf('?', StringComparison.Ordinal);
        if (queryStart < 0)
        {
            return string.Empty;
        }

        var qs = data.Substring(queryStart + 1);
        var hashIdx = qs.IndexOf('#', StringComparison.Ordinal);
        if (hashIdx >= 0)
        {
            qs = qs.Substring(0, hashIdx);
        }

        foreach (var pair in qs.Split('&'))
        {
            if (pair.Length == 0)
            {
                continue;
            }

            var eq = pair.IndexOf('=', StringComparison.Ordinal);
            var key = eq < 0 ? pair : pair.Substring(0, eq);
            if (WebUtility.UrlDecode(key) == argument)
            {
                var value = eq < 0 ? string.Empty : pair.Substring(eq + 1);
                return WebUtility.UrlDecode(value);
            }
        }

        return string.Empty;
    }

    private static string Regexp(string data, string? pattern)
    {
        if (pattern == null)
        {
            return data;
        }

        var match = Regex.Match(data, pattern, RegexOptions.None, RegexTimeout);
        if (!match.Success)
        {
            return string.Empty;
        }

        return match.Groups.Count > 1 ? match.Groups[1].Value : match.Value;
    }

    private static string ReReplace(string data, string? pattern, string? replacement)
    {
        if (pattern == null)
        {
            return data;
        }

        return Regex.Replace(data, pattern, replacement ?? string.Empty, RegexOptions.None, RegexTimeout);
    }

    private static string Replace(string data, string? from, string? to)
    {
        if (string.IsNullOrEmpty(from))
        {
            return data;
        }

        return data.Replace(from, to ?? string.Empty, StringComparison.Ordinal);
    }

    private static string ReplaceChars(string data, string? from, string? to)
    {
        if (string.IsNullOrEmpty(from))
        {
            return data;
        }

        to ??= string.Empty;
        var sb = new StringBuilder(data.Length);
        foreach (var c in data)
        {
            var idx = from.IndexOf(c, StringComparison.Ordinal);
            if (idx < 0)
            {
                sb.Append(c);
            }
            else if (idx < to.Length)
            {
                sb.Append(to[idx]);
            }
        }

        return sb.ToString();
    }

    private static string Split(string data, string? separator, string? position)
    {
        if (string.IsNullOrEmpty(separator) || position == null)
        {
            return data;
        }

        if (!int.TryParse(position, NumberStyles.Integer, CultureInfo.InvariantCulture, out var pos))
        {
            return data;
        }

        var parts = data.Split(separator![0]);
        if (pos < 0)
        {
            pos += parts.Length;
        }

        if (pos < 0 || pos >= parts.Length)
        {
            return data;
        }

        return parts[pos];
    }

    private static string Trim(string data, string? cutset)
    {
        if (string.IsNullOrEmpty(cutset))
        {
            return data.Trim();
        }

        return data.Trim(cutset![0]);
    }

    private static string NormalizeWhitespace(string data)
    {
        return Regex.Replace(data.Trim(), @"\s+", " ", RegexOptions.None, RegexTimeout);
    }

    private static string Diacritics(string data, string? op)
    {
        if (op != "replace")
        {
            return data;
        }

        var formD = data.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(formD.Length);
        foreach (var ch in formD)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
            {
                sb.Append(ch);
            }
        }

        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    private static string Validate(string data, string? args)
    {
        if (args == null)
        {
            return data;
        }

        var delimiters = new char[] { ',', ' ', '/', ')', '(', '.', ';', '[', ']', '"', '|', ':' };
        var validList = args.ToLowerInvariant().Split(delimiters, StringSplitOptions.RemoveEmptyEntries).ToList();
        var present = data.ToLowerInvariant().Split(delimiters, StringSplitOptions.RemoveEmptyEntries);
        var intersect = validList.Intersect(present).ToList();
        return string.Join(",", intersect);
    }

    private static string MakeValidFileName(string data)
    {
        var invalids = System.IO.Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(data.Length);
        var changed = false;
        foreach (var c in data)
        {
            if (Array.IndexOf(invalids, c) >= 0)
            {
                changed = true;
                sb.Append('_');
            }
            else
            {
                sb.Append(c);
            }
        }

        if (sb.Length == 0)
        {
            return "_";
        }

        return changed ? sb.ToString() : data;
    }

    private static string JsonJoinArray(string data, string? jsonPath, string? separator)
    {
        if (jsonPath == null)
        {
            return data;
        }

        using var doc = JsonDocument.Parse(data);
        var token = doc.RootElement.ResolvePath(jsonPath);
        if (token is not { ValueKind: JsonValueKind.Array })
        {
            return data;
        }

        var parts = token.Value.EnumerateArray().Select(element => element.ToScalarString() ?? string.Empty);
        return string.Join(separator ?? string.Empty, parts);
    }

    private static string DateParse(string data, string? layout, TimeProvider timeProvider)
    {
        if (layout == null)
        {
            return data;
        }

        var parsed = ParseDateTimeGoLang(data, layout, timeProvider);
        if (parsed == null)
        {
            return data;
        }

        return ToIso(parsed.Value);
    }

    private static string FromTimeAgo(string data, TimeProvider timeProvider)
    {
        var parsed = ParseTimeAgo(data, timeProvider.GetUtcNow().UtcDateTime);
        if (parsed == null)
        {
            return data;
        }

        return ToIso(parsed.Value);
    }

    private static string FromUnknown(string data, TimeProvider timeProvider)
    {
        var str = data.Trim();
        var now = timeProvider.GetUtcNow().UtcDateTime;

        if (str.Length > 0 &&
            str.All(char.IsDigit) &&
            long.TryParse(str, NumberStyles.Integer, CultureInfo.InvariantCulture, out var unix))
        {
            return ToIso(DateTimeOffset.FromUnixTimeSeconds(unix).UtcDateTime);
        }

        if (str.IndexOf("now", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return ToIso(now);
        }

        if (Regex.IsMatch(str, @"(?i)\bago", RegexOptions.None, RegexTimeout))
        {
            var ago = ParseTimeAgo(str, now);
            if (ago != null)
            {
                return ToIso(ago.Value);
            }
        }

        var keyword = Regex.Match(str, @"(?i)\b(today|tomorrow|yesterday)\b", RegexOptions.None, RegexTimeout);
        if (keyword.Success)
        {
            var dayOffset = keyword.Groups[1].Value.ToLowerInvariant() switch
            {
                "tomorrow" => 1,
                "yesterday" => -1,
                _ => 0,
            };
            var timePart = str.Substring(keyword.Index + keyword.Length).Trim();
            var time = TimeSpan.Zero;
            if (timePart.Length > 0 &&
                DateTime.TryParse(timePart, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
            {
                time = dt.TimeOfDay;
            }

            return ToIso(now.Date.AddDays(dayOffset) + time);
        }

        if (DateTime.TryParse(
            str,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var generic))
        {
            return ToIso(generic);
        }

        return data;
    }

    private static DateTime? ParseTimeAgo(string str, DateTime now)
    {
        var lower = str.ToLowerInvariant();
        if (lower.Contains("now", StringComparison.Ordinal))
        {
            return now;
        }

        lower = lower
            .Replace(",", string.Empty, StringComparison.Ordinal)
            .Replace("ago", string.Empty, StringComparison.Ordinal)
            .Replace("and", string.Empty, StringComparison.Ordinal);

        var span = TimeSpan.Zero;
        var matched = false;
        foreach (Match m in Regex.Matches(lower, @"\s*?([\d\.]+)\s*?([^\d\s\.]+)\s*?", RegexOptions.None, RegexTimeout))
        {
            if (!float.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var val))
            {
                continue;
            }

            var unit = m.Groups[2].Value;

            if (unit.Contains("sec", StringComparison.Ordinal) || unit == "s")
            {
                span += TimeSpan.FromSeconds(val);
            }
            else if (unit.Contains("min", StringComparison.Ordinal) || unit == "m")
            {
                span += TimeSpan.FromMinutes(val);
            }
            else if (unit.Contains("hour", StringComparison.Ordinal) ||
                unit.Contains("hr", StringComparison.Ordinal) ||
                unit == "h")
            {
                span += TimeSpan.FromHours(val);
            }
            else if (unit.Contains("day", StringComparison.Ordinal) || unit == "d")
            {
                span += TimeSpan.FromDays(val);
            }
            else if (unit.Contains("week", StringComparison.Ordinal) ||
                unit.Contains("wk", StringComparison.Ordinal) ||
                unit == "w")
            {
                span += TimeSpan.FromDays(val * 7);
            }
            else if (unit.Contains("month", StringComparison.Ordinal) || unit == "mo")
            {
                span += TimeSpan.FromDays(val * 30);
            }
            else if (unit.Contains("year", StringComparison.Ordinal) || unit == "y")
            {
                span += TimeSpan.FromDays(val * 365);
            }
            else
            {
                continue;
            }

            matched = true;
        }

        if (!matched)
        {
            return null;
        }

        return now - span;
    }

    private static DateTime? ParseDateTimeGoLang(string date, string layout, TimeProvider timeProvider)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        date = date.Trim();
        var usesDateParts = layout.Contains("y", StringComparison.Ordinal) ||
            layout.Contains("h", StringComparison.Ordinal) ||
            layout.Contains("d", StringComparison.Ordinal);
        if (usesDateParts &&
            DateTime.TryParseExact(date, layout, CultureInfo.InvariantCulture, DateTimeStyles.None, out var direct))
        {
            return DateTime.SpecifyKind(direct, DateTimeKind.Utc);
        }

        var format = layout
            .Replace("2006", "yyyy", StringComparison.Ordinal)
            .Replace("06", "yy", StringComparison.Ordinal)
            .Replace("January", "MMMM", StringComparison.Ordinal)
            .Replace("Jan", "MMM", StringComparison.Ordinal)
            .Replace("01", "MM", StringComparison.Ordinal)
            .Replace("Monday", "dddd", StringComparison.Ordinal)
            .Replace("Mon", "ddd", StringComparison.Ordinal)
            .Replace("02", "dd", StringComparison.Ordinal)
            .Replace("2", "d", StringComparison.Ordinal)
            .Replace("05", "ss", StringComparison.Ordinal)
            .Replace("15", "HH", StringComparison.Ordinal)
            .Replace("03", "hh", StringComparison.Ordinal)
            .Replace("3", "h", StringComparison.Ordinal)
            .Replace("04", "mm", StringComparison.Ordinal)
            .Replace("4", "m", StringComparison.Ordinal)
            .Replace("5", "s", StringComparison.Ordinal)
            .Replace("1", "M", StringComparison.Ordinal)
            .Replace(".0000", "ffff", StringComparison.Ordinal)
            .Replace(".000", "fff", StringComparison.Ordinal)
            .Replace(".00", "ff", StringComparison.Ordinal)
            .Replace(".0", "f", StringComparison.Ordinal)
            .Replace(".9999", "FFFF", StringComparison.Ordinal)
            .Replace(".999", "FFF", StringComparison.Ordinal)
            .Replace(".99", "FF", StringComparison.Ordinal)
            .Replace(".9", "F", StringComparison.Ordinal)
            .Replace("PM", "tt", StringComparison.Ordinal)
            .Replace("pm", "tt", StringComparison.Ordinal)
            .Replace("Z07:00", "'Z'zzz", StringComparison.Ordinal)
            .Replace("Z07", "'Z'zz", StringComparison.Ordinal)
            .Replace("-07:00", "zzz", StringComparison.Ordinal)
            .Replace("-07", "zz", StringComparison.Ordinal);

        if (!DateTime.TryParseExact(date, format, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
        {
            return null;
        }

        if (!format.Contains("yy", StringComparison.Ordinal) && parsed > now)
        {
            parsed = parsed.AddYears(-1);
        }

        return DateTime.SpecifyKind(parsed, DateTimeKind.Utc);
    }

    private static string ToIso(DateTime value)
    {
        var utc = value.Kind == DateTimeKind.Utc ? value : DateTime.SpecifyKind(value, DateTimeKind.Utc);
        return utc.ToString("o", CultureInfo.InvariantCulture);
    }

    private static string? ArgString(object? args)
    {
        switch (args)
        {
            case null:
                return null;
            case string s:
                return s;
            case System.Collections.IDictionary map:
                foreach (var value in map.Values)
                {
                    return value?.ToString();
                }

                return null;
            case IEnumerable<object> list:
                var first = list.FirstOrDefault();
                return first?.ToString();
            default:
                return args.ToString();
        }
    }

    private static string? ArgAt(object? args, int index)
    {
        switch (args)
        {
            case null:
                return null;
            case string s:
                return index == 0 ? s : null;
            case System.Collections.IDictionary map:
                var values = new List<object?>();
                foreach (var value in map.Values)
                {
                    values.Add(value);
                }

                return index >= 0 && index < values.Count ? values[index]?.ToString() : null;
            case IReadOnlyList<object> indexed:
                return index >= 0 && index < indexed.Count ? indexed[index]?.ToString() : null;
            case IEnumerable<object> seq:
                var materialized = seq.ToList();
                return index >= 0 && index < materialized.Count ? materialized[index]?.ToString() : null;
            default:
                return index == 0 ? args.ToString() : null;
        }
    }
}
