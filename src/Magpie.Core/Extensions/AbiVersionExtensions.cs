using System;

namespace Magpie.Core.Extensions;

internal static class AbiVersionExtensions
{
    public static bool IsCompatibleWith(this Version currentAbi, string? targetAbi)
    {
        if (string.IsNullOrWhiteSpace(targetAbi))
        {
            return true;
        }

        var normalizedCurrent = currentAbi.Normalize();
        var value = targetAbi.Trim();

        return IsRangeExpression(value)
            ? IsVersionRangeCompatible(value, normalizedCurrent)
            : TryParseAbiVersion(value, out var exact) && normalizedCurrent.CompareTo(exact) == 0;
    }

    public static Version Normalize(this Version version)
    {
        return new(
            version.Major,
            Math.Max(version.Minor, 0),
            Math.Max(version.Build, 0),
            Math.Max(version.Revision, 0));
    }

    public static string Format(this Version version)
    {
        return version.Revision > 0 ? version.ToString(4) : version.ToString(3);
    }

    private static bool IsVersionRangeCompatible(string value, Version currentAbi)
    {
        if (value.Length < 2)
        {
            return false;
        }

        var includeMin = value[0] == '[';
        var includeMax = value[^1] == ']';
        if ((value[0] is not '[' and not '(') || (value[^1] is not ']' and not ')'))
        {
            return false;
        }

        var body = value[1..^1];
        if (!body.Contains(',', StringComparison.Ordinal))
        {
            return includeMin &&
                includeMax &&
                TryParseAbiVersion(body, out var exact) &&
                currentAbi.CompareTo(exact) == 0;
        }

        var parts = body.Split(',', 2, StringSplitOptions.TrimEntries);
        if (parts.Length != 2 ||
            !TryParseOptionalAbiVersion(parts[0], out var min) ||
            !TryParseOptionalAbiVersion(parts[1], out var max))
        {
            return false;
        }

        if (min is not null)
        {
            var minCompare = currentAbi.CompareTo(min);
            if (includeMin ? minCompare < 0 : minCompare <= 0)
            {
                return false;
            }
        }

        if (max is not null)
        {
            var maxCompare = currentAbi.CompareTo(max);
            if (includeMax ? maxCompare > 0 : maxCompare >= 0)
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryParseOptionalAbiVersion(string value, out Version? version)
    {
        version = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        if (!TryParseAbiVersion(value, out var parsed))
        {
            return false;
        }

        version = parsed;
        return true;
    }

    private static bool TryParseAbiVersion(string value, out Version version)
    {
        var metadataStart = value.IndexOfAny(['-', '+']);
        var normalized = metadataStart < 0 ? value : value[..metadataStart];
        if (!Version.TryParse(normalized.Trim(), out var parsed))
        {
            version = new Version(0, 0);
            return false;
        }

        version = parsed.Normalize();
        return true;
    }

    private static bool IsRangeExpression(string value)
    {
        return value.StartsWith('[') || value.StartsWith('(') || value.EndsWith(']') || value.EndsWith(')');
    }
}
