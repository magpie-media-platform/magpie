using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

using Magpie.Extensions.MusicBrainz.QueryEngine;
using Magpie.Models;
using Magpie.Models.Data;
using Magpie.Models.Filtering;
using Magpie.Models.MetadataSearch;
using Magpie.Sdk.Content;

namespace Magpie.Extensions.MusicBrainz.Mapping;

internal static class MappingExtensions
{
    private const string CountryFact = "country";
    private const string TrackCountFact = "trackCount";
    private const string FormatFact = "format";
    private const int PerformerRelationshipCap = 30;
    private const int CrewRelationshipCap = 20;
    private const int WriterRelationshipCap = 10;

    public static SearchHit? ToHit(this ArtistDto? artist)
    {
        if (!TryMbid(artist?.Id, out var mbid))
        {
            return null;
        }

        var externalIds = new List<ExternalIdentifier> { new() { Source = Constants.ProviderType, Value = mbid } };
        AddIdentifiers(externalIds, Constants.IsniSource, artist!.Isnis);
        AddIdentifiers(externalIds, Constants.IpiSource, artist.Ipis);
        return new SearchHit
        {
            Target = QueryTarget.Artist,
            PrimaryMbid = mbid,
            Title = artist.Name.NullIfBlank(),
            SortTitle = artist.SortName.NullIfBlank(),
            Disambiguation = artist.Disambiguation.NullIfBlank(),
            Aliases = artist.Aliases,
            ArtistType = artist.Type.NullIfBlank(),
            Established = ParseTemporal(artist.LifeSpan?.Begin),
            Dissolved = ParseTemporal(artist.LifeSpan?.End),
            BirthDate = ParseTemporal(artist.LifeSpan?.Begin),
            DeathDate = ParseTemporal(artist.LifeSpan?.End),
            Ended = artist.LifeSpan?.Ended,
            Genres = HitGenreNames(artist.Genres, artist.Tags),
            UrlRelations = artist.Relations,
            ExternalIds = externalIds
        };
    }

    public static SearchHit? ToHit(this ReleaseGroupDto? releaseGroup)
    {
        if (!TryMbid(releaseGroup?.Id, out var mbid))
        {
            return null;
        }

        var references = ArtistReferences(releaseGroup!.ArtistCredit);
        var representative = releaseGroup.SelectRepresentativeRelease();
        var externalIds = new List<ExternalIdentifier> { new() { Source = Constants.ProviderType, Value = mbid } };
        return new SearchHit
        {
            Target = QueryTarget.Album,
            PrimaryMbid = mbid,
            Title = releaseGroup.Title.NullIfBlank(),
            Disambiguation = releaseGroup.Disambiguation.NullIfBlank(),
            Aliases = releaseGroup.Aliases,
            ArtistCredit = ArtistCredit(releaseGroup.ArtistCredit),
            ArtistReferences = references,
            FirstReleaseDate = ParseTemporal(releaseGroup.FirstReleaseDate),
            RepresentativeLanguage = representative?.TextRepresentation?.Language.NullIfBlank(),
            Genres = HitGenreNames(releaseGroup.Genres, releaseGroup.Tags),
            UrlRelations = releaseGroup.Relations,
            ExternalIds = externalIds
        };
    }

    public static SearchHit? ToHit(this RecordingDto? recording)
    {
        if (!TryMbid(recording?.Id, out var mbid))
        {
            return null;
        }

        var references = ArtistReferences(recording!.ArtistCredit);
        var externalIds = new List<ExternalIdentifier> { new() { Source = Constants.ProviderType, Value = mbid } };
        AddIdentifiers(externalIds, Constants.IsrcSource, recording.Isrcs);
        return new SearchHit
        {
            Target = QueryTarget.Track,
            PrimaryMbid = mbid,
            Title = recording.Title.NullIfBlank(),
            Disambiguation = recording.Disambiguation.NullIfBlank(),
            Aliases = recording.Aliases,
            ArtistCredit = ArtistCredit(recording.ArtistCredit),
            ArtistReferences = references,
            FirstReleaseDate = ParseTemporal(recording.FirstReleaseDate),
            Duration = Duration(recording.Length),
            Genres = HitGenreNames(recording.Genres, recording.Tags),
            UrlRelations = recording.Relations,
            ExternalIds = externalIds
        };
    }

    public static SearchHit? ToHit(this ReleaseDto? release)
    {
        var hit = release?.ReleaseGroup.ToHit();
        return hit is null || release!.LabelInfo is null
            ? hit
            : hit with { LabelReferences = release.LabelReferences() };
    }

