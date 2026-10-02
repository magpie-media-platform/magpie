using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

using Magpie.Models;
using Magpie.Models.Data;
using Magpie.Models.MetadataSearch;
using Magpie.Sdk.Content;

namespace Magpie.Extensions.Tvmaze.Mapping;

internal static class MappingExtensions
{
    private const string SeasonLevelSlug = "season";

    private const string EpisodeLevelSlug = "episode";

    private static readonly IReadOnlyDictionary<string, string> TypeMap =
        Constants.ShowImageTypes.ToDictionary(type => type, StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> WriterTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "Writer",
        "Screenplay",
        "Story"
    };

    public static void AddShowFacts(this MetadataContribution contribution, ShowDto show)
    {
        if (show.Rating?.Average is double rating)
        {
            contribution.AddFact("rating", rating.ToString(CultureInfo.InvariantCulture));
        }

        contribution.AddFact("type", show.Type.NullIfBlank()?.Trim());
        contribution.AddFact("schedule", show.Schedule.ToDisplayText());
    }

    public static void AddPersonFacts(this MetadataContribution contribution, PersonDto person)
    {
        contribution.AddFact("gender", person.Gender.NullIfBlank()?.Trim().ToLowerInvariant());
        contribution.AddCountryFact(person.Country);
    }

    public static void AddCountryFact(this MetadataContribution contribution, CountryDto? country)
    {
        contribution.AddFact("country", country?.Code.NullIfBlank()?.Trim().ToUpperInvariant());
    }

    private static void AddFact(this MetadataContribution contribution, string key, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        contribution.CustomMetadata.Add(new CustomMetadataValue { Key = key, Value = value });
    }

    private static string? ToDisplayText(this ScheduleDto? schedule)
    {
        var days = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var day in schedule?.Days ?? [])
        {
            var value = day.NullIfBlank()?.Trim();
            if (value is not null && seen.Add(value))
            {
                days.Add(value);
            }
        }

