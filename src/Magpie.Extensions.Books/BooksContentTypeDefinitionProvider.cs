using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

using Magpie.Extensions.Books.Parsing;
using Magpie.Models;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Releases;
using Magpie.Models.Signals;
using Magpie.Sdk.Content;

namespace Magpie.Extensions.Books;

internal sealed class BooksContentTypeDefinitionProvider :
    IPathResolutionContentTypeDefinitionProvider,
    IReleaseMatchContentTypeDefinitionProvider
{
    private static readonly ContentTypeDefinition BookDefinition = new()
    {
        Slug = ContentTypeSlug,
        DisplayName = "Book",
        PluralDisplayName = "Books",
        IconName = "book",
        Categories = ["books"],
        LevelDefinitions =
        [
            new ContentHierarchyLevelDefinition
            {
                    Slug = Constants.EbookLevelSlug,
                ParentSlug = null,
                DisplayName = "E-book",
                IconName = "book",
                SupportedMetadataFields = BookMetadataFields,
                CustomMetadataFields = [],
                AcceptedFileTypes =
                [
                    "application/epub+zip",
                    "application/x-mobipocket-ebook",
                    "application/vnd.amazon.ebook",
                    "application/pdf",
                    ".epub",
                    ".mobi",
                    ".azw",
                    ".azw3",
                    ".pdf"
                ],
                AcceptedSidecarTypes = [".opf", ".jpg", ".jpeg", ".png"],
                Subfolder = "ebooks",
                StandardFactFields =
                [
                    new CustomMetadataDefinition { Key = "pages", DisplayName = "Pages", DataType = DataType.Number },
                    new CustomMetadataDefinition { Key = "isbn", DisplayName = "ISBN" },
                    new CustomMetadataDefinition { Key = "language", DisplayName = "Language" }
                ],
                IsAcquirable = true,
                OrdinalKind = OrdinalKind.Name
            },
            new ContentHierarchyLevelDefinition
            {
                    Slug = Constants.AudiobookLevelSlug,
                ParentSlug = null,
                DisplayName = "Audiobook",
                IconName = "headphones",
                SupportedMetadataFields = BookMetadataFields,
                CustomMetadataFields = [],
                AcceptedFileTypes = ["audio/*", ".m4b", ".mp3", ".m4a", ".aac", ".flac", ".ogg", ".opus", ".wav"],
                AcceptedSidecarTypes = [".cue", ".jpg", ".jpeg", ".png"],
                Subfolder = "audiobooks",
                StandardFactFields =
                [
                    new CustomMetadataDefinition { Key = "isbn", DisplayName = "ISBN" },
                    new CustomMetadataDefinition { Key = "language", DisplayName = "Language" }
                ],
                IsAcquirable = true,
                OrdinalKind = OrdinalKind.Name
            }
        ]
    };

    private const string ContentTypeSlug = "book";

    private const string AuthorSignal = "author";

    private const MetadataField BookMetadataFields =
        MetadataField.Title |
        MetadataField.OriginalTitle |
        MetadataField.SortTitle |
        MetadataField.Language |
        MetadataField.Overview |
        MetadataField.PublishDate |
        MetadataField.ExternalIds |
        MetadataField.References |
        MetadataField.Images |
        MetadataField.AlternateTexts |
        MetadataField.ContentRatings |
        MetadataField.Genres |
        MetadataField.Relationships |
        MetadataField.SequenceNumber;

    private static readonly Regex VolumePattern = new(
        @"(?<![0-9])(?:vol(?:ume)?\.?\s*|v)(\d+)(?![0-9])",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly Regex EbookFormatPattern = new(
        @"(?<![a-z0-9])(ebook|epub|mobi|azw3?|pdf)(?![a-z0-9])",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly Regex AudiobookFormatPattern = new(
        @"(?<![a-z0-9])(audiobook|audible|m4b|mp3|m4a|aac|flac|ogg)(?![a-z0-9])",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly Regex AudiobookPartPattern = new(
        @"^(?:\d{1,4}\b|chapter\b|part\b|disc\b|cd\b)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private enum BookFormat
    {
        None,
        Ebook,
        Audiobook,
        Conflicting
    }

    public ContentTypeDefinition Definition => BookDefinition;

    public ReleaseMatchResult IsMatch(
        ContentTarget target,
        IReadOnlyList<IReadOnlyList<ContentFact>> contentFacts,
        Release release,
        IReadOnlyList<ContentFact> releaseFacts)
    {
        if (string.IsNullOrWhiteSpace(release.Title))
        {
            return ReleaseMatchResult.Indeterminate;
        }

        var book = target.Lineage.FirstOrDefault(
            static item => item.Level is Constants.EbookLevelSlug or Constants.AudiobookLevelSlug);
        if (book is null)
        {
            return ReleaseMatchResult.Indeterminate;
        }

        var releaseFormat = ClassifyBookFormat(
            releaseFacts
                .SelectMany(fact => fact.Facets.Values.Prepend(fact.Value))
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Prepend(release.Title));
        if (releaseFormat == BookFormat.Conflicting)
        {
            return ReleaseMatchResult.Indeterminate;
        }

        if ((releaseFormat == BookFormat.Ebook && book.Level != Constants.EbookLevelSlug) ||
            (releaseFormat == BookFormat.Audiobook && book.Level != Constants.AudiobookLevelSlug))
        {
            return ReleaseMatchResult.NoMatch;
        }

        var normalizedRelease = release.Title.NormalizeMediaFilename();
        var metadata = target.Metadata.GetValueOrDefault(book.Id);
        var titles =
            from title in (
                from text in metadata?.AlternateTexts ?? []
                where string.Equals(text.Kind, "Title", StringComparison.OrdinalIgnoreCase)
                select text.Text)
                .Prepend(metadata?.OriginalTitle)
                .Prepend(metadata?.Title)
            where !string.IsNullOrWhiteSpace(title)
            select title.Trim();
        var titleMatched = titles.Any(title =>
        {
            var normalizedTitle = title.NormalizeMediaFilename();
            return string.Equals(normalizedRelease, normalizedTitle, StringComparison.OrdinalIgnoreCase) ||
                normalizedRelease.StartsWith(
                    string.Concat(normalizedTitle, " "),
                    StringComparison.OrdinalIgnoreCase);
        });
        if (!titleMatched)
        {
            return !titles.Any()
                ? ReleaseMatchResult.Indeterminate
                : ReleaseMatchResult.NoMatch;
        }

        var volumeMatch = VolumePattern.Match(release.Title);
        if (book.Ordinal is { } volume)
        {
            if (!volumeMatch.Success)
            {
                return ReleaseMatchResult.Indeterminate;
            }

            if (!int.TryParse(
                volumeMatch.Groups[1].Value,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var parsedVolume) ||
                parsedVolume != volume)
            {
                return ReleaseMatchResult.NoMatch;
            }
        }

        var yearMatches = release.Title.FindYearTokens();
        if (metadata?.PublishDate?.Year is { } year)
        {
            if (yearMatches.Count == 0)
            {
                return ReleaseMatchResult.Indeterminate;
            }

            if (!int.TryParse(
                yearMatches[^1].Groups[1].Value,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var parsedYear) ||
                parsedYear != year)
            {
                return ReleaseMatchResult.NoMatch;
            }
        }

        return ReleaseMatchResult.Confident;
    }

    private static BookFormat ClassifyBookFormat(IEnumerable<string?> values)
    {
        var ebook = false;
        var audiobook = false;
        foreach (var value in values)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            ebook |= EbookFormatPattern.IsMatch(value);
            audiobook |= AudiobookFormatPattern.IsMatch(value);
        }

        return (ebook, audiobook) switch
        {
            (true, true) => BookFormat.Conflicting,
            (true, false) => BookFormat.Ebook,
            (false, true) => BookFormat.Audiobook,
            _ => BookFormat.None
        };
    }

    public IReadOnlyDictionary<string, IReadOnlyList<string>>? Parse(string filePath, out string? levelSlug)
    {
        levelSlug = filePath.ResolveBookLevelSlug();
        if (levelSlug is null)
        {
            return null;
        }

        var parsed = levelSlug == Constants.AudiobookLevelSlug
            ? ParseAudiobookFilename(filePath)
            : ParseBookFilename(filePath);
        if (string.IsNullOrWhiteSpace(parsed.Title))
        {
            return null;
        }

        return new SignalBagBuilder()
            .AddValue(ParsedFileSignalKeys.Title, parsed.Title)
            .AddValue(ParsedFileSignalKeys.Year, parsed.Year)
            .AddValue(AuthorSignal, parsed.Author)
            .AddValue(ParsedFileSignalKeys.TitleToken, parsed.Title)
            .Build();
    }

    private static ParsedBookFile ParseBookFilename(string filePath)
    {
        var parts = ParsePath(filePath);
        return parts.ParentYear is null ? FromStem(parts) : FromParent(parts);
    }

    private static ParsedBookFile ParseAudiobookFilename(string filePath)
    {
        var parts = ParsePath(filePath);
        if (parts.ParentYear is null &&
            (string.IsNullOrWhiteSpace(parts.ParentTitle) || !AudiobookPartPattern.IsMatch(parts.Stem)))
        {
            return FromStem(parts);
        }

        return FromParent(parts);
    }

    private static ParsedBookFile FromStem(ParsedPath parts) => new()
    {
        Author = null,
        Title = parts.StemTitle,
        Year = parts.StemYear
    };

    private static ParsedBookFile FromParent(ParsedPath parts) => new()
    {
        Author = parts.Author,
        Title = string.IsNullOrWhiteSpace(parts.ParentTitle) ? parts.StemTitle : parts.ParentTitle,
        Year = parts.ParentYear ?? parts.StemYear
    };

    private static ParsedPath ParsePath(string filePath)
    {
        var stem = (Path.GetFileNameWithoutExtension(filePath) ?? string.Empty).NormalizeMediaFilename();
        var (stemTitle, stemYear) = ExtractTitleAndYear(stem);
        var parent = (Path.GetFileName(Path.GetDirectoryName(filePath)) ?? string.Empty).NormalizeMediaFilename();
        var (parentTitle, parentYear) = ExtractTitleAndYear(parent);
        var grandparent = Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(filePath))) ?? string.Empty;
        return new ParsedPath(
            stem,
            stemTitle,
            stemYear,
            parentTitle,
            parentYear,
            string.IsNullOrEmpty(grandparent) ? null : grandparent.NormalizeMediaFilename());
    }

    private static (string Title, int? Year) ExtractTitleAndYear(string normalised)
    {
        if (normalised.Length == 0)
        {
            return (normalised, null);
        }

        var matches = normalised.FindYearTokens();
        if (matches.Count == 0)
        {
            return (normalised, null);
        }

        var last = matches[^1];
        var year = int.Parse(last.Groups[1].Value, CultureInfo.InvariantCulture);
        var title = normalised[..last.Index].Trim();
        return (string.IsNullOrEmpty(title) ? normalised : title, year);
    }

    public string? BuildPath(
        IReadOnlyList<ContentHierarchyItem> ancestors,
        IReadOnlyDictionary<Guid, IReadOnlyDictionary<string, IReadOnlyList<string>>> relationshipsByNode,
        string originalFileName,
        string extension)
    {
        var book = ancestors[^1];
        if (book.Level is not (Constants.EbookLevelSlug or Constants.AudiobookLevelSlug))
        {
            return null;
        }

        var bookTitle = book.Name.Trim();
        var fileName = string.IsNullOrWhiteSpace(bookTitle) ? originalFileName : bookTitle!;
        var bookYear = book.MetadataContainer?.Contributions.OrderBy(c => c.Ordinal)
            .Select(c => c.PublishDate?.Year).FirstOrDefault(year => year is not null);
        if (bookYear is { } year)
        {
            fileName += $" ({year.ToString(CultureInfo.InvariantCulture)})";
        }

        var relative = book.Level == Constants.AudiobookLevelSlug
            ? Path.Combine(fileName, WithExtension(originalFileName, extension))
            : WithExtension(fileName, extension);
        return relative;
    }

    private static string WithExtension(string name, string extension)
    {
        return string.IsNullOrEmpty(extension) ? name : name + "." + extension;
    }

    private sealed record ParsedPath(
        string Stem,
        string StemTitle,
        int? StemYear,
        string ParentTitle,
        int? ParentYear,
        string? Author);
}
