using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

using Magpie.Extensions.Tv.Parsing;
using Magpie.Models;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Releases;
using Magpie.Models.Signals;
using Magpie.Sdk.Content;

using Microsoft.Extensions.Logging;

namespace Magpie.Extensions.Tv;

internal sealed class TvContentTypeDefinitionProvider(ILogger<TvContentTypeDefinitionProvider> logger)
    :
        IPathResolutionContentTypeDefinitionProvider,
        IFileRoutingContentTypeDefinitionProvider,
        IReleaseMatchContentTypeDefinitionProvider
{
    private static readonly ContentTypeDefinition TvDefinition = new()
    {
        Slug = Constants.ContentTypeSlug,
        DisplayName = "TV",
        PluralDisplayName = "TV Shows",
        IconName = "television",
        Categories = ["tv"],
        LevelDefinitions =
        [
            new ContentHierarchyLevelDefinition
            {
                    Slug = Constants.ShowLevelSlug,
                ParentSlug = null,
                DisplayName = "Show",
                IconName = "television",
                SupportedMetadataFields = ShowMetadataFields,
                CustomMetadataFields = [],
                StandardFactFields =
                [
                    new CustomMetadataDefinition { Key = "status", DisplayName = "Status" },
                    new CustomMetadataDefinition { Key = "certification", DisplayName = "Certification" },
                    new CustomMetadataDefinition { Key = "language", DisplayName = "Language" }
                ],
                IsAcquirable = true,
                Subfolder = string.Empty,
                OrdinalKind = OrdinalKind.Name
            },
            new ContentHierarchyLevelDefinition
            {
                    Slug = Constants.SeasonLevelSlug,
                ParentSlug = Constants.ShowLevelSlug,
                DisplayName = "Season",
                IconName = "queue",
                SupportedMetadataFields = SeasonMetadataFields,
                CustomMetadataFields = [],
                IsAcquirable = true,
                OrdinalKind = OrdinalKind.Number
            },
            new ContentHierarchyLevelDefinition
            {
                    Slug = Constants.EpisodeLevelSlug,
                ParentSlug = Constants.SeasonLevelSlug,
                DisplayName = "Episode",
                IconName = "television-simple",
                SupportedMetadataFields = EpisodeMetadataFields,
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
                    new CustomMetadataDefinition { Key = "certification", DisplayName = "Certification" }
                ],
                IsAcquirable = true,
                OrdinalKind = OrdinalKind.Number
            }
        ]
    };

    private const MetadataField ShowMetadataFields =
        MetadataField.Title |
        MetadataField.OriginalTitle |
        MetadataField.SortTitle |
        MetadataField.Language |
        MetadataField.Overview |
        MetadataField.PublishDate |
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

    private const MetadataField SeasonMetadataFields =
        MetadataField.Title |
        MetadataField.Overview |
        MetadataField.PublishDate |
        MetadataField.SequenceNumber |
        MetadataField.ExternalIds |
        MetadataField.Images;

    private const MetadataField EpisodeMetadataFields =
        MetadataField.Title |
        MetadataField.OriginalTitle |
        MetadataField.SortTitle |
        MetadataField.Overview |
        MetadataField.PublishDate |
        MetadataField.Duration |
        MetadataField.SequenceNumber |
        MetadataField.ExternalIds |
        MetadataField.Images |
        MetadataField.AlternateTexts |
        MetadataField.ContentRatings;

    private const string SeasonSignal = "season";
    private const string EpisodeSignal = "episode";

    private static readonly TimeSpan PublishDateTimeZoneSlack = TimeSpan.FromDays(1);

    public ContentTypeDefinition Definition => TvDefinition;

    /// <inheritdoc />
    public IReadOnlyList<Guid> ResolveFileChildren(
        ContentHierarchyItem parent,
        IReadOnlyList<ContentHierarchyItem> children,
        IReadOnlyDictionary<string, List<string>> signals)
    {
        var (signal, childLevel) = parent.Level switch
        {
            Constants.ShowLevelSlug => (SeasonSignal, Constants.SeasonLevelSlug),
            Constants.SeasonLevelSlug => (EpisodeSignal, Constants.EpisodeLevelSlug),
            _ => (string.Empty, string.Empty)
        };
        if (!signals.TryGetValue(signal, out var values))
        {
            return [];
        }

        var resolved = new List<Guid>();
        foreach (var value in values)
        {
            if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var sequenceNumber))
            {
                return [];
            }

            var matches = children
                .Where(child => child.Level == childLevel && SequenceNumberOf(child) == sequenceNumber)
                .ToArray();
            if (matches.Length != 1)
            {
                return [];
            }

            resolved.Add(matches[0].Id);
        }

        return resolved;
    }

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

        var show = target.Lineage.FirstOrDefault(static item => item.Level == Constants.ShowLevelSlug);
        if (show is null)
        {
            return ReleaseMatchResult.Indeterminate;
        }

        var parsed = release.Title.ParseTvReleaseTitle();
        var metadata = target.Metadata.GetValueOrDefault(show.Id);
        var showTitles = (
            from title in (
                from text in metadata?.AlternateTexts ?? []
                where string.Equals(text.Kind, "Title", StringComparison.OrdinalIgnoreCase)
                select text.Text)
                .Prepend(metadata?.OriginalTitle)
                .Prepend(metadata?.Title)
            where !string.IsNullOrWhiteSpace(title)
            select title.Trim()).ToArray();
        var releaseMatchesShow = release.Scope.Any(scope =>
            scope.Status == ReleaseScopeStatus.Matched &&
            target.Lineage.Any(item => item.Id == scope.ContentHierarchyItemId));
        var anchorLength = parsed.TitleTokens.Count == 0 && releaseMatchesShow ? 0 : -1;
        var anchorTitle = string.Empty;
        foreach (var title in showTitles)
        {
            var titleTokens = title.TokeniseName();
            if (titleTokens.Count == 0 || titleTokens.Count > parsed.TitleTokens.Count)
            {
                continue;
            }

            var matches = true;
            for (var index = 0; index < titleTokens.Count; index++)
            {
                if (!string.Equals(
                    titleTokens[index],
                    parsed.TitleTokens[index],
                    StringComparison.OrdinalIgnoreCase))
                {
                    matches = false;
                    break;
                }
            }

            if (matches && titleTokens.Count > anchorLength)
            {
                anchorLength = titleTokens.Count;
                anchorTitle = title;
            }
        }

        if (anchorLength < 0)
        {
            return !showTitles.Any()
                ? ReleaseMatchResult.Indeterminate
                : ReleaseMatchResult.NoMatch;
        }

        var hasMarker = parsed.MarkerStart is not null;
        var anchorOnly = parsed.TitleTokens.Count == anchorLength;
        var markerFollowsAnchor = parsed.TitleTokens
            .Skip(anchorLength)
            .All(IsIgnorableInterveningToken);

        int? seriesPackMarker = null;
        for (var index = anchorLength; index < parsed.TitleTokens.Count - 1; index++)
        {
            if (string.Equals(parsed.TitleTokens[index], "complete", StringComparison.OrdinalIgnoreCase) &&
                (string.Equals(parsed.TitleTokens[index + 1], "series", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(parsed.TitleTokens[index + 1], "collection", StringComparison.OrdinalIgnoreCase)))
            {
                seriesPackMarker = index;
                break;
            }
        }

        var isSeriesPack = !hasMarker &&
            seriesPackMarker is { } packMarker &&
            parsed.TitleTokens
                .Skip(anchorLength)
                .Take(packMarker - anchorLength)
                .All(IsIgnorableInterveningToken);
        var hasAmbiguousTitle = hasMarker
            ? !markerFollowsAnchor
            : seriesPackMarker is not null ? !isSeriesPack : !anchorOnly;

        if (!hasAmbiguousTitle && metadata?.PublishDate?.Year is { } targetYear)
        {
            var releaseTitlePrefix = parsed.MarkerStart is { } markerStart
                ? release.Title[..markerStart]
                : release.Title;
            var releaseYears = TvParsingPatterns.Year
                .Matches(releaseTitlePrefix)
                .Select(static match => match.Value)
                .ToList();
            foreach (Match titleYear in TvParsingPatterns.Year.Matches(anchorTitle))
            {
                releaseYears.Remove(titleYear.Value);
            }

            var targetYearText = targetYear.ToString(CultureInfo.InvariantCulture);
            if (releaseYears.Count > 0 && !releaseYears.Contains(targetYearText))
            {
                return ReleaseMatchResult.NoMatch;
            }
        }

        if (target.Item.Level == Constants.ShowLevelSlug)
        {
            return !hasAmbiguousTitle && (hasMarker || isSeriesPack || anchorOnly)
                ? ReleaseMatchResult.Confident
                : ReleaseMatchResult.Indeterminate;
        }

        var season = target.Lineage.FirstOrDefault(static item => item.Level == Constants.SeasonLevelSlug);
        if (SequenceNumberOf(season) is not { } seasonNumber)
        {
            return ReleaseMatchResult.Indeterminate;
        }

        if (isSeriesPack)
        {
            return target.Item.Level != Constants.EpisodeLevelSlug
                ? ReleaseMatchResult.Confident
                : IsPublishedBy(target, release) switch
                {
                    true => ReleaseMatchResult.Confident,
                    false => ReleaseMatchResult.NoMatch,
                    null => ReleaseMatchResult.Indeterminate,
                };
        }

        if (parsed.Seasons.Count == 0)
        {
            if (target.Item.Level == Constants.EpisodeLevelSlug)
            {
                var releaseTitleTokens = release.Title.TokeniseName();
                var factValueCount = 0;
                var allFactValuesMatched = true;
                foreach (var releaseFact in releaseFacts)
                {
                    if (string.IsNullOrWhiteSpace(releaseFact.Value))
                    {
                        allFactValuesMatched = false;
                        break;
                    }

                    factValueCount++;
                    var factTokens = releaseFact.Value.TokeniseName();
                    var factMatched = false;
                    for (var start = 0; start <= releaseTitleTokens.Count - factTokens.Count; start++)
                    {
                        var matches = factTokens.Count > 0;
                        for (var index = 0; index < factTokens.Count; index++)
                        {
                            if (!string.Equals(
                                factTokens[index],
                                releaseTitleTokens[start + index],
                                StringComparison.OrdinalIgnoreCase))
                            {
                                matches = false;
                                break;
                            }
                        }

                        if (matches)
                        {
                            factMatched = true;
                            break;
                        }
                    }

                    if (!factMatched)
                    {
                        allFactValuesMatched = false;
                        break;
                    }
                }

                if (factValueCount >= 2 && allFactValuesMatched)
                {
                    return ReleaseMatchResult.Confident;
                }
            }

            return ReleaseMatchResult.Indeterminate;
        }

        if (!parsed.Seasons.Contains(seasonNumber))
        {
            return ReleaseMatchResult.NoMatch;
        }

        if (target.Item.Level == Constants.SeasonLevelSlug)
        {
            return hasAmbiguousTitle
                ? ReleaseMatchResult.Indeterminate
                : ReleaseMatchResult.Confident;
        }

        if (target.Item.Level != Constants.EpisodeLevelSlug ||
            SequenceNumberOf(target.Item) is not { } episodeNumber)
        {
            return ReleaseMatchResult.Indeterminate;
        }

        var included = parsed.Episodes.Count > 0
            ? parsed.Episodes.Contains(episodeNumber)
            : IsPublishedBy(target, release);
        return included switch
        {
            false => ReleaseMatchResult.NoMatch,
            null => ReleaseMatchResult.Indeterminate,
            true => hasAmbiguousTitle ? ReleaseMatchResult.Indeterminate : ReleaseMatchResult.Confident,
        };
    }

    private static bool? IsPublishedBy(ContentTarget target, Release release)
    {
        if (target.Metadata.GetValueOrDefault(target.Item.Id)?.PublishDate is not { } published || published == default)
        {
            return null;
        }

        return release.PublishDate == default ||
            published.Date <= release.PublishDate.Date + PublishDateTimeZoneSlack;
    }

    private static bool IsIgnorableInterveningToken(string token) =>
        token is "complete" or "the" ||
        string.Equals(TvParsingPatterns.Year.Match(token).Value, token, StringComparison.Ordinal);

    public IReadOnlyDictionary<string, IReadOnlyList<string>>? Parse(string filePath, out string? levelSlug)
    {
        levelSlug = null;
        if (ReleaseFiles.IsSample(filePath))
        {
            return null;
        }

        var parsed = filePath.ParseTvFilename(out var reason);
        if (parsed is null)
        {
            logger.LogDebug(
                "The TV filename parser produced no query for {FilePath}: {Reason}.",
                filePath,
                reason ?? TvParsingMessages.NoSeasonEpisodeReason);
            return null;
        }

        if (string.IsNullOrWhiteSpace(parsed.Title))
        {
            return null;
        }

        var title = parsed.Year is { } year
            ? parsed.Title + " " + year.ToString(CultureInfo.InvariantCulture)
            : parsed.Title;

        levelSlug = parsed.Season is not null && parsed.Episodes.Count > 0 ? Constants.ShowLevelSlug : null;
        var signals = new SignalBagBuilder()
                .AddValue(ParsedFileSignalKeys.Title, parsed.Title)
                .AddValue(ParsedFileSignalKeys.Year, parsed.Year)
                .AddValue(SeasonSignal, parsed.Season)
                .AddValue(ParsedFileSignalKeys.TitleToken, title)
                .AddValue(ParsedFileSignalKeys.RootTitle, title);
        foreach (var episode in parsed.Episodes)
        {
            signals.AddValue(EpisodeSignal, episode);
        }

        return signals.Build();
    }

    public string? BuildPath(
        IReadOnlyList<ContentHierarchyItem> ancestors,
        IReadOnlyDictionary<Guid, IReadOnlyDictionary<string, IReadOnlyList<string>>> relationshipsByNode,
        string originalFileName,
        string extension)
    {
        var plan = BuildPathPlan(ancestors, relationshipsByNode, originalFileName, extension);
        return plan is null
            ? null
            : string.Join("/", plan.Directories.Select(directory => directory.Name).Append(plan.FileName));
    }

    public ContentPathPlan? BuildPathPlan(
        IReadOnlyList<ContentHierarchyItem> ancestors,
        IReadOnlyDictionary<Guid, IReadOnlyDictionary<string, IReadOnlyList<string>>> relationshipsByNode,
        string originalFileName,
        string extension)
    {
        var show = ancestors.FirstOrDefault(n => n.Level == Constants.ShowLevelSlug);
        if (show is null || string.IsNullOrWhiteSpace(show.Name))
        {
            return null;
        }

        var season = ancestors.FirstOrDefault(n => n.Level == Constants.SeasonLevelSlug);
        var episode = ancestors[^1].Level == Constants.EpisodeLevelSlug ? ancestors[^1] : null;

        var showTitle = show.Name.Trim();
        var showYear = show.MetadataContainer?.Contributions.OrderBy(c => c.Ordinal)
            .Select(c => c.PublishDate?.Year).FirstOrDefault(year => year is not null);
        var showFolder = showYear is { } year
            ? $"{showTitle} ({year.ToString(CultureInfo.InvariantCulture)})"
            : showTitle;
        var fileName = originalFileName;

        if (SequenceNumberOf(season) is { } seasonNumber)
        {
            if (episode is not null && SequenceNumberOf(episode) is { } episodeNumber)
            {
                fileName = $"{showTitle} - " +
                    $"S{seasonNumber.ToString("D2", CultureInfo.InvariantCulture)}" +
                    $"E{episodeNumber.ToString("D2", CultureInfo.InvariantCulture)}";
                if (!string.IsNullOrWhiteSpace(episode.Name))
                {
                    fileName += $" - {episode.Name.Trim()}";
                }
            }
        }

        return new ContentPathPlan
        {
            Directories = [new ContentPathPlanDirectory { LevelSlug = Constants.ShowLevelSlug, Name = showFolder }],
            FileName = WithExtension(fileName, extension)
        };
    }

    private static string WithExtension(string name, string extension) =>
        string.IsNullOrEmpty(extension) ? name : name + "." + extension;

    private static int? SequenceNumberOf(ContentHierarchyItem? item) =>
        item?.MetadataContainer?.Contributions.OrderBy(contribution => contribution.Ordinal)
            .Select(contribution => contribution.SequenceNumber).FirstOrDefault(number => number is not null);
}
