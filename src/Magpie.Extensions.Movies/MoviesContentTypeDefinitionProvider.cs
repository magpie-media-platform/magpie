using System;
using System.Collections.Generic;
using System.Linq;

using Magpie.Extensions.Movies.Parsing;
using Magpie.Models;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Releases;
using Magpie.Models.Signals;
using Magpie.Sdk.Content;

namespace Magpie.Extensions.Movies;

internal sealed class MoviesContentTypeDefinitionProvider : IReleaseMatchContentTypeDefinitionProvider
{
    private static readonly ContentTypeDefinition MovieDefinition = new()
    {
        Slug = ContentTypeSlug,
        DisplayName = "Movie",
        PluralDisplayName = "Movies",
        IconName = "film-slate",
        Categories = ["movies"],
        LevelDefinitions =
        [
            new ContentHierarchyLevelDefinition
            {
                Slug = MovieSlug,
                ParentSlug = null,
                DisplayName = "Movie",
                IconName = "film-slate",
                SupportedMetadataFields = MovieMetadataFields,
                CustomMetadataFields = [],
                AcceptedFileTypes = ["video/*"],
                AcceptedSidecarTypes = [".srt", ".ass", ".vtt", ".sub", ".idx"],
                StandardFactFields =
                [
                    new CustomMetadataDefinition
                    {
                        Key = "runtime",
                        DisplayName = "Runtime",
                        DataType = DataType.Number,
                        Format = "duration"
                    },
                    new CustomMetadataDefinition { Key = "status", DisplayName = "Status" },
                    new CustomMetadataDefinition { Key = "certification", DisplayName = "Certification" },
                    new CustomMetadataDefinition { Key = "language", DisplayName = "Language" }
                ],
                IsAcquirable = true,
                Subfolder = string.Empty,
                OrdinalKind = OrdinalKind.None
            }
        ]
    };

    public const string ContentTypeSlug = "movie";

    public const string MovieSlug = "movie";

    private const MetadataField MovieMetadataFields =
        MetadataField.Title |
        MetadataField.OriginalTitle |
        MetadataField.SortTitle |
        MetadataField.Language |
        MetadataField.Overview |
        MetadataField.PublishDate |
        MetadataField.Duration |
        MetadataField.ProductionStatus |
        MetadataField.ExternalIds |
        MetadataField.References |
        MetadataField.Relationships |
        MetadataField.Images |
        MetadataField.Previews |
        MetadataField.AlternateTexts |
        MetadataField.ContentRatings |
        MetadataField.Awards |
        MetadataField.Genres;

    public ContentTypeDefinition Definition => MovieDefinition;

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

        var movie = target.Lineage.FirstOrDefault(static item => item.Level == MovieSlug);
        if (movie is null)
        {
            return ReleaseMatchResult.Indeterminate;
        }

        var parsed = release.Title.ParseMovieFilename();
        if (string.IsNullOrWhiteSpace(parsed.Title))
        {
            return ReleaseMatchResult.Indeterminate;
        }

        var parsedTitle = parsed.Title.NormalizeMediaFilename();
        var metadata = target.Metadata.GetValueOrDefault(movie.Id);
        var titles = (
            from title in (
                from text in metadata?.AlternateTexts ?? []
                where string.Equals(text.Kind, "Title", StringComparison.OrdinalIgnoreCase)
                select text.Text)
                .Prepend(metadata?.OriginalTitle)
                .Prepend(metadata?.Title)
            where !string.IsNullOrWhiteSpace(title)
            select title.Trim()).ToArray();
        if (!titles.Any())
        {
            return ReleaseMatchResult.Indeterminate;
        }

        var titleMatched = titles.Any(title => string.Equals(
            title.NormalizeMediaFilename(),
            parsedTitle,
            StringComparison.OrdinalIgnoreCase));
        if (!titleMatched && parsed.Year is null)
        {
            titleMatched = titles.Any(title =>
            {
                var normalizedTitle = title.NormalizeMediaFilename();
                return parsedTitle.StartsWith(
                    string.Concat(normalizedTitle, " "),
                    StringComparison.OrdinalIgnoreCase);
            });
        }

        if (!titleMatched)
        {
            return ReleaseMatchResult.NoMatch;
        }

        var targetYear = metadata?.PublishDate?.Year;
        if (targetYear is not null && parsed.Year is null)
        {
            return ReleaseMatchResult.Indeterminate;
        }

        return targetYear is not null && parsed.Year != targetYear
            ? ReleaseMatchResult.NoMatch
            : ReleaseMatchResult.Confident;
    }

    public IReadOnlyDictionary<string, IReadOnlyList<string>>? Parse(string filePath, out string? levelSlug)
    {
        levelSlug = null;
        if (ReleaseFiles.IsSample(filePath))
        {
            return null;
        }

        var parsed = filePath.ParseMovieFilename();
        if (string.IsNullOrWhiteSpace(parsed.Title))
        {
            return null;
        }

        return new SignalBagBuilder()
                .AddValue(ParsedFileSignalKeys.Title, parsed.Title)
                .AddValue(ParsedFileSignalKeys.Year, parsed.Year)
                .AddValue(ParsedFileSignalKeys.TitleToken, parsed.Title)
                .Build();
    }
}
