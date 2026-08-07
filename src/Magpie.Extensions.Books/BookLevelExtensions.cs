using System;
using System.IO;
using System.Linq;

namespace Magpie.Extensions.Books;

internal static class BookLevelExtensions
{
    private static readonly string[] AudiobookExtensions =
        [".m4b", ".mp3", ".m4a", ".aac", ".flac", ".ogg", ".opus", ".wav"];

    private static readonly string[] EbookExtensions = [".epub", ".azw3", ".azw", ".mobi", ".pdf"];

    public static string? ResolveBookLevelSlug(this string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return null;
        }

        var stem = Path.GetFileNameWithoutExtension(filePath);
        if (string.IsNullOrEmpty(stem))
        {
            return null;
        }

        var extension = Path.GetExtension(filePath);
        if (EbookExtensions.Any(supported =>
            string.Equals(extension, supported, StringComparison.OrdinalIgnoreCase)))
        {
            return Constants.EbookLevelSlug;
        }

        return AudiobookExtensions.Any(supported =>
            string.Equals(extension, supported, StringComparison.OrdinalIgnoreCase))
                ? Constants.AudiobookLevelSlug
                : null;
    }
}