    public static MetadataSearchCandidate ToCandidate(this SearchHit hit, Uri? thumbnailUrl = null)
    {
        var level = Level(hit.Target);
        var source = PrimarySource(hit.Target);
        var references = hit.ArtistReferences
            .Where(reference => reference.CreditName is not null || reference.CanonicalName is not null)
            .Select(reference => new ProviderMetadataSearchSubjectReference
            {
                SubjectKind = ArtistSubjectKind(reference),
                DisplayName = reference.CreditName ?? reference.CanonicalName!,
                RelationshipKind = Constants.ArtistRelationshipKind,
                ExternalIds = reference.Mbid is null
                    ? new Dictionary<string, string>()
                    : new Dictionary<string, string> { [Constants.ProviderType] = reference.Mbid }
            })
            .Concat((hit.LabelReferences ?? [])
            .Where(reference => reference.Name is not null)
            .Select(reference => new ProviderMetadataSearchSubjectReference
            {
                SubjectKind = Constants.LabelSubjectKind,
                DisplayName = reference.Name!,
                RelationshipKind = Constants.LabelSubjectKind,
                ExternalIds = reference.Mbid is null
                    ? new Dictionary<string, string>()
                    : new Dictionary<string, string> { [Constants.ProviderType] = reference.Mbid }
            }))
            .ToArray();
        return new MetadataSearchCandidate
        {
            ProviderType = Constants.ProviderType,
            ExternalIdSource = source,
            ExternalId = hit.PrimaryMbid,
            ContentType = Constants.ContentTypeSlug,
            Level = level,
            Title = hit.Title,
            Language = hit.RepresentativeLanguage,
            Year = hit.FirstReleaseDate?.Value.Year,
            ReleaseDate = hit.FirstReleaseDate?.Value,
            Overview = hit.Disambiguation.NullIfBlank() ?? hit.ArtistCredit,
            ThumbnailUrl = thumbnailUrl,
            SubjectReferences = references
        };
    }

    public static MetadataContribution ArtistContribution(this ArtistDto artist, DateTime utcNow)
    {
        var mbid = NormalizeMbid(artist.Id);
        var begin = ParseTemporal(artist.LifeSpan?.Begin)?.Value;
        var end = ParseTemporal(artist.LifeSpan?.End)?.Value;
        var personArtist = IsPersonArtist(artist.Type);
        var contribution = new MetadataContribution
        {
            ProviderType = Constants.ProviderType,
            Source = Source(QueryTarget.Artist, mbid ?? string.Empty),
            Title = artist.Name.NullIfBlank(),
            SortTitle = artist.SortName.NullIfBlank(),
            Overview = artist.Disambiguation.NullIfBlank(),
            Established = personArtist ? null : begin,
            Dissolved = personArtist ? null : end,
            BirthDate = personArtist ? begin : null,
            DeathDate = personArtist ? end : null,
            DateLastRefreshed = utcNow
        };
        AddFact(contribution, CountryFact, NormalizeCountry(artist.Country));
        AddExternalId(contribution, Constants.ProviderType, mbid);
        AddExternalIds(contribution, Constants.IsniSource, artist.Isnis);
        AddExternalIds(contribution, Constants.IpiSource, artist.Ipis);
        AddAliases(contribution, artist.Aliases);
        AddGenres(contribution, artist.Genres);
        AddReferences(contribution, artist.Relations);
        return contribution;
    }

    public static MetadataContribution InitialReleaseGroupContribution(this ReleaseGroupDto releaseGroup)
    {
        var mbid = NormalizeMbid(releaseGroup.Id);
        var contribution = new MetadataContribution
        {
            ProviderType = Constants.ProviderType,
            Source = Source(QueryTarget.Album, mbid ?? string.Empty),
            Title = releaseGroup.Title.NullIfBlank(),
            Overview = releaseGroup.Disambiguation.NullIfBlank(),
            PublishDate = ParseTemporal(releaseGroup.FirstReleaseDate)?.Value
        };
        AddExternalId(contribution, Constants.ProviderType, mbid);
        AddAliases(contribution, releaseGroup.Aliases);
        AddGenres(contribution, releaseGroup.Genres);
        AddReferences(contribution, releaseGroup.Relations);
        return contribution;
    }

