using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

using Magpie.Sdk.Content;

namespace Magpie.Extensions.Music.Parsing;

internal static class FilePathExtensions
{
    private static readonly Regex LeadingTrackNumber = new(
        @"^\d{1,3}[\s\-\.]+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static ParsedMusicFile ParseMusicFilename(this string filePath)
    {
        var stem = Path.GetFileNameWithoutExtension(filePath) ?? string.Empty;
        var filenameTitle = StripLeadingTrackNumber(stem.NormalizeMediaFilename());

        var parentFolder = Path.GetFileName(Path.GetDirectoryName(filePath)) ?? string.Empty;
        var parentNormalised = parentFolder.NormalizeMediaFilename();
        var (album, year) = ExtractAlbumAndYear(parentNormalised, fallbackTitle: filenameTitle);

        var grandparentFolder = Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(filePath)))
            ?? string.Empty;
        var artist = string.IsNullOrEmpty(grandparentFolder)
            ? null
            : NullIfEmpty(grandparentFolder.NormalizeMediaFilename());

        return new ParsedMusicFile
        {
            Artist = artist,
            Album = album,
            Year = year
        };
    }

    private static string StripLeadingTrackNumber(string stemNormalised)
    {
        var match = LeadingTrackNumber.Match(stemNormalised);
        return match.Success ? stemNormalised[match.Length..].Trim() : stemNormalised;
    }

    private static (string Album, int? Year) ExtractAlbumAndYear(string parentNormalised, string fallbackTitle)
    {
        if (string.IsNullOrEmpty(parentNormalised))
        {
            return (fallbackTitle, null);
        }

        var yearMatches = parentNormalised.FindYearTokens();
        if (yearMatches.Count == 0)
        {
            return (parentNormalised, null);
        }

        var lastMatch = yearMatches[^1];
        var year = int.Parse(lastMatch.Groups[1].Value, CultureInfo.InvariantCulture);
        var album = parentNormalised[..lastMatch.Index].Trim();
        return (string.IsNullOrEmpty(album) ? parentNormalised : album, year);
    }

    private static string? NullIfEmpty(string value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
