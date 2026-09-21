using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

using Magpie.Extensions.Music.Parsing;
using Magpie.Models;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Releases;
using Magpie.Models.Signals;
using Magpie.Sdk.Content;

namespace Magpie.Extensions.Music;

internal sealed class MusicContentTypeDefinitionProvider :
    IPathResolutionContentTypeDefinitionProvider,
    IReleaseMatchContentTypeDefinitionProvider
{
    private static readonly Regex SamplePattern = new(
        @"(?:^|[._-])sample(?:$|[._-])|\[sample\]",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex TrackPattern = new(
        @"(?<![A-Za-z0-9])(?:track|trk|t)[\s._-]*0*(?<track>\d{1,3})(?![A-Za-z0-9])",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly ContentTypeDefinition MusicDefinition = new()
    {
        Slug = ContentTypeSlug,
        DisplayName = "Music",
        PluralDisplayName = "Music",
        IconName = "music-note",
        Categories = ["music"],
        LevelDefinitions =
        [
            new ContentHierarchyLevelDefinition
            {
                Slug = ArtistSlug,
                ParentSlug = null,
                DisplayName = "Artist",
                IconName = "microphone",
                SupportedMetadataFields = ArtistMetadataFields,
                CustomMetadataFields = [],
                StandardFactFields =
                [
                    new CustomMetadataDefinition { Key = "country", DisplayName = "Country" }
                ],
                IsAcquirable = false,
                Subfolder = string.Empty,
                OrdinalKind = OrdinalKind.Name
            },
            new ContentHierarchyLevelDefinition
            {
                Slug = AlbumSlug,
                ParentSlug = ArtistSlug,
                DisplayName = "Album",
                IconName = "record",
                SupportedMetadataFields = AlbumMetadataFields,
                CustomMetadataFields = [],
                StandardFactFields =
                [
                    new() { Key = "trackCount", DisplayName = "Tracks", DataType = DataType.Number },
                    new() { Key = "format", DisplayName = "Format" }
                ],
                IsAcquirable = true,
                Subfolder = string.Empty,
                OrdinalKind = OrdinalKind.Name
            },
            new ContentHierarchyLevelDefinition
            {
                Slug = TrackSlug,
                ParentSlug = AlbumSlug,
                DisplayName = "Track",
                IconName = "music-note",
                SupportedMetadataFields = TrackMetadataFields,
                CustomMetadataFields = [],
                AcceptedFileTypes = ["audio/*"],
                AcceptedSidecarTypes = [".cue", ".log", ".m3u"],
                StandardFactFields =
                [
                    new CustomMetadataDefinition
                    {
                        Key = "duration",
                        DisplayName = "Duration",
                        DataType = DataType.Number,
                        Format = "duration"
                    }
                ],
                IsAcquirable = true,
                OrdinalKind = OrdinalKind.Number
            }
        ]
    };

    public const string ContentTypeSlug = "music";

    public const string ArtistSlug = "artist";

    public const string AlbumSlug = "album";

    public const string TrackSlug = "track";

    private const string ArtistSignal = "artist";

    private const string AlbumSignal = "album";

    private const MetadataField ArtistMetadataFields =
        MetadataField.Title |
        MetadataField.SortTitle |
        MetadataField.Overview |
        MetadataField.Established |
        MetadataField.Dissolved |
        MetadataField.BirthDate |
        MetadataField.DeathDate |
        MetadataField.ExternalIds |
        MetadataField.References |
        MetadataField.Images |
        MetadataField.AlternateTexts |
        MetadataField.Genres;

    private const MetadataField AlbumMetadataFields =
        MetadataField.Title |
        MetadataField.SortTitle |
        MetadataField.Overview |
        MetadataField.PublishDate |
        MetadataField.ExternalIds |
        MetadataField.References |
        MetadataField.Images |
        MetadataField.AlternateTexts |
        MetadataField.Genres;

    private const MetadataField TrackMetadataFields =
        MetadataField.Title |
        MetadataField.Overview |
        MetadataField.Duration |
        MetadataField.SequenceNumber |
        MetadataField.ExternalIds |
        MetadataField.Images;

    public ContentTypeDefinition Definition => MusicDefinition;

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

        var trackMatch = TrackPattern.Match(release.Title);
        if (target.Item.Level == TrackSlug &&
            target.Item.Ordinal is { } targetTrack &&
            trackMatch.Success &&
            int.TryParse(
                trackMatch.Groups["track"].Value,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var releaseTrack) &&
            releaseTrack != targetTrack)
        {
            return ReleaseMatchResult.NoMatch;
        }

        var normalizedRelease = " " + release.Title.NormalizeMediaFilename() + " ";
        var hadCompleteAlternative = false;
        foreach (var factSet in contentFacts)
        {
            var values = (
                from fact in factSet
                where !string.IsNullOrWhiteSpace(fact.Value)
                let value = fact.Value?.NormalizeMediaFilename()
                where value.Length > 0
                select value).ToArray();
            if (!values.Any() || factSet.Any(fact => fact.Facets.Count > 0))
            {
                continue;
            }

            hadCompleteAlternative = true;
            if (values.All(value => normalizedRelease.Contains(
                " " + value + " ",
                StringComparison.OrdinalIgnoreCase)))
            {
                return ReleaseMatchResult.Confident;
            }
        }

        var requiredItems = target.Item.Level switch
        {
            ArtistSlug => target.Lineage.Where(static item => item.Level == ArtistSlug),
            AlbumSlug => target.Lineage.Where(static item => item.Level is ArtistSlug or AlbumSlug),
            TrackSlug => target.Lineage.Where(static item => item.Level is ArtistSlug or TrackSlug),
            _ => [],
        };
        var requiredTitles = (
            from item in requiredItems
            let title = target.Metadata.TryGetValue(item.Id, out var metadata)
                ? metadata.Title
                : item.Name
            where !string.IsNullOrWhiteSpace(title)
            select title.NormalizeMediaFilename()).ToArray();
        if (!requiredTitles.Any())
        {
            return hadCompleteAlternative ? ReleaseMatchResult.NoMatch : ReleaseMatchResult.Indeterminate;
        }

        return requiredTitles.All(title => normalizedRelease.Contains(
            " " + title + " ",
            StringComparison.OrdinalIgnoreCase))
            ? ReleaseMatchResult.Confident
            : ReleaseMatchResult.NoMatch;
    }

    public IReadOnlyDictionary<string, IReadOnlyList<string>>? Parse(string filePath, out string? levelSlug)
    {
        levelSlug = null;
        if (SamplePattern.IsMatch(Path.GetFileNameWithoutExtension(filePath)))
        {
            return null;
        }

        var parsed = filePath.ParseMusicFilename();
        if (string.IsNullOrWhiteSpace(parsed.Album))
        {
            return null;
        }

        levelSlug = string.IsNullOrWhiteSpace(parsed.Artist) ? null : ArtistSlug;
        return new SignalBagBuilder()
                .AddValue(ParsedFileSignalKeys.Title, parsed.Album)
                .AddValue(ParsedFileSignalKeys.Year, parsed.Year)
                .AddValue(ArtistSignal, parsed.Artist)
                .AddValue(AlbumSignal, parsed.Album)
                .AddValue(ParsedFileSignalKeys.TitleToken, parsed.Album)
                .AddValue(ParsedFileSignalKeys.RootTitle, parsed.Artist)
                .Build();
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
        var artist = ancestors.FirstOrDefault(n => n.Level == ArtistSlug);
        if (artist is null || string.IsNullOrWhiteSpace(artist.Name))
        {
            return null;
        }

        var album = ancestors.FirstOrDefault(n => n.Level == AlbumSlug);
        if (album is null || string.IsNullOrWhiteSpace(album.Name))
        {
            return null;
        }

        var track = ancestors[^1].Level == TrackSlug ? ancestors[^1] : null;

        var albumTitle = album.Name.Trim();
        var albumYear = album.MetadataContainer?.Contributions.OrderBy(c => c.Ordinal)
            .Select(c => c.PublishDate?.Year).FirstOrDefault(year => year is not null);
        var albumFolder = albumYear is { } year
            ? $"{albumTitle} ({year.ToString(CultureInfo.InvariantCulture)})"
            : albumTitle;

        var artistFolder = artist.Name.Trim();
        var fileName = originalFileName;

        if (track?.Ordinal is { } trackNumber)
        {
            fileName = trackNumber.ToString("D2", CultureInfo.InvariantCulture);
            if (!string.IsNullOrWhiteSpace(track.Name))
            {
                fileName += " - " + track.Name.Trim();
            }
        }

        return new ContentPathPlan
        {
            Directories =
            [
                new ContentPathPlanDirectory { LevelSlug = ArtistSlug, Name = artistFolder },
                new ContentPathPlanDirectory { LevelSlug = AlbumSlug, Name = albumFolder }
            ],
            FileName = WithExtension(fileName, extension)
        };
    }

    private static string WithExtension(string name, string extension) =>
        string.IsNullOrEmpty(extension) ? name : name + "." + extension;
}