    public static MetadataContribution AlbumContribution(
        this ReleaseGroupDto releaseGroup,
        ReleaseDto? release,
        Uri? frontCoverUrl,
        DateTime utcNow)
    {
        var contribution = releaseGroup.InitialReleaseGroupContribution();
        contribution.DateLastRefreshed = utcNow;
        contribution.Language = release?.TextRepresentation?.Language.NullIfBlank();

        var trackCount = release?.Media?.Sum(medium => medium?.TrackCount ?? medium?.Tracks?.Count ?? 0) ?? 0;
        if (trackCount > 0)
        {
            contribution.CustomMetadata.Add(new CustomMetadataValue
            {
                Key = TrackCountFact,
                Value = trackCount.ToString(CultureInfo.InvariantCulture)
            });
        }

        var formats = release?.Media?
            .Select(medium => medium?.Format.NullIfBlank())
            .Where(format => format is not null)
            .Select(format => format!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray() ??
            [];
        if (formats.Length > 0)
        {
            contribution.CustomMetadata.Add(new CustomMetadataValue
            {
                Key = FormatFact,
                Value = string.Join(", ", formats)
            });
        }

        if (frontCoverUrl is not null)
        {
            contribution.Images.Add(new Image
            {
                Type = Constants.CoverImageType,
                Url = frontCoverUrl,
                Source = Constants.ProviderType,
                Ordinal = 0,
                IsPrimary = true
            });
        }

        return contribution;
    }

    public static MetadataContribution RecordingContribution(this RecordingDto recording, DateTime utcNow)
    {
        var mbid = NormalizeMbid(recording.Id);
        var contribution = new MetadataContribution
        {
            ProviderType = Constants.ProviderType,
            Source = Source(QueryTarget.Track, mbid ?? string.Empty),
            Title = recording.Title.NullIfBlank(),
            Overview = recording.Disambiguation.NullIfBlank(),
            PublishDate = ParseTemporal(recording.FirstReleaseDate)?.Value,
            Duration = Duration(recording.Length),
            DateLastRefreshed = utcNow
        };
        AddExternalId(contribution, Constants.ProviderType, mbid);
        AddExternalIds(contribution, Constants.IsrcSource, recording.Isrcs);
        AddAliases(contribution, recording.Aliases);
        AddGenres(contribution, recording.Genres);
        AddReferences(contribution, recording.Relations);
        return contribution;
    }

    public static IReadOnlyList<ChildHierarchyDefinition> ReleaseGroupChildren(
        this IReadOnlyList<ReleaseGroupDto> releaseGroups)
    {
        var children = new List<ChildHierarchyDefinition>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var releaseGroup in releaseGroups)
        {
            if (!TryMbid(releaseGroup.Id, out var mbid) || releaseGroup.Title.NullIfBlank() is not { } title ||
                !seen.Add(mbid))
            {
                continue;
            }

            children.Add(new ChildHierarchyDefinition
            {
                Level = Constants.AlbumLevelSlug,
                Name = title,
                Slug = mbid,
                AlignmentKey = "release-group:" + mbid,
                ExternalIds = new Dictionary<string, string> { [Constants.ProviderType] = mbid },
                InitialContribution = releaseGroup.InitialReleaseGroupContribution()
            });
        }

        return children;
    }

    public static IReadOnlyList<ChildHierarchyDefinition> TrackChildren(this ReleaseDto? release)
    {
        var children = new List<ChildHierarchyDefinition>();
        var sequence = 0;
        foreach (var medium in release?.Media?.OrderBy(value => value?.Position ?? int.MaxValue).AsEnumerable() ?? [])
        {
            foreach (var track in medium?.Tracks?.OrderBy(value => value?.Position ?? int.MaxValue).AsEnumerable()
                ?? [])
            {
                sequence++;
                if (!TryMbid(track?.Id, out var trackMbid) || !TryMbid(track?.Recording?.Id, out _) ||
                    (track?.Title.NullIfBlank() ?? track?.Recording?.Title.NullIfBlank()) is not { } title)
                {
                    continue;
                }

                var contribution = TrackAppearanceContribution(track!, sequence, trackMbid);
                var externalIds = new Dictionary<string, string> { [Constants.ProviderType] = trackMbid };
                var firstIsrc = track!.Recording?.Isrcs
                    ?.Select(NormalizeIsrc)
                    .FirstOrDefault(value => value is not null);
                if (firstIsrc is not null)
                {
                    externalIds[Constants.IsrcSource] = firstIsrc;
                }

                children.Add(new ChildHierarchyDefinition
                {
                    Level = Constants.TrackLevelSlug,
                    Name = title,
                    Slug = trackMbid,
                    AlignmentKey = "track:" + trackMbid,
                    Ordinal = sequence,
                    ExternalIds = externalIds,
                    InitialContribution = contribution
                });
            }
        }

        return children;
    }

    public static IReadOnlyList<LabelReference> LabelReferences(this ReleaseDto? release)
    {
        var references = new List<LabelReference>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var info in release?.LabelInfo ?? [])
        {
            var mbid = NormalizeMbid(info?.Label?.Id);
            var name = info?.Label?.Name.NullIfBlank();
            if ((mbid is null && name is null) || !seen.Add(mbid ?? name!))
            {
                continue;
            }

            references.Add(new LabelReference { Mbid = mbid, Name = name });
        }