        var joinedDays = days.Count == 0 ? null : string.Join(", ", days);
        var time = schedule?.Time.NullIfBlank()?.Trim();
        return (joinedDays, time) switch
        {
            (not null, not null) => joinedDays + " at " + time,
            (not null, null) => joinedDays,
            (null, not null) => time,
            _ => null
        };
    }

    public static DateTime? ToDateOnly(this string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return DateTime.TryParseExact(
            value.Trim(),
            "yyyy-MM-dd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var parsed)
            ? DateTime.SpecifyKind(parsed.Date, DateTimeKind.Utc)
            : null;
    }

    public static IReadOnlyList<ChildHierarchyDefinition> BuildSeasonDefinitions(
        this IReadOnlyList<SeasonDto> seasons,
        IReadOnlyList<EpisodeDto> episodes)
    {
        var episodesBySeason = episodes.GroupBySeason();

        var result = new List<ChildHierarchyDefinition>();
        var seenSeasonNumbers = new HashSet<int>();
        foreach (var season in seasons.OrderBy(candidate => candidate.Number ?? int.MaxValue))
        {
            if (season?.Number is not int seasonNumber)
            {
                continue;
            }

            seenSeasonNumbers.Add(seasonNumber);
            var seasonEpisodes = episodesBySeason.TryGetValue(seasonNumber, out var grouped) ? grouped : [];
            result.Add(season.ToSeasonDefinition(seasonNumber, seasonEpisodes));
        }

        foreach (var pair in episodesBySeason.OrderBy(entry => entry.Key))
        {
            if (seenSeasonNumbers.Contains(pair.Key))
            {
                continue;
            }

            result.Add(((SeasonDto?)null).ToSeasonDefinition(pair.Key, pair.Value));
        }

        return result;
    }

    private static ChildHierarchyDefinition ToSeasonDefinition(
        this SeasonDto? season,
        int seasonNumber,
        IReadOnlyList<EpisodeDto> seasonEpisodes)
    {
        var name = string.IsNullOrWhiteSpace(season?.Name)
            ? string.Format(CultureInfo.InvariantCulture, "Season {0}", seasonNumber)
            : season.Name;
        var slug = string.Format(CultureInfo.InvariantCulture, "season-{0}", seasonNumber);

        var episodeDefinitions = new List<ChildHierarchyDefinition>(seasonEpisodes.Count);
        foreach (var episode in seasonEpisodes)
        {
            var definition = episode.ToEpisodeDefinition(seasonNumber);
            if (definition is not null)
            {
                episodeDefinitions.Add(definition);
            }
        }

        var externalIds = new Dictionary<string, string>();
        if (season is { Id: > 0 })
        {
            externalIds[Constants.ProviderType] = season.Id.ToString(CultureInfo.InvariantCulture);
        }

        return new ChildHierarchyDefinition
        {
            Level = SeasonLevelSlug,
            Name = name,
            Slug = slug,
            Ordinal = seasonNumber,
            AlignmentKey = string.Format(CultureInfo.InvariantCulture, "season:{0}", seasonNumber),
            ExternalIds = externalIds,
            InitialContribution = season.ToSeasonContribution(seasonNumber),
            Children = episodeDefinitions
        };
    }

    private static ChildHierarchyDefinition? ToEpisodeDefinition(this EpisodeDto? episode, int seasonNumber)
    {
        if (episode is null || episode.Id <= 0 || episode.Number is not int episodeNumber)
        {
            return null;
        }

        var name = string.IsNullOrWhiteSpace(episode.Name)
            ? string.Format(CultureInfo.InvariantCulture, "Episode {0}", episodeNumber)
            : episode.Name;
        var slug = string.Format(CultureInfo.InvariantCulture, "s{0:D2}e{1:D2}", seasonNumber, episodeNumber);

        return new ChildHierarchyDefinition
        {
            Level = EpisodeLevelSlug,
            Name = name,
            Slug = slug,
            Ordinal = episodeNumber,
            AlignmentKey = string.Format(CultureInfo.InvariantCulture, "episode:{0}:{1}", seasonNumber, episodeNumber),
            ExternalIds = new Dictionary<string, string>
            {
                [Constants.ProviderType] = episode.Id.ToString(CultureInfo.InvariantCulture)
            },
            InitialContribution = episode.ToEpisodeContribution(dateLastRefreshed: null)
        };
    }

    public static MetadataContribution? ToEpisodeContribution(this EpisodeDto? episode, DateTime? dateLastRefreshed)
    {
        if (episode is null || episode.Id <= 0)
        {
            return null;
        }

        var idString = episode.Id.ToString(CultureInfo.InvariantCulture);
        var contribution = new MetadataContribution
        {
            ProviderType = Constants.ProviderType,
            Source = string.Format(CultureInfo.InvariantCulture, "tvmaze:episode:{0}", idString),
            Title = episode.Name.NullIfBlank(),
            Overview = episode.Summary.NormalizeHtmlToText(),
            PublishDate = episode.AirDate.ToDateOnly(),
            SequenceNumber = episode.Number,
            DateLastRefreshed = dateLastRefreshed
        };
        contribution.ExternalIds.Add(new ExternalId
        {
            Source = Constants.ProviderType,
            Value = idString,
            Ordinal = 0
        });
        if (episode.Runtime is int runtimeMinutes && runtimeMinutes > 0)
        {
            contribution.Duration = TimeSpan.FromMinutes(runtimeMinutes);
        }

        var still = episode.Image.ToStill(ordinal: 0);
        if (still is not null)
        {
            contribution.Images.Add(still);
        }

        return contribution;
    }

    private static MetadataContribution ToSeasonContribution(this SeasonDto? season, int seasonNumber)
    {
        var idString = season is { Id: > 0 }
            ? season.Id.ToString(CultureInfo.InvariantCulture)
            : seasonNumber.ToString(CultureInfo.InvariantCulture);
        var contribution = new MetadataContribution
        {
            ProviderType = Constants.ProviderType,
            Source = string.Format(CultureInfo.InvariantCulture, "tvmaze:season:{0}", idString),
            Title = season?.Name.NullIfBlank(),
            Overview = season?.Summary.NormalizeHtmlToText(),
            PublishDate = season?.PremiereDate.ToDateOnly(),
            Dissolved = season?.EndDate.ToDateOnly(),
            SequenceNumber = seasonNumber
        };
        if (season is { Id: > 0 })
        {
            contribution.ExternalIds.Add(new ExternalId
            {
                Source = Constants.ProviderType,
                Value = idString,
                Ordinal = 0
            });
        }

        var poster = season?.Image.ToPoster(ordinal: 0);
        if (poster is not null)
        {
            contribution.Images.Add(poster);
        }

        return contribution;
    }

    private static Dictionary<int, List<EpisodeDto>> GroupBySeason(this IReadOnlyList<EpisodeDto> episodes)
    {
        var grouped = new Dictionary<int, List<EpisodeDto>>();
        foreach (var episode in episodes)
        {
            if (episode?.Season is not int seasonNumber)
            {
                continue;
            }

            if (!grouped.TryGetValue(seasonNumber, out var list))
            {
                list = [];
                grouped[seasonNumber] = list;
            }

            list.Add(episode);
        }

        foreach (var list in grouped.Values)
        {
            list.Sort((left, right) => (left.Number ?? int.MaxValue).CompareTo(right.Number ?? int.MaxValue));
        }

        return grouped;
    }

    public static void AddShowImages(
        this MetadataContribution contribution,
        IReadOnlyList<ImageDto> images,
        ImageReferenceDto? fallbackPoster)
    {
        foreach (var image in images.ToShowImages(fallbackPoster))
        {
            contribution.Images.Add(image);
        }
    }

    public static IReadOnlyList<Image> ToShowImages(
        this IReadOnlyList<ImageDto> images,
        ImageReferenceDto? fallbackPoster)
    {
        var mappedImages = new List<Image>();
        var ordinal = 0;
        var hasPoster = false;
        foreach (var image in images)
        {
            if (image is null ||
                string.IsNullOrWhiteSpace(image.Type) ||
                !TypeMap.TryGetValue(image.Type, out var mappedType))
            {
                continue;
            }

            var resolution = image.Resolutions?.Original ?? image.Resolutions?.Medium;
            var url = TryUri(resolution?.Url);
            if (url is null)
            {
                continue;
            }

            mappedImages.Add(new Image
            {
                Type = mappedType,
                MimeType = url.ToMimeType(),
                Url = url,
                Source = Constants.ProviderType,
                Ordinal = ordinal++,
                IsPrimary = image.Main,
                Width = resolution?.Width,
                Height = resolution?.Height
            });
            hasPoster |= mappedType == "poster";
        }

        if (!hasPoster)
        {
            var fallback = fallbackPoster.ToPoster(ordinal);
            if (fallback is not null)
            {
                mappedImages.Add(fallback);
            }
        }

        return mappedImages;
    }

    public static Image? ToProfileImage(this ImageReferenceDto? reference)
    {
        var url = TryUri(reference?.Original) ?? TryUri(reference?.Medium);
        if (url is null)
        {
            return null;
        }

        return new Image
        {
            Type = "profile",
            MimeType = url.ToMimeType(),
            Url = url,
            Source = Constants.ProviderType,
            Ordinal = 0
        };
    }

    public static Image? ToPoster(this ImageReferenceDto? reference, int ordinal)
    {
        return reference.ToImage("poster", ordinal);
    }

    public static Image? ToStill(this ImageReferenceDto? reference, int ordinal)
    {
        return reference.ToImage("still", ordinal);
    }

    private static Image? ToImage(this ImageReferenceDto? reference, string type, int ordinal)
    {
        var url = TryUri(reference?.Original) ?? TryUri(reference?.Medium);
        if (url is null)
        {
            return null;
        }

        return new Image
        {
            Type = type,
            MimeType = url.ToMimeType(),
            Url = url,
            Source = Constants.ProviderType,
            Ordinal = ordinal
        };
    }

    private static string? ToMimeType(this Uri url)
    {
        return url.AbsolutePath.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
            url.AbsolutePath.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase)
                ? "image/jpeg"
                : url.AbsolutePath.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
                    ? "image/png"
                    : url.AbsolutePath.EndsWith(".webp", StringComparison.OrdinalIgnoreCase)
                        ? "image/webp"
                        : null;
    }

    public static IReadOnlyList<SubjectRelationshipDefinition> ToSubjectRelationships(
        this ShowDto show,
        DateTime utcNow)
    {
        var result = new List<SubjectRelationshipDefinition>();
        (show.Embedded?.Cast).AppendCastRelationships(RelationshipKinds.Cast, utcNow, result);
        (show.Embedded?.Crew)?.Select(entry => (entry?.Person, entry?.Type))
            .AppendCrewRelationships(utcNow, result);
        show.Network.AppendNetworkRelationship("tvmaze:network:", utcNow, result);
        show.WebChannel.AppendNetworkRelationship("tvmaze:webchannel:", utcNow, result);
        return result;
    }

    public static IReadOnlyList<SubjectRelationshipDefinition> ToGuestSubjectRelationships(
        this IReadOnlyList<CastEntryDto>? cast,
        IReadOnlyList<GuestCrewEntryDto>? crew,
        DateTime utcNow)
    {
        var result = new List<SubjectRelationshipDefinition>();
        cast.AppendCastRelationships(RelationshipKinds.GuestCast, utcNow, result);
        crew?.Select(entry => (entry?.Person, entry?.Type)).AppendCrewRelationships(utcNow, result);
        return result;
    }

    private static void AppendCastRelationships(
        this IReadOnlyList<CastEntryDto>? cast,
        string relationshipKind,
        DateTime utcNow,
        List<SubjectRelationshipDefinition> result)
    {
        if (cast is null || cast.Count == 0)
        {
            return;
        }

        var emitted = 0;
        foreach (var entry in cast)
        {
            if (emitted >= Constants.CastCap)
            {
                break;
            }

            if (entry?.Person is not { } person || person.Id <= 0 || string.IsNullOrWhiteSpace(person.Name))
            {
                continue;
            }

            result.Add(new SubjectRelationshipDefinition
            {
                RelationshipKind = relationshipKind,
                RelationshipQualifier = (entry.Character?.Name).ToCastQualifier(entry.Self, entry.Voice),
                RelationshipOrdinal = result.Count,
                SubjectKind = RelationshipKinds.PersonSubject,
                SubjectName = person.Name.Trim(),
                SubjectExternalIds = person.Id.ToPersonExternalIds(),
                InitialSubjectContribution = person.ToContribution(utcNow)
            });
            emitted++;
        }
    }

    private static void AppendCrewRelationships(
        this IEnumerable<(PersonDto? Person, string? Type)> crew,
        DateTime utcNow,
        List<SubjectRelationshipDefinition> result)
    {
        var emitted = 0;
        foreach (var entry in crew)
        {
            if (emitted >= Constants.CrewCap)
            {
                break;
            }

            var person = entry.Person;
            if (person is null || person.Id <= 0 || string.IsNullOrWhiteSpace(person.Name))
            {
                continue;
            }

            result.Add(new SubjectRelationshipDefinition
            {
                RelationshipKind = entry.Type.ToRelationshipKind(),
                RelationshipQualifier = entry.Type.NullIfBlank(),
                RelationshipOrdinal = result.Count,
                SubjectKind = RelationshipKinds.PersonSubject,
                SubjectName = person.Name.Trim(),
                SubjectExternalIds = person.Id.ToPersonExternalIds(),
                InitialSubjectContribution = person.ToContribution(utcNow)
            });
            emitted++;
        }
    }

    private static void AppendNetworkRelationship(
        this NetworkDto? network,
        string sourcePrefix,
        DateTime utcNow,
        List<SubjectRelationshipDefinition> result)
    {
        if (network is null || network.Id <= 0 || string.IsNullOrWhiteSpace(network.Name))
        {
            return;
        }

        var idString = network.Id.ToString(CultureInfo.InvariantCulture);
        var contribution = new MetadataContribution
        {
            ProviderType = Constants.ProviderType,
            Source = sourcePrefix + idString,
            Title = network.Name.Trim(),
            DateLastRefreshed = utcNow
        };
        contribution.ExternalIds.Add(new ExternalId
        {
            Source = Constants.ProviderType,
            Value = idString,
            Ordinal = 0
        });
        contribution.AddCountryFact(network.Country);

        if (TryUri(network.OfficialSite) is { } officialSite)
        {
            contribution.References.Add(new Reference
            {
                Kind = "official",
                Source = Constants.ProviderType,
                Url = officialSite,
                DisplayName = "Official site",
                Ordinal = 0
            });
        }

        result.Add(new SubjectRelationshipDefinition
        {
            RelationshipKind = RelationshipKinds.Network,
            RelationshipOrdinal = result.Count,
            SubjectKind = RelationshipKinds.StudioSubject,
            SubjectName = network.Name.Trim(),
            SubjectExternalIds = new Dictionary<string, string> { [Constants.ProviderType] = idString },
            InitialSubjectContribution = contribution
        });
    }

    internal static string ToRelationshipKind(this string? type)
    {
        if (string.IsNullOrWhiteSpace(type))
        {
            return RelationshipKinds.Crew;
        }

        var trimmed = type.Trim();
        if (string.Equals(trimmed, "Director", StringComparison.OrdinalIgnoreCase))
        {
            return RelationshipKinds.Director;
        }

        if (WriterTypes.Contains(trimmed))
        {
            return RelationshipKinds.Writer;
        }

        return RelationshipKinds.Crew;
    }

    internal static string? ToCastQualifier(this string? characterName, bool self, bool voice)
    {
        var name = characterName.NullIfBlank()?.Trim();
        var qualifier = (self, voice) switch
        {
            (true, true) => "self, voice",
            (true, false) => "self",
            (false, true) => "voice",
            _ => null
        };
        if (name is null)
        {
            return qualifier;
        }

        return qualifier is null ? name : name + " (" + qualifier + ")";
    }

    private static IReadOnlyDictionary<string, string> ToPersonExternalIds(this long personId)
    {
        var idString = personId.ToString(CultureInfo.InvariantCulture);
        return new Dictionary<string, string> { [Constants.ProviderType] = idString };
    }

    private static MetadataContribution ToContribution(this PersonDto person, DateTime utcNow)
    {
        var idString = person.Id.ToString(CultureInfo.InvariantCulture);
        var contribution = new MetadataContribution
        {
            ProviderType = Constants.ProviderType,
            Source = "tvmaze:person:" + idString,
            Title = person.Name!.Trim(),
            BirthDate = person.Birthday.ToDateOnly(),
            DeathDate = person.Deathday.ToDateOnly(),
            DateLastRefreshed = utcNow
        };

        contribution.ExternalIds.Add(new ExternalId
        {
            Source = Constants.ProviderType,
            Value = idString,
            Ordinal = 0
        });
        contribution.AddPersonFacts(person);

        var profile = person.Image.ToProfileImage();
        if (profile is not null)
        {
            contribution.Images.Add(profile);
        }

        return contribution;
    }

    public static MetadataSearchCandidate? ToMetadataSearchCandidate(this ShowDto? show)
    {
        if (show is null || show.Id <= 0)
        {
            return null;
        }

        var idString = show.Id.ToString(CultureInfo.InvariantCulture);
        var releaseDate = show.Premiered.ToDateOnly();
        return new MetadataSearchCandidate
        {
            ProviderType = Constants.ProviderType,
            ExternalIdSource = Constants.ProviderType,
            ExternalId = idString,
            ContentType = "tv",
            Level = "show",
            Title = show.Name.NullIfBlank(),
            Year = releaseDate?.Year,
            ReleaseDate = releaseDate,
            Overview = show.Summary.NormalizeHtmlToText(),
            Language = show.Language.NullIfBlank(),
            ThumbnailUrl = show.Image.ToThumbnail(),
            SubjectReferences = show.ToSearchSubjectReferences()
        };
    }

    private static Uri? ToThumbnail(this ImageReferenceDto? image)
    {
        return TryUri(image?.Medium) ?? TryUri(image?.Original);
    }

    public static IReadOnlyList<ProviderMetadataSearchSubjectReference> ToSearchSubjectReferences(this ShowDto show)
    {
        var references = new List<ProviderMetadataSearchSubjectReference>(show.ToNetworkReferences());
        foreach (var entry in show.Embedded?.Cast ?? [])
        {
            if (entry?.Person is not { } person || person.Id <= 0 || string.IsNullOrWhiteSpace(person.Name))
            {
                continue;
            }

            references.Add(person.ToSearchSubjectReference(
                RelationshipKinds.Cast,
                (entry.Character?.Name).ToCastQualifier(entry.Self, entry.Voice)));
        }

        foreach (var entry in show.Embedded?.Crew ?? [])
        {
            if (entry?.Person is not { } person || person.Id <= 0 || string.IsNullOrWhiteSpace(person.Name))
            {
                continue;
            }

            references.Add(person.ToSearchSubjectReference(entry.Type.ToRelationshipKind(), entry.Type));
        }

        return references;
    }

    public static IReadOnlyList<ProviderMetadataSearchSubjectReference> ToNetworkReferences(this ShowDto show)
    {
        var references = new List<ProviderMetadataSearchSubjectReference>();
        show.Network.AddSearchSubjectReference(references);
        show.WebChannel.AddSearchSubjectReference(references);
        return references;
    }

    private static void AddSearchSubjectReference(
        this NetworkDto? network,
        List<ProviderMetadataSearchSubjectReference> references)
    {
        if (network is null || network.Id <= 0 || string.IsNullOrWhiteSpace(network.Name))
        {
            return;
        }

        references.Add(new ProviderMetadataSearchSubjectReference
        {
            SubjectKind = RelationshipKinds.StudioSubject,
            DisplayName = network.Name.Trim(),
            RelationshipKind = RelationshipKinds.Network,
            ExternalIds = new Dictionary<string, string>
            {
                [Constants.ProviderType] = network.Id.ToString(CultureInfo.InvariantCulture)
            }
        });
    }

    private static ProviderMetadataSearchSubjectReference ToSearchSubjectReference(
        this PersonDto person,
        string relationshipKind,
        string? qualifier)
    {
        return new()
        {
            SubjectKind = RelationshipKinds.PersonSubject,
            DisplayName = person.Name!.Trim(),
            RelationshipKind = relationshipKind,
            RelationshipQualifier = qualifier,
            ExternalIds = new Dictionary<string, string>
            {
                [Constants.ProviderType] = person.Id.ToString(CultureInfo.InvariantCulture)
            }
        };
    }

    public static MetadataContribution ToContribution(
        this ShowDto show,
        IReadOnlyList<ImageDto> images,
        IReadOnlyList<AkaDto> akas,
        DateTime utcNow)
    {
        var idString = show.Id.ToString(CultureInfo.InvariantCulture);
        var contribution = new MetadataContribution
        {
            ProviderType = Constants.ProviderType,
            Source = show.ToSource(),
            Title = show.Name.NullIfBlank(),
            Overview = show.Summary.NormalizeHtmlToText(),
            PublishDate = show.Premiered.ToDateOnly(),
            Duration = show.ToRuntime(),
            Language = show.Language.NullIfBlank(),
            ProductionStatus = show.Status.NullIfBlank(),
            Established = show.Premiered.ToDateOnly(),
            Dissolved = show.Ended.ToDateOnly(),
            DateLastRefreshed = utcNow
        };

        contribution.ExternalIds.Add(new ExternalId
        {
            Source = Constants.ProviderType,
            Value = idString,
            Ordinal = 0
        });
        var imdbId = show.Externals?.Imdb;
        if (!string.IsNullOrWhiteSpace(imdbId))
        {
            contribution.ExternalIds.Add(new ExternalId
            {
                Source = Constants.ImdbSource,
                Value = imdbId.Trim(),
                Ordinal = 1
            });
        }

        if (show.Externals?.TheTvdb is { } theTvdb && theTvdb > 0)
        {
            contribution.ExternalIds.Add(new ExternalId
            {
                Source = Constants.TheTvdbSource,
                Value = theTvdb.ToString(CultureInfo.InvariantCulture),
                Ordinal = 2
            });
        }

        if (show.Externals?.TvRage is { } tvRage && tvRage > 0)
        {
            contribution.ExternalIds.Add(new ExternalId
            {
                Source = Constants.TvRageSource,
                Value = tvRage.ToString(CultureInfo.InvariantCulture),
                Ordinal = 3
            });
        }

        contribution.AddGenres(show.Genres);
        contribution.AddShowFacts(show);
        contribution.AddAlternateTexts(show, akas);
        foreach (var reference in show.ToReferences())
        {
            contribution.References.Add(reference);
        }

        contribution.AddShowImages(images, show.Image);

        return contribution;
    }

    public static string ToSource(this ShowDto show)
    {
        return Constants.ShowSourcePrefix + show.Id.ToString(CultureInfo.InvariantCulture);
    }

    public static IReadOnlyList<AlternateText> ToAlternateTexts(this ShowDto show, IReadOnlyList<AkaDto> akas)
    {
        var texts = new List<AlternateText>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var primaryTitle = show.Name.NullIfBlank()?.Trim();
        foreach (var aka in akas)
        {
            var name = aka?.Name.NullIfBlank()?.Trim();
            if (name is null || string.Equals(name, primaryTitle, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!seen.Add(name))
            {
                continue;
            }

            texts.Add(new AlternateText { Kind = "Title", Language = null, Text = name, Ordinal = texts.Count });
        }

        return texts;
    }

    public static IReadOnlyList<Reference> ToReferences(this ShowDto show)
    {
        var references = new List<Reference>();
        var databaseUrl = TryUri(show.Url);
        if (databaseUrl is not null)
        {
            references.Add(new Reference
            {
                Kind = "database",
                Source = Constants.ProviderType,
                Url = databaseUrl,
                DisplayName = "TVmaze",
                Ordinal = references.Count
            });
        }

        var officialSite = TryUri(show.OfficialSite);
        if (officialSite is not null)
        {
            references.Add(new Reference
            {
                Kind = "official",
                Source = Constants.ProviderType,
                Url = officialSite,
                DisplayName = "Official site",
                Ordinal = references.Count
            });
        }

        return references;
    }

    public static TimeSpan? ToRuntime(this ShowDto show)
    {
        var minutes = show.Runtime ?? show.AverageRuntime;
        return minutes is int value && value > 0 ? TimeSpan.FromMinutes(value) : null;
    }

    private static void AddGenres(this MetadataContribution contribution, IReadOnlyList<string>? genres)
    {
        if (genres is null)
        {
            return;
        }

        var ordinal = 0;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var genre in genres)
        {
            if (string.IsNullOrWhiteSpace(genre) || !seen.Add(genre.Trim()))
            {
                continue;
            }

            contribution.Genres.Add(new Genre { Name = genre.Trim(), Ordinal = ordinal++ });
        }
    }

    private static void AddAlternateTexts(
        this MetadataContribution contribution,
        ShowDto show,
        IReadOnlyList<AkaDto> akas)
    {
        foreach (var text in show.ToAlternateTexts(akas))
        {
            contribution.AlternateTexts.Add(text);
        }
    }

    private static Uri? TryUri(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps) ? uri : null;
    }
}