        return references;
    }

    public static IReadOnlyList<SubjectRelationshipDefinition> LabelSubjectRelationships(
        this ReleaseDto? release,
        DateTime utcNow,
        IReadOnlyDictionary<string, LabelDto>? enrichedLabels = null)
    {
        var result = new List<SubjectRelationshipDefinition>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var info in release?.LabelInfo ?? [])
        {
            var mbid = NormalizeMbid(info?.Label?.Id);
            var label = mbid is not null && enrichedLabels?.TryGetValue(mbid, out var enrichedLabel) == true
                    ? enrichedLabel
                    : info?.Label;
            var name = label?.Name.NullIfBlank() ?? info?.Label?.Name.NullIfBlank();
            if (name is null || !seen.Add(mbid ?? name))
            {
                continue;
            }

            var contribution = LabelContribution(label, mbid, name, utcNow);
            result.Add(new SubjectRelationshipDefinition
            {
                RelationshipKind = Constants.LabelSubjectKind,
                RelationshipQualifier = info?.CatalogNumber.NullIfBlank(),
                RelationshipOrdinal = result.Count,
                SubjectKind = Constants.LabelSubjectKind,
                SubjectName = name,
                SubjectExternalIds = mbid is null
                    ? new Dictionary<string, string>()
                    : new Dictionary<string, string> { [Constants.ProviderType] = mbid },
                InitialSubjectContribution = contribution
            });
        }

        return result;
    }

    public static IReadOnlyList<SubjectRelationshipDefinition> AlbumSubjectRelationships(
        this ReleaseGroupDto releaseGroup,
        ReleaseDto? release,
        DateTime utcNow,
        IReadOnlyDictionary<string, LabelDto>? enrichedLabels = null)
    {
        var result = release.LabelSubjectRelationships(utcNow, enrichedLabels).ToList();
        AddPendingRelationships(
            result,
            PerformerRelationships(releaseGroup.ArtistCredit, []),
            PerformerRelationshipCap,
            utcNow);
        if (release is null)
        {
            return result;
        }

        var recordings = ReleaseRecordings(release);
        var artistRelations = (release.Relations ?? [])
            .Concat(recordings.SelectMany(recording => recording.Relations ?? []))
            .Concat(WorkArtistRelations(recordings));
        AddPendingRelationships(result, CrewRelationships(artistRelations), CrewRelationshipCap, utcNow);
        AddPendingRelationships(result, WriterRelationships(recordings), WriterRelationshipCap, utcNow);
        return result;
    }

    public static IReadOnlyList<SubjectRelationshipDefinition> TrackSubjectRelationships(
        this RecordingDto recording,
        DateTime utcNow)
    {
        var result = new List<SubjectRelationshipDefinition>();
        AddPendingRelationships(
            result,
            PerformerRelationships(recording.ArtistCredit, recording.Relations ?? []),
            PerformerRelationshipCap,
            utcNow);
        AddPendingRelationships(result, WriterRelationships([recording]), WriterRelationshipCap, utcNow);
        return result;
    }

    public static string? FindExternalId(this IEnumerable<MetadataContribution> contributions, string source)
    {
        var fallback = default(string);
        foreach (var contribution in contributions)
        {
            foreach (var externalId in contribution.ExternalIds)
            {
                if (!string.Equals(externalId.Source, source, StringComparison.OrdinalIgnoreCase) ||
                    !Guid.TryParse(externalId.Value, out var parsed))
                {
                    continue;
                }

                var value = parsed.ToString("D");
                if (contribution.ProviderType == Constants.ProviderType)
                {
                    return value;
                }

                fallback ??= value;
            }
        }

        return fallback;
    }

    public static ReleaseDto? SelectRepresentativeRelease(this ReleaseGroupDto releaseGroup)
    {
        return releaseGroup.Releases?
            .Where(release => TryMbid(release?.Id, out _))
            .OrderByDescending(
                release => string.Equals(release!.Status, "Official", StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(release => ParseTemporal(release!.Date) is not null)
            .ThenBy(release => ParseTemporal(release!.Date)?.Value ?? DateTime.MaxValue)
            .ThenBy(release => release!.Id, StringComparer.Ordinal)
            .FirstOrDefault();
    }

    public static ReleaseDto? SelectDurationRelease(this ReleaseGroupDto releaseGroup)
    {
        return releaseGroup.Releases?
            .Where(release => TryMbid(release?.Id, out _))
            .OrderByDescending(
                release => string.Equals(release!.Status, "Official", StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(release => ParseTemporal(release!.Date) is not null)
            .ThenBy(release => ParseTemporal(release!.Date)?.Value.Year ?? int.MaxValue)
            .ThenByDescending(release => ParseTemporal(release!.Date)?.Precision)
            .ThenBy(release => ParseTemporal(release!.Date)?.Value ?? DateTime.MaxValue)
            .ThenByDescending(release => ReleaseTrackCount(release!) is > 0)
            .ThenByDescending(release => ReleaseTrackCount(release!) ?? 0)
            .ThenBy(release => release!.Id, StringComparer.Ordinal)
            .FirstOrDefault();
    }

    private static int? ReleaseTrackCount(ReleaseDto release)
    {
        var counts = (release.Media ?? [])
            .Where(medium => medium?.TrackCount is > 0)
            .Select(medium => medium!.TrackCount!.Value)
            .ToArray();
        return counts.Length == 0 ? null : counts.Sum();
    }

    private static MetadataContribution TrackAppearanceContribution(TrackDto track, int sequence, string trackMbid)
    {
        var trackArtistCredit = ArtistCredit(track.ArtistCredit);
        var recordingArtistCredit = ArtistCredit(track.Recording?.ArtistCredit);
        var contribution = new MetadataContribution
        {
            ProviderType = Constants.ProviderType,
            Source = "musicbrainz:track:" + trackMbid,
            Title = track.Title.NullIfBlank() ?? track.Recording?.Title.NullIfBlank(),
            Overview = string.Equals(trackArtistCredit, recordingArtistCredit, StringComparison.Ordinal)
                ? null
                : trackArtistCredit,
            SequenceNumber = sequence,
            Duration = Duration(track.Length ?? track.Recording?.Length)
        };
        AddExternalId(contribution, Constants.ProviderType, trackMbid);
        AddExternalIds(contribution, Constants.IsrcSource, track.Recording?.Isrcs);
        return contribution;
    }

    private static MetadataContribution LabelContribution(
        LabelDto? label,
        string? mbid,
        string fallbackName,
        DateTime utcNow)
    {
        var contribution = new MetadataContribution
        {
            ProviderType = Constants.ProviderType,
            Source = "musicbrainz:label:" + (mbid ?? fallbackName),
            Title = label?.Name.NullIfBlank() ?? fallbackName,
            SortTitle = label?.SortName.NullIfBlank(),
            Overview = label?.Disambiguation.NullIfBlank(),
            Established = ParseTemporal(label?.LifeSpan?.Begin)?.Value,
            Dissolved = ParseTemporal(label?.LifeSpan?.End)?.Value,
            DateLastRefreshed = utcNow
        };
        AddExternalId(contribution, Constants.ProviderType, mbid);
        AddExternalId(
            contribution,
            Constants.IfpiSource,
            label?.LabelCode?.ToString(CultureInfo.InvariantCulture));
        AddExternalIds(contribution, Constants.IsniSource, label?.Isnis);
        AddExternalIds(contribution, Constants.IpiSource, label?.Ipis);
        AddReferences(contribution, label?.Relations);
        return contribution;
    }

    private static IReadOnlyList<ArtistReference> ArtistReferences(IReadOnlyList<ArtistCreditDto>? credits)
    {
        return credits
            ?.Select(credit => new ArtistReference
            {
                Mbid = NormalizeMbid(credit.Artist?.Id),
                CanonicalName = credit.Artist?.Name.NullIfBlank(),
                CreditName = credit.Name.NullIfBlank() ?? credit.Artist?.Name.NullIfBlank(),
                ArtistType = credit.Artist?.Type.NullIfBlank()
            })
            .Where(reference =>
                reference.Mbid is not null ||
                reference.CanonicalName is not null ||
                reference.CreditName is not null)
            .ToArray() ??
            [];
    }

    private static string ArtistSubjectKind(ArtistReference reference)
    {
        return string.Equals(
            reference.ArtistType,
            Constants.PersonArtistType,
            StringComparison.OrdinalIgnoreCase)
                ? Constants.PersonSubjectKind
                : Constants.ArtistLevelSlug;
    }

    private static string? ArtistCredit(IReadOnlyList<ArtistCreditDto>? credits)
    {
        if (credits is null || credits.Count == 0)
        {
            return null;
        }

        var value = string.Concat(credits.Select(credit =>
            (credit.Name.NullIfBlank() ?? credit.Artist?.Name.NullIfBlank() ?? string.Empty) + credit.JoinPhrase));
        return value.NullIfBlank();
    }

    private static IReadOnlyList<RecordingDto> ReleaseRecordings(ReleaseDto release)
    {
        return release.Media?
                .Where(medium => medium is not null)
                .OrderBy(medium => medium!.Position ?? int.MaxValue)
                .SelectMany(medium => medium!.Tracks ?? [])
                .Where(track => track?.Recording is not null)
                .OrderBy(track => track!.Position ?? int.MaxValue)
                .Select(track => track!.Recording!)
                .ToArray() ??
            [];
    }

    private static IReadOnlyList<PendingRelationship> PerformerRelationships(
        IReadOnlyList<ArtistCreditDto>? credits,
        IEnumerable<RelationDto> relations)
    {
        var relationArtists = relations
            .Where(IsPerformerRelation)
            .Select(relation => RelationPerson(
                relation,
                Constants.PerformerRelationshipKind,
                RelationQualifier(relation)))
            .Where(person => person is not null)
            .Select(person => person!)
            .GroupBy(person => person.Mbid, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => new PendingRelationship
                {
                    Mbid = group.Key,
                    Name = group.Select(person => person.Name).First(),
                    SortName = group.Select(person => person.SortName).FirstOrDefault(value => value is not null),
                    RelationshipKind = Constants.PerformerRelationshipKind,
                    Qualifier = JoinQualifiers(group.Select(person => person.Qualifier))
                },
                StringComparer.OrdinalIgnoreCase);
        var result = new List<PendingRelationship>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var credit in credits ?? [])
        {
            var person = CreditPerson(credit);
            if (person is null || !seen.Add(person.Mbid))
            {
                continue;
            }

            result.Add(relationArtists.TryGetValue(person.Mbid, out var related)
                ? related with { Name = person.Name, SortName = person.SortName ?? related.SortName }
                : person);
        }

        result.AddRange(
            relationArtists.Values
                .Where(person => seen.Add(person.Mbid))
                .OrderBy(person => person.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(person => person.Mbid, StringComparer.Ordinal));
        return result;
    }

    private static IReadOnlyList<PendingRelationship> CrewRelationships(
        IEnumerable<RelationDto> relations)
    {
        return [.. relations
            .Select(
                relation => RelationPerson(relation, Constants.CrewRelationshipKind, CrewRole(relation.Type)))
            .Where(person => person is not null)
            .Select(person => person!)
            .GroupBy(person => string.Concat(person.Mbid, "\0", person.Qualifier), StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(person => CrewRoleOrder(person.Qualifier))
            .ThenBy(person => person.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(person => person.Mbid, StringComparer.Ordinal)];
    }

    private static IReadOnlyList<PendingRelationship> WriterRelationships(
        IEnumerable<RecordingDto> recordings)
    {
        return [.. WorkArtistRelations(recordings)
            .Select(relation => RelationPerson(
                relation,
                Constants.WriterRelationshipKind,
                WriterRole(relation.Type)))
            .Where(person => person is not null)
            .Select(person => person!)
            .GroupBy(person => string.Concat(person.Mbid, "\0", person.Qualifier), StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(person => WriterRoleOrder(person.Qualifier))
            .ThenBy(person => person.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(person => person.Mbid, StringComparer.Ordinal)];
    }

    private static IEnumerable<RelationDto> WorkArtistRelations(
        IEnumerable<RecordingDto> recordings)
    {
        return recordings
            .SelectMany(recording => recording.Relations ?? [])
            .Where(relation => relation.Work is not null &&
                string.Equals(
                    relation.TargetType,
                    Constants.WorkRelationTarget,
                    StringComparison.OrdinalIgnoreCase))
            .SelectMany(relation => relation.Work!.Relations ?? []);
    }

    private static PendingRelationship? CreditPerson(ArtistCreditDto? credit)
    {
        var artist = credit?.Artist;
        if (artist is null || !TryMbid(artist.Id, out var mbid) ||
            (artist.Name.NullIfBlank() ?? credit?.Name.NullIfBlank()) is not { } name)
        {
            return null;
        }

        return new PendingRelationship
        {
            Mbid = mbid,
            Name = name,
            SortName = artist.SortName.NullIfBlank(),
            RelationshipKind = Constants.PerformerRelationshipKind
        };
    }

    private static PendingRelationship? RelationPerson(
        RelationDto relation,
        string relationshipKind,
        string? qualifier)
    {
        var artist = relation.Artist;
        if ((qualifier is null &&
            !string.Equals(
                relationshipKind,
                Constants.PerformerRelationshipKind,
                StringComparison.Ordinal)) ||
            !string.Equals(
                relation.TargetType,
                Constants.ArtistRelationTarget,
                StringComparison.OrdinalIgnoreCase) ||
            artist is null ||
            !TryMbid(artist.Id, out var mbid) ||
            artist.Name.NullIfBlank() is not { } name)
        {
            return null;
        }

        return new PendingRelationship
        {
            Mbid = mbid,
            Name = name,
            SortName = artist.SortName.NullIfBlank(),
            RelationshipKind = relationshipKind,
            Qualifier = qualifier
        };
    }

    private static bool IsPerformerRelation(RelationDto relation)
    {
        return relation.Type is not null &&
            (string.Equals(
                relation.Type,
                Constants.InstrumentRelationshipType,
                StringComparison.OrdinalIgnoreCase) ||
                string.Equals(
                    relation.Type,
                    Constants.VocalRelationshipType,
                    StringComparison.OrdinalIgnoreCase) ||
                string.Equals(
                    relation.Type,
                    Constants.PerformerRelationshipType,
                    StringComparison.OrdinalIgnoreCase) ||
                string.Equals(
                    relation.Type,
                    Constants.PerformingOrchestraRelationshipType,
                    StringComparison.OrdinalIgnoreCase));
    }

    private static string? RelationQualifier(RelationDto relation)
    {
        var credited = QualifierValues(relation.AttributeCredits?.Values);
        if (credited.Count > 0)
        {
            return string.Join(", ", credited);
        }

        var attributes = QualifierValues(relation.Attributes);
        if (attributes.Count > 0)
        {
            return string.Join(", ", attributes);
        }

        var values = QualifierValues(relation.AttributeValues?.Values);
        if (values.Count > 0)
        {
            return string.Join(", ", values);
        }

        return string.Equals(
            relation.Type,
            Constants.VocalRelationshipType,
            StringComparison.OrdinalIgnoreCase)
                ? Constants.VocalsRole
                : null;
    }

    private static IReadOnlyList<string> QualifierValues(IEnumerable<string>? values)
    {
        return values?
            .Select(value => value.NullIfBlank())
            .Where(value => value is not null && !IsPerformerModifier(value))
            .Select(value => value!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray() ??
            [];
    }

    private static bool IsPerformerModifier(string value)
    {
        return value.Equals("additional", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("guest", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("solo", StringComparison.OrdinalIgnoreCase);
    }

    private static string? JoinQualifiers(IEnumerable<string?> qualifiers)
    {
        var values = qualifiers
            .Where(qualifier => qualifier is not null)
            .SelectMany(qualifier => qualifier!.Split(", ", StringSplitOptions.RemoveEmptyEntries))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return values.Length > 0 ? string.Join(", ", values) : null;
    }

    private static string? CrewRole(string? type)
    {
        return type?.ToLowerInvariant() switch
        {
            Constants.ProducerRelationshipType => Constants.ProducerRole,
            Constants.EngineerRelationshipType
                or Constants.AudioRelationshipType
                or Constants.AudioEngineerRelationshipType
                or Constants.SoundRelationshipType
                or Constants.SoundEngineerRelationshipType
                or Constants.RecordingRelationshipType
                or Constants.RecordingEngineerRelationshipType
                or Constants.FieldRecordistRelationshipType
                or Constants.MasteringRelationshipType
                or Constants.MasteringEngineerRelationshipType
                or Constants.BalanceRelationshipType => Constants.EngineerRole,
            Constants.MixRelationshipType or Constants.MixerRelationshipType =>
                Constants.MixerRole,
            Constants.ArrangerRelationshipType
                or Constants.InstrumentArrangerRelationshipType
                or Constants.VocalArrangerRelationshipType
                or Constants.OrchestratorRelationshipType => Constants.ArrangerRole,
            _ => null
        };
    }

    private static string? WriterRole(string? type)
    {
        return type?.ToLowerInvariant() switch
        {
            Constants.ComposerRelationshipType => Constants.ComposerRole,
            Constants.LyricistRelationshipType => Constants.LyricistRole,
            Constants.WriterRelationshipType => Constants.WriterRole,
            _ => null
        };
    }

    private static int CrewRoleOrder(string? role)
    {
        return role switch
        {
            Constants.ProducerRole => 0,
            Constants.EngineerRole => 1,
            Constants.MixerRole => 2,
            Constants.ArrangerRole => 3,
            _ => int.MaxValue
        };
    }

    private static int WriterRoleOrder(string? role)
    {
        return role switch
        {
            Constants.ComposerRole => 0,
            Constants.LyricistRole => 1,
            Constants.WriterRole => 2,
            _ => int.MaxValue
        };
    }

    private static void AddPendingRelationships(
        ICollection<SubjectRelationshipDefinition> result,
        IEnumerable<PendingRelationship> pending,
        int cap,
        DateTime utcNow)
    {
        foreach (var relationship in pending.Take(cap))
        {
            var contribution = new MetadataContribution
            {
                ProviderType = Constants.ProviderType,
                Source = Source(QueryTarget.Artist, relationship.Mbid),
                Title = relationship.Name,
                SortTitle = relationship.SortName,
                DateLastRefreshed = utcNow
            };
            AddExternalId(contribution, Constants.ProviderType, relationship.Mbid);
            result.Add(new SubjectRelationshipDefinition
            {
                RelationshipKind = relationship.RelationshipKind,
                RelationshipQualifier = relationship.Qualifier,
                RelationshipOrdinal = result.Count,
                SubjectKind = Constants.PersonSubjectKind,
                SubjectName = relationship.Name,
                SubjectExternalIds = new Dictionary<string, string> { [Constants.ProviderType] = relationship.Mbid },
                InitialSubjectContribution = contribution
            });
        }
    }

    private static void AddAliases(MetadataContribution contribution, IReadOnlyList<AliasDto>? aliases)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var ordinal = 0;
        foreach (var alias in aliases ?? [])
        {
            var name = alias.Name.NullIfBlank();
            var locale = alias.Locale.NullIfBlank();
            if (name is null || !seen.Add(name + "\0" + locale))
            {
                continue;
            }

            contribution.AlternateTexts.Add(new AlternateText
            {
                Kind = Constants.AlternateTextKind,
                Text = name,
                Language = locale,
                Ordinal = ordinal++
            });
        }
    }

    private static void AddGenres(MetadataContribution contribution, IReadOnlyList<GenreDto>? genres)
    {
        var ordinal = 0;
        foreach (var name in GenreNames(genres))
        {
            contribution.Genres.Add(new Genre { Name = name, Ordinal = ordinal++ });
        }
    }

    private static IReadOnlyList<string> GenreNames(IReadOnlyList<GenreDto>? genres)
    {
        return genres?
                .Where(genre => genre.Count > 0 && !string.IsNullOrWhiteSpace(genre.Name))
                .OrderByDescending(genre => genre.Count)
                .ThenBy(genre => genre.Name, StringComparer.OrdinalIgnoreCase)
                .Select(genre => genre.Name!.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray() ??
            [];
    }

    private static IReadOnlyList<string>? HitGenreNames(IReadOnlyList<GenreDto>? genres, IReadOnlyList<GenreDto>? tags)
    {
        var available = tags ?? genres;
        return available is null ? null : GenreNames(available);
    }

    private static void AddReferences(MetadataContribution contribution, IReadOnlyList<RelationDto>? relations)
    {
        var ordinal = 0;
        foreach (var reference in (relations ?? []).Project())
        {
            contribution.References.Add(new Reference
            {
                Kind = reference.Kind,
                Source = Constants.ProviderType,
                Url = reference.Url,
                DisplayName = reference.Kind,
                Ordinal = ordinal++
            });
        }
    }

    private static void AddFact(MetadataContribution contribution, string key, string? value)
    {
        if (value is not null)
        {
            contribution.CustomMetadata.Add(new CustomMetadataValue { Key = key, Value = value });
        }
    }

    private static void AddExternalIds(MetadataContribution contribution, string source, IReadOnlyList<string>? values)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var value in values ?? [])
        {
            var normalized = source == Constants.IsrcSource ? NormalizeIsrc(value) : value.NullIfBlank();
            if (normalized is not null && seen.Add(normalized))
            {
                AddExternalId(contribution, source, normalized);
            }
        }
    }

    private static void AddIdentifiers(List<ExternalIdentifier> result, string source, IReadOnlyList<string>? values)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var value in values ?? [])
        {
            var normalized = source == Constants.IsrcSource ? NormalizeIsrc(value) : value.NullIfBlank();
            if (normalized is not null && seen.Add(normalized))
            {
                result.Add(new ExternalIdentifier { Source = source, Value = normalized });
            }
        }
    }

    private static void AddExternalId(MetadataContribution contribution, string source, string? value)
    {
        if (value is null)
        {
            return;
        }

        contribution.ExternalIds.Add(new ExternalId
        {
            Source = source,
            Value = value,
            Ordinal = contribution.ExternalIds.Count
        });
    }

    private static TemporalValue? ParseTemporal(string? value)
    {
        return TemporalValue.TryParse(value, out var temporal) ? temporal : null;
    }

    private static TimeSpan? Duration(long? milliseconds)
    {
        return milliseconds is > 0 ? TimeSpan.FromMilliseconds(milliseconds.Value) : null;
    }

    private static string? NormalizeMbid(string? value)
    {
        return TryMbid(value, out var mbid) ? mbid : null;
    }

    private static string? NormalizeCountry(string? value) => value.NullIfBlank()?.ToUpperInvariant();

    private static bool IsPersonArtist(string? type)
    {
        return string.Equals(type.NullIfBlank(), "Person", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryMbid(string? value, out string mbid)
    {
        mbid = string.Empty;
        if (!Guid.TryParse(value, out var parsed))
        {
            return false;
        }

        mbid = parsed.ToString("D", CultureInfo.InvariantCulture);
        return true;
    }

    private static string? NormalizeIsrc(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var isrc = new string([.. value.Where(character => character != '-' && !char.IsWhiteSpace(character))])
            .ToUpperInvariant();
        return isrc.Length == 12 && isrc.All(char.IsLetterOrDigit) ? isrc : null;
    }

    private static string Level(QueryTarget target)
    {
        return target switch
        {
            QueryTarget.Artist => Constants.ArtistLevelSlug,
            QueryTarget.Album => Constants.AlbumLevelSlug,
            QueryTarget.Track => Constants.TrackLevelSlug,
            _ => throw new InvalidOperationException("target")
        };
    }

    private static string PrimarySource(QueryTarget target)
    {
        return target switch
        {
            QueryTarget.Artist => Constants.ProviderType,
            QueryTarget.Album => Constants.ProviderType,
            QueryTarget.Track => Constants.ProviderType,
            _ => throw new InvalidOperationException("target")
        };
    }

    private static string Source(QueryTarget target, string mbid)
    {
        return target switch
        {
            QueryTarget.Artist => "musicbrainz:artist:" + mbid,
            QueryTarget.Album => "musicbrainz:release-group:" + mbid,
            QueryTarget.Track => "musicbrainz:recording:" + mbid,
            _ => throw new InvalidOperationException("target")
        };
    }

    private sealed record PendingRelationship
    {
        public required string Mbid { get; init; }

        public required string Name { get; init; }

        public string? SortName { get; init; }

        public required string RelationshipKind { get; init; }

        public string? Qualifier { get; init; }
    }
}
