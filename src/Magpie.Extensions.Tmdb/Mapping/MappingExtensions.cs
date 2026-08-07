using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

using Magpie.Extensions.Tmdb.QueryEngine;
using Magpie.Models;
using Magpie.Models.Data;
using Magpie.Models.MetadataSearch;
using Magpie.Sdk.Content;

namespace Magpie.Extensions.Tmdb.Mapping;

internal static class MappingExtensions
{
    private const string AlternateTitleKind = "title";

    private const int TheatricalReleaseType = 3;

    private const int TheatricalLimitedReleaseType = 2;

    private const string PosterType = "poster";

    private const string BackdropType = "backdrop";

    private const string LogoType = "logo";

    private const string StillType = "still";

    private const string ProfileType = "profile";

    private const string PosterSize = "w500";

    private const string BackdropSize = "w1280";

    private const string LogoSize = "w500";

    private const string StillSize = "w300";

    private const string ProfileSize = "w185";

    private const int GalleryCapPerType = 10;

    private const string NameAliasKind = "name";

    private const string SocialReferenceKind = "social";

    private static readonly IReadOnlyDictionary<string, string> EmptyStrings = new Dictionary<string, string>();

    private const int CastCap = 30;

    private const int CrewCap = 30;

    private static readonly HashSet<string> WriterJobs = new(StringComparer.OrdinalIgnoreCase)
    {
        "Writer",
        "Screenplay",
        "Story"
    };

    private const int StudioCap = 12;

    private const string SeasonLevelSlug = "season";

    private const string EpisodeLevelSlug = "episode";

    private const string YouTubeSite = "YouTube";

    private const string YouTubeEmbedKind = "youtube";

    public static IReadOnlyList<AlternateText> ToAlternateTexts(this MovieAlternativeTitlesDto? payload)
    {
        return Project(payload?.Titles);
    }

    public static IReadOnlyList<AlternateText> ToAlternateTexts(this TvAlternativeTitlesDto? payload)
    {
        return Project(payload?.Results);
    }

    private static IReadOnlyList<AlternateText> Project(IReadOnlyList<AlternativeTitleEntryDto>? entries)
    {
        if (entries is null || entries.Count == 0)
        {
            return [];
        }

        var result = new List<AlternateText>(entries.Count);
        var ordinal = 0;
        foreach (var entry in entries)
        {
            if (entry is null || string.IsNullOrWhiteSpace(entry.Title))
            {
                continue;
            }

            result.Add(new AlternateText
            {
                Kind = entry.Type.NullIfBlank()?.Trim() ?? AlternateTitleKind,
                Language = null,
                Text = entry.Title!.Trim(),
                Ordinal = ordinal++
            });
        }

        return result;
    }

    internal static SearchHit ToSearchHit(this MovieSearchHitDto hit, int sourceOrder)
    {
        return new()
        {
            Route = ContentRoute.Movie,
            Id = hit.Id,
            Title = hit.Title.NullIfBlank(),
            OriginalTitle = hit.OriginalTitle.NullIfBlank(),
            Overview = hit.Overview.NullIfBlank(),
            OriginalLanguage = hit.OriginalLanguage.NullIfBlank(),
            ReleaseDate = hit.ReleaseDate.ParseDateOnly(),
            GenreIds = hit.GenreIds ?? [],
            PosterPath = hit.PosterPath.NullIfBlank(),
            BackdropPath = hit.BackdropPath.NullIfBlank(),
            SourceOrder = sourceOrder,
            ExternalIds = ContentExternalIds(hit.Id, externalIds: null)
        };
    }

    internal static SearchHit ToSearchHit(this TvSearchHitDto hit, int sourceOrder)
    {
        return new()
        {
            Route = ContentRoute.TvShow,
            Id = hit.Id,
            Title = hit.Name.NullIfBlank(),
            OriginalTitle = hit.OriginalName.NullIfBlank(),
            Overview = hit.Overview.NullIfBlank(),
            OriginalLanguage = hit.OriginalLanguage.NullIfBlank(),
            ReleaseDate = hit.FirstAirDate.ParseDateOnly(),
            GenreIds = hit.GenreIds ?? [],
            PosterPath = hit.PosterPath.NullIfBlank(),
            BackdropPath = hit.BackdropPath.NullIfBlank(),
            SourceOrder = sourceOrder,
            ExternalIds = ContentExternalIds(hit.Id, externalIds: null)
        };
    }

    internal static SearchHit FromMovieCredit(
        this MovieCreditDto hit,
        ResolvedSubject subject,
        bool cast,
        int sourceOrder)
    {
        return new()
        {
            Route = ContentRoute.Movie,
            Id = hit.Id,
            Title = hit.Title.NullIfBlank(),
            OriginalTitle = hit.OriginalTitle.NullIfBlank(),
            Overview = hit.Overview.NullIfBlank(),
            OriginalLanguage = hit.OriginalLanguage.NullIfBlank(),
            ReleaseDate = hit.ReleaseDate.ParseDateOnly(),
            GenreIds = hit.GenreIds ?? [],
            PosterPath = hit.PosterPath.NullIfBlank(),
            BackdropPath = hit.BackdropPath.NullIfBlank(),
            SourceOrder = sourceOrder,
            ExternalIds = ContentExternalIds(hit.Id, externalIds: null),
            SubjectReferences = [SubjectReference(subject, cast, cast ? hit.Character : hit.Job)]
        };
    }

    internal static SearchHit FromTvCredit(this TvCreditDto hit, ResolvedSubject subject, bool cast, int sourceOrder)
    {
        return new()
        {
            Route = ContentRoute.TvShow,
            Id = hit.Id,
            Title = hit.Name.NullIfBlank(),
            OriginalTitle = hit.OriginalName.NullIfBlank(),
            Overview = hit.Overview.NullIfBlank(),
            OriginalLanguage = hit.OriginalLanguage.NullIfBlank(),
            ReleaseDate = hit.FirstAirDate.ParseDateOnly(),
            GenreIds = hit.GenreIds ?? [],
            PosterPath = hit.PosterPath.NullIfBlank(),
            BackdropPath = hit.BackdropPath.NullIfBlank(),
            SourceOrder = sourceOrder,
            ExternalIds = ContentExternalIds(hit.Id, externalIds: null),
            SubjectReferences = [SubjectReference(subject, cast, cast ? hit.Character : hit.Job)]
        };
    }

    internal static SearchHit FromMovieDetail(this MovieDetailDto detail)
    {
        return new()
        {
            Route = ContentRoute.Movie,
            Id = detail.Id,
            Title = detail.Title.NullIfBlank(),
            OriginalTitle = detail.OriginalTitle.NullIfBlank(),
            Overview = detail.Overview.NullIfBlank(),
            OriginalLanguage = detail.OriginalLanguage.NullIfBlank(),
            ReleaseDate = detail.ReleaseDate.ParseDateOnly(),
            Duration = detail.Runtime is >= 0 ? TimeSpan.FromMinutes(detail.Runtime.Value) : null,
            ProductionStatus = detail.Status.NullIfBlank(),
            GenreIds = detail.Genres?.Select(genre => genre.Id).ToList() ?? [],
            GenreNames = detail.Genres?.Select(genre => genre.Name).Where(name => !string.IsNullOrWhiteSpace(name))
                .Select(name => name!.Trim()).ToList() ??
                [],
            PosterPath = detail.PosterPath.NullIfBlank(),
            BackdropPath = detail.BackdropPath.NullIfBlank(),
            Homepage = detail.Homepage.NullIfBlank(),
            ImageGallery = detail.Images,
            AlternateTexts = detail.AlternativeTitles.ToAlternateTexts(),
            ContentRatings = detail.ReleaseDates.ToContentRatings(),
            Previews = detail.Videos.ToPreviews(),
            ExternalIds = ContentExternalIds(detail.Id, detail.ExternalIds),
            SubjectReferences = SubjectReferences(detail.ProductionCompanies, detail.Credits),
            DetailsLoaded = true
        };
    }

    internal static SearchHit FromTvDetail(this TvDetailDto detail)
    {
        return new()
        {
            Route = ContentRoute.TvShow,
            Id = detail.Id,
            Title = detail.Name.NullIfBlank(),
            OriginalTitle = detail.OriginalName.NullIfBlank(),
            Overview = detail.Overview.NullIfBlank(),
            OriginalLanguage = detail.OriginalLanguage.NullIfBlank(),
            ReleaseDate = detail.FirstAirDate.ParseDateOnly(),
            Duration = detail.EpisodeRunTime?.FirstOrDefault() is > 0 and var runtime
                ? TimeSpan.FromMinutes(runtime)
                : null,
            ProductionStatus = detail.Status.NullIfBlank(),
            GenreIds = detail.Genres?.Select(genre => genre.Id).ToList() ?? [],
            GenreNames = detail.Genres?.Select(genre => genre.Name).Where(name => !string.IsNullOrWhiteSpace(name))
                .Select(name => name!.Trim()).ToList() ??
                [],
            PosterPath = detail.PosterPath.NullIfBlank(),
            BackdropPath = detail.BackdropPath.NullIfBlank(),
            Homepage = detail.Homepage.NullIfBlank(),
            ImageGallery = detail.Images,
            AlternateTexts = detail.AlternativeTitles.ToAlternateTexts(),
            ContentRatings = detail.ContentRatings.ToContentRatings(),
            Previews = detail.Videos.ToPreviews(),
            ExternalIds = ContentExternalIds(detail.Id, detail.ExternalIds),
            SubjectReferences = SubjectReferences(detail.ProductionCompanies, detail.Credits, detail.Networks),
            DetailsLoaded = true
        };
    }

    internal static MetadataSearchCandidate ToMetadataSearchCandidate(this SearchHit hit)
    {
        return new()
        {
            ProviderType = Constants.ProviderType,
            ExternalIdSource = Constants.ProviderType,
            ExternalId = hit.Id.ToString(CultureInfo.InvariantCulture),
            ContentType = hit.Route == ContentRoute.Movie ? "movie" : "tv",
            Level = hit.Route == ContentRoute.Movie ? "movie" : "show",
            Title = hit.Title,
            Year = hit.ReleaseDate?.Year,
            ReleaseDate = hit.ReleaseDate,
            Overview = hit.Overview,
            Language = hit.OriginalLanguage,
            ThumbnailUrl = hit.ImageUrl,
            SubjectReferences = hit.SubjectReferences
        };
    }

    internal static ProviderMetadataSearchSubjectReference FromResolvedSubject(this ResolvedSubject subject)
    {
        return new()
        {
            SubjectKind = SubjectKind(subject.Role),
            DisplayName = subject.Name,
            RelationshipKind = subject.Role switch
            {
                SubjectRole.Company => "studio",
                SubjectRole.Network => Constants.Network,
                SubjectRole.Person => "person",
                SubjectRole.Cast => "cast",
                SubjectRole.Crew => "crew",
                SubjectRole.Director => "director",
                SubjectRole.Writer => "writer",
                _ => null
            },
            ExternalIds = SubjectExternalIds(subject)
        };
    }

    private static string SubjectKind(SubjectRole role)
    {
        return role switch
        {
            SubjectRole.Company => Constants.Studio,
            SubjectRole.Network => Constants.Network,
            _ => "person"
        };
    }

    private static IReadOnlyDictionary<string, string> SubjectExternalIds(ResolvedSubject subject)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [Constants.ProviderType] = subject.Id.ToString(CultureInfo.InvariantCulture)
        };
        if (subject.ExternalIds is not null)
        {
            foreach (var pair in subject.ExternalIds)
            {
                values[pair.Key] = pair.Value;
            }
        }

        return values;
    }

    private static IReadOnlyDictionary<string, string> ContentRatingsByBody(SearchHit hit)
    {
        if (hit.ContentRatings.Count == 0)
        {
            return EmptyStrings;
        }

        return hit.ContentRatings
            .GroupBy(rating => rating.Body, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.OrderBy(rating => rating.Ordinal).First().Rating,
                StringComparer.OrdinalIgnoreCase);
    }

    private static IReadOnlyDictionary<string, string> ContentExternalIds(int id, ExternalIdsDto? externalIds)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [Constants.ProviderType] = id.ToString(CultureInfo.InvariantCulture)
        };
        if (!string.IsNullOrWhiteSpace(externalIds?.ImdbId))
        {
            values["imdb"] = externalIds.ImdbId!;
        }

        if (!string.IsNullOrWhiteSpace(externalIds?.WikidataId))
        {
            values["wikidata"] = externalIds.WikidataId!;
        }

        if (externalIds?.TvdbId is > 0 and var tvdbId)
        {
            values["tvdb"] = tvdbId.ToString(CultureInfo.InvariantCulture);
        }

        return values;
    }

    private static ProviderMetadataSearchSubjectReference SubjectReference(
        ResolvedSubject subject,
        bool cast,
        string? qualifier)
    {
        return new()
        {
            SubjectKind = SubjectKind(subject.Role),
            DisplayName = subject.Name,
            RelationshipKind = subject.Role switch
            {
                SubjectRole.Company => "studio",
                SubjectRole.Network => Constants.Network,
                _ => cast ? Constants.Cast : qualifier.ClassifyCrewJob()
            },
            RelationshipQualifier = qualifier.NullIfBlank(),
            ExternalIds = SubjectExternalIds(subject)
        };
    }

    private static IReadOnlyList<ProviderMetadataSearchSubjectReference> SubjectReferences(
        IReadOnlyList<ProductionCompanyDto>? companies,
        CreditsDto? credits,
        IReadOnlyList<ProductionCompanyDto>? networks = null)
    {
        var references = new List<ProviderMetadataSearchSubjectReference>();
        if (companies is not null)
        {
            references.AddRange(companies
                .Where(company => company.Id > 0 && !string.IsNullOrWhiteSpace(company.Name))
                .Select(company => SubjectReference(
                    new ResolvedSubject(company.Id, company.Name!.Trim(), SubjectRole.Company),
                    cast: false,
                    qualifier: null)));
        }

        if (networks is not null)
        {
            references.AddRange(networks
                .Where(network => network.Id > 0 && !string.IsNullOrWhiteSpace(network.Name))
                .Select(network => SubjectReference(
                    new ResolvedSubject(network.Id, network.Name!.Trim(), SubjectRole.Network),
                    cast: false,
                    qualifier: null)));
        }

        if (credits?.Cast is not null)
        {
            references.AddRange(credits.Cast
                .Where(cast => cast.Id > 0 && !string.IsNullOrWhiteSpace(cast.Name))
                .Select(cast => SubjectReference(
                    new ResolvedSubject(cast.Id, cast.Name!.Trim(), SubjectRole.Cast),
                    cast: true,
                    cast.Character)));
        }

        if (credits?.Crew is not null)
        {
            references.AddRange(credits.Crew
                .Where(crew => crew.Id > 0 && !string.IsNullOrWhiteSpace(crew.Name))
                .Select(crew => SubjectReference(
                    new ResolvedSubject(crew.Id, crew.Name!.Trim(), SubjectRole.Crew),
                    cast: false,
                    crew.Job)));
        }

        return references;
    }

    public static MetadataContribution ToContribution(
        this CompanyDetailDto dto,
        string subjectKind,
        string imageBaseUrl,
        DateTime utcNow)
    {
        var network = subjectKind == Constants.Network;
        var segment = network ? "network" : "company";
        var idString = dto.Id.ToString(CultureInfo.InvariantCulture);
        var contribution = new MetadataContribution
        {
            ProviderType = Constants.ProviderType,
            Source = $"tmdb:{segment}:{idString}",
            Title = dto.Name.NullIfBlank(),
            Overview = dto.Description.NullIfBlank(),
            DateLastRefreshed = utcNow
        };

        contribution.ExternalIds.Add(new ExternalId
        {
            Source = Constants.ProviderType,
            Value = idString,
            Ordinal = 0
        });

        contribution.AddSelfReferences(segment, idString, imdbId: null, segment, dto.Homepage);

        var logo = dto.LogoPath.ResolveImageUrl(imageBaseUrl, "w185");
        if (logo is not null)
        {
            contribution.Images.Add(new Image
            {
                Type = "logo",
                Url = logo,
                Source = Constants.ProviderType,
                Ordinal = 0
            });
        }

        AddCustomMetadata(contribution, dto);
        return contribution;
    }

    private static void AddCustomMetadata(MetadataContribution contribution, CompanyDetailDto dto)
    {
        if (dto.OriginCountry.NullIfBlank() is { } originCountry)
        {
            contribution.CustomMetadata.Add(new CustomMetadataValue
            {
                Key = "originCountry",
                Value = originCountry.Trim()
            });
        }

        if (dto.Headquarters.NullIfBlank() is { } headquarters)
        {
            contribution.CustomMetadata.Add(new CustomMetadataValue
            {
                Key = "headquarters",
                Value = headquarters.Trim()
            });
        }

        if (dto.ParentCompany is { Id: > 0 } parent && !string.IsNullOrWhiteSpace(parent.Name))
        {
            contribution.CustomMetadata.Add(new CustomMetadataValue
            {
                Key = "parentCompany",
                Value = parent.Name!.Trim()
            });
            contribution.CustomMetadata.Add(new CustomMetadataValue
            {
                Key = "parentCompanyId",
                Value = parent.Id.ToString(CultureInfo.InvariantCulture)
            });
        }
    }

    public static IReadOnlyList<ContentRating> ToContentRatings(this MovieReleaseDatesDto? payload)
    {
        if (payload?.Results is null || payload.Results.Count == 0)
        {
            return [];
        }

        var result = new List<ContentRating>();
        var ordinal = 0;
        foreach (var country in payload.Results)
        {
            if (country is null || string.IsNullOrWhiteSpace(country.CountryCode))
            {
                continue;
            }

            var certification = PickFirstCertification(country.ReleaseDates);
            if (certification is null)
            {
                continue;
            }

            result.Add(new ContentRating
            {
                Body = country.CountryCode!.Trim().ToUpperInvariant(),
                Rating = certification,
                Ordinal = ordinal++
            });
        }

        return result;
    }

    public static IReadOnlyList<ContentRating> ToContentRatings(this TvContentRatingsDto? payload)
    {
        if (payload?.Results is null || payload.Results.Count == 0)
        {
            return [];
        }

        var result = new List<ContentRating>();
        var ordinal = 0;
        foreach (var entry in payload.Results)
        {
            if (entry is null ||
                string.IsNullOrWhiteSpace(entry.CountryCode) ||
                string.IsNullOrWhiteSpace(entry.Rating))
            {
                continue;
            }

            result.Add(new ContentRating
            {
                Body = entry.CountryCode!.Trim().ToUpperInvariant(),
                Rating = entry.Rating!.Trim(),
                Ordinal = ordinal++
            });
        }

        return result;
    }

    private static string? PickFirstCertification(IReadOnlyList<ReleaseDateEntryDto>? releaseDates)
    {
        if (releaseDates is null || releaseDates.Count == 0)
        {
            return null;
        }

        return PickCertificationOfType(releaseDates, TheatricalReleaseType)
            ?? PickCertificationOfType(releaseDates, TheatricalLimitedReleaseType)
            ?? PickCertificationOfType(releaseDates, releaseType: null);
    }

    private static string? PickCertificationOfType(IReadOnlyList<ReleaseDateEntryDto> releaseDates, int? releaseType)
    {
        foreach (var release in releaseDates)
        {
            if (release is null || releaseType is { } type && release.Type != type)
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(release.Certification))
            {
                return release.Certification!.Trim();
            }
        }

        return null;
    }

    public static IReadOnlyList<SubjectRelationshipDefinition> ToSubjectRelationships(
        this CreditsDto? credits,
        string imageBaseUrl,
        DateTime utcNow)
    {
        if (credits is null)
        {
            return [];
        }

        var result = new List<SubjectRelationshipDefinition>();
        AppendCast(credits.Cast, imageBaseUrl, utcNow, result);
        AppendCrew(credits.Crew, imageBaseUrl, utcNow, result);
        return result;
    }

    private static void AppendCast(
        IReadOnlyList<CastMemberDto>? cast,
        string imageBaseUrl,
        DateTime utcNow,
        List<SubjectRelationshipDefinition> result)
    {
        if (cast is null || cast.Count == 0)
        {
            return;
        }

        var sorted = new List<CastMemberDto>(cast.Count);
        foreach (var entry in cast)
        {
            if (entry is null || entry.Id <= 0 || string.IsNullOrWhiteSpace(entry.Name))
            {
                continue;
            }

            sorted.Add(entry);
        }

        sorted.Sort((a, b) => (a.Order ?? int.MaxValue).CompareTo(b.Order ?? int.MaxValue));

        var emitted = 0;
        foreach (var entry in sorted)
        {
            if (emitted >= CastCap)
            {
                break;
            }

            result.Add(new SubjectRelationshipDefinition
            {
                RelationshipKind = Constants.Cast,
                RelationshipQualifier = entry.Character.NullIfBlank(),
                RelationshipOrdinal = emitted,
                SubjectKind = Constants.Person,
                SubjectName = entry.Name!.Trim(),
                SubjectExternalIds = BuildExternalIds(entry.Id),
                InitialSubjectContribution = BuildPersonContribution(
                    entry.Id,
                    entry.Name!,
                    entry.ProfilePath,
                    imageBaseUrl,
                    utcNow)
            });
            emitted++;
        }
    }

    private static void AppendCrew(
        IReadOnlyList<CrewMemberDto>? crew,
        string imageBaseUrl,
        DateTime utcNow,
        List<SubjectRelationshipDefinition> result)
    {
        if (crew is null || crew.Count == 0)
        {
            return;
        }

        var emitted = 0;
        var ordinalBase = result.Count;
        foreach (var entry in crew)
        {
            if (entry is null || entry.Id <= 0 || string.IsNullOrWhiteSpace(entry.Name))
            {
                continue;
            }

            if (emitted >= CrewCap)
            {
                break;
            }

            var kind = ClassifyCrewJob(entry.Job);
            result.Add(new SubjectRelationshipDefinition
            {
                RelationshipKind = kind,
                RelationshipQualifier = entry.Job.NullIfBlank(),
                RelationshipOrdinal = ordinalBase + emitted,
                SubjectKind = Constants.Person,
                SubjectName = entry.Name!.Trim(),
                SubjectExternalIds = BuildExternalIds(entry.Id),
                InitialSubjectContribution = BuildPersonContribution(
                    entry.Id,
                    entry.Name!,
                    entry.ProfilePath,
                    imageBaseUrl,
                    utcNow)
            });
            emitted++;
        }
    }

    internal static string ClassifyCrewJob(this string? job)
    {
        if (string.IsNullOrWhiteSpace(job))
        {
            return Constants.Crew;
        }

        var trimmed = job!.Trim();
        if (string.Equals(trimmed, "Director", StringComparison.OrdinalIgnoreCase))
        {
            return Constants.Director;
        }

        if (WriterJobs.Contains(trimmed))
        {
            return Constants.Writer;
        }

        return Constants.Crew;
    }

    private static IReadOnlyDictionary<string, string> BuildExternalIds(int personId)
    {
        var idString = personId.ToString(CultureInfo.InvariantCulture);
        return new Dictionary<string, string>()
        {
            [Constants.ProviderType] = idString
        };
    }

    private static MetadataContribution BuildPersonContribution(
        int personId,
        string name,
        string? profilePath,
        string imageBaseUrl,
        DateTime utcNow)
    {
        var idString = personId.ToString(CultureInfo.InvariantCulture);
        var contribution = new MetadataContribution
        {
            ProviderType = Constants.ProviderType,
            Source = $"{Constants.PersonSourcePrefix}{idString}",
            Title = name.Trim(),
            DateLastRefreshed = utcNow
        };

        contribution.ExternalIds.Add(new ExternalId
        {
            Source = Constants.ProviderType,
            Value = idString,
            Ordinal = 0
        });

        var profile = profilePath.ResolveImageUrl(imageBaseUrl, "w185");
        if (profile is not null)
        {
            contribution.Images.Add(new Image
            {
                Type = "profile",
                Url = profile,
                Source = Constants.ProviderType,
                Ordinal = 0
            });
        }

        return contribution;
    }

    public static DateTime? ParseDateOnly(this string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return DateTime.TryParseExact(
            value,
            "yyyy-MM-dd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var parsed)
            ? DateTime.SpecifyKind(parsed, DateTimeKind.Utc)
            : null;
    }

    internal static IReadOnlyList<Image> ToImages(this string? imageBaseUrl, string? posterPath, string? backdropPath)
    {
        var images = new List<Image>();
        var poster = posterPath.ResolveImageUrl(imageBaseUrl, PosterSize);
        if (poster is not null)
        {
            images.Add(new Image
            {
                Type = PosterType,
                MimeType = MimeType(posterPath),
                Url = poster,
                Source = Constants.ProviderType,
                Ordinal = images.Count
            });
        }

        var backdrop = backdropPath.ResolveImageUrl(imageBaseUrl, BackdropSize);
        if (backdrop is not null)
        {
            images.Add(new Image
            {
                Type = BackdropType,
                MimeType = MimeType(backdropPath),
                Url = backdrop,
                Source = Constants.ProviderType,
                Ordinal = images.Count
            });
        }

        return images;
    }

    internal static IReadOnlyList<Image> ToImages(
        this string? imageBaseUrl,
        string? posterPath,
        string? backdropPath,
        ImagesPayloadDto? gallery)
    {
        var images = new List<Image>(imageBaseUrl.ToImages(posterPath, backdropPath));
        if (gallery is null)
        {
            return images;
        }

        images.AppendGallery(
            imageBaseUrl,
            gallery.Posters,
            PosterType,
            PosterSize,
            posterPath);
        images.AppendGallery(
            imageBaseUrl,
            gallery.Backdrops,
            BackdropType,
            BackdropSize,
            backdropPath);
        images.AppendGallery(
            imageBaseUrl,
            gallery.Logos,
            LogoType,
            LogoSize,
            primaryPath: null);
        return images;
    }

    internal static void AppendGallery(
        this List<Image> images,
        string? imageBaseUrl,
        IReadOnlyList<ImageEntryDto>? entries,
        string type,
        string size,
        string? primaryPath)
    {
        if (entries is null || entries.Count == 0)
        {
            return;
        }

        var emitted = 0;
        foreach (var image in images)
        {
            if (string.Equals(image.Type, type, StringComparison.Ordinal))
            {
                emitted++;
            }
        }

        foreach (var entry in entries)
        {
            if (emitted >= GalleryCapPerType)
            {
                break;
            }

            var path = entry?.FilePath.NullIfBlank();
            if (path is null || primaryPath is not null && string.Equals(path, primaryPath, StringComparison.Ordinal))
            {
                continue;
            }

            var url = path.ResolveImageUrl(imageBaseUrl, size);
            if (url is null)
            {
                continue;
            }

            images.Add(new Image
            {
                Type = type,
                MimeType = MimeType(path),
                Url = url,
                Source = Constants.ProviderType,
                Language = entry!.LanguageCode.NullIfBlank(),
                Width = entry.Width is > 0 ? entry.Width : null,
                Height = entry.Height is > 0 ? entry.Height : null,
                Ordinal = images.Count
            });
            emitted++;
        }
    }

    private static string? MimeType(string? path)
    {
        if (path is null)
        {
            return null;
        }

        return path.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase)
                ? "image/jpeg"
                : path.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
                    ? "image/png"
                    : path.EndsWith(".svg", StringComparison.OrdinalIgnoreCase)
                        ? "image/svg+xml"
                        : null;
    }

    internal static string IncludeImageLanguage(this string language)
    {
        var dash = language.IndexOf('-');
        return (dash > 0 ? language.Substring(0, dash) : language) + ",null";
    }

    internal static Uri? ResolveImageUrl(this string? path, string? imageBase, string size)
    {
        if (string.IsNullOrEmpty(path))
        {
            return null;
        }

        var basePart = string.IsNullOrEmpty(imageBase) ? "https://image.tmdb.org/t/p/" : imageBase!;
        if (!basePart.EndsWith("/", StringComparison.Ordinal))
        {
            basePart += "/";
        }

        var trimmed = path!.StartsWith("/", StringComparison.Ordinal) ? path.Substring(1) : path;
        var composed = string.Format(
            CultureInfo.InvariantCulture,
            "{0}{1}/{2}",
            basePart,
            size,
            trimmed);
        return Uri.TryCreate(composed, UriKind.Absolute, out var uri) ? uri : null;
    }

    public static MetadataContribution ToContribution(
        this string kind,
        int id,
        string imageBaseUrl,
        DateTime utcNow,
        string? title,
        string? originalTitle,
        string? overview,
        DateTime? publishDate,
        TimeSpan? duration,
        string? originalLanguage,
        string? productionStatus,
        ExternalIdsDto? externalIds,
        string? posterPath,
        string? backdropPath,
        string? homepage,
        IReadOnlyList<GenreDto>? genres,
        ImagesPayloadDto? images,
        IReadOnlyList<AlternateText> alternateTexts,
        IReadOnlyList<ContentRating> contentRatings,
        IReadOnlyList<Preview> previews)
    {
        var idString = id.ToString(CultureInfo.InvariantCulture);
        var contribution = new MetadataContribution
        {
            ProviderType = Constants.ProviderType,
            Source = $"tmdb:{kind}:{idString}",
            Title = title.NullIfBlank(),
            OriginalTitle = originalTitle.NullIfBlank(),
            Overview = overview.NullIfBlank(),
            PublishDate = publishDate,
            Duration = duration,
            Language = originalLanguage.NullIfBlank(),
            ProductionStatus = productionStatus.NullIfBlank(),
            DateLastRefreshed = utcNow
        };

        contribution.ExternalIds.Add(new ExternalId
        {
            Source = Constants.ProviderType,
            Value = idString,
            Ordinal = 0
        });
        var imdbId = externalIds?.ImdbId.NullIfBlank();
        if (imdbId is not null)
        {
            contribution.ExternalIds.Add(new ExternalId
            {
                Source = "imdb",
                Value = imdbId,
                Ordinal = contribution.ExternalIds.Count
            });
        }

        var wikidataId = externalIds?.WikidataId.NullIfBlank();
        if (wikidataId is not null)
        {
            contribution.ExternalIds.Add(new ExternalId
            {
                Source = "wikidata",
                Value = wikidataId,
                Ordinal = contribution.ExternalIds.Count
            });
        }

        if (externalIds?.TvdbId is > 0 and var tvdbId)
        {
            contribution.ExternalIds.Add(new ExternalId
            {
                Source = "tvdb",
                Value = tvdbId.ToString(CultureInfo.InvariantCulture),
                Ordinal = contribution.ExternalIds.Count
            });
        }

        contribution.AddSelfReferences(kind, idString, imdbId, "title", homepage);
        contribution.AddSocialReferences(externalIds);

        if (genres is not null)
        {
            var ordinal = 0;
            foreach (var genre in genres)
            {
                if (string.IsNullOrWhiteSpace(genre.Name))
                {
                    continue;
                }

                contribution.Genres.Add(new Genre
                {
                    Name = genre.Name!,
                    Ordinal = ordinal++
                });
            }
        }

        foreach (var image in imageBaseUrl.ToImages(posterPath, backdropPath, images))
        {
            contribution.Images.Add(image);
        }

        foreach (var alternate in alternateTexts)
        {
            contribution.AlternateTexts.Add(alternate);
        }

        foreach (var rating in contentRatings)
        {
            contribution.ContentRatings.Add(rating);
        }

        foreach (var preview in previews)
        {
            contribution.Previews.Add(preview);
        }

        return contribution;
    }

    public static MetadataContribution ToContribution(this MovieDetailDto dto, string imageBaseUrl, DateTime utcNow)
    {
        return "movie".ToContribution(
            dto.Id,
            imageBaseUrl,
            utcNow,
            dto.Title,
            dto.OriginalTitle,
            dto.Overview,
            dto.ReleaseDate.ParseDateOnly(),
            dto.Runtime is > 0 ? TimeSpan.FromMinutes(dto.Runtime.Value) : null,
            dto.OriginalLanguage,
            dto.Status,
            dto.ExternalIds,
            dto.PosterPath,
            dto.BackdropPath,
            dto.Homepage,
            dto.Genres,
            dto.Images,
            dto.AlternativeTitles.ToAlternateTexts(),
            dto.ReleaseDates.ToContentRatings(),
            dto.Videos.ToPreviews());
    }

    public static MetadataContribution ToContribution(this PersonDetailDto dto, string imageBaseUrl, DateTime utcNow)
    {
        var idString = dto.Id.ToString(CultureInfo.InvariantCulture);
        var contribution = new MetadataContribution
        {
            ProviderType = Constants.ProviderType,
            Source = $"{Constants.PersonSourcePrefix}{idString}",
            Title = dto.Name.NullIfBlank(),
            Overview = dto.Biography.NullIfBlank(),
            BirthDate = dto.Birthday.ParseDateOnly(),
            DeathDate = dto.Deathday.ParseDateOnly(),
            DateLastRefreshed = utcNow
        };

        contribution.ExternalIds.Add(new ExternalId
        {
            Source = Constants.ProviderType,
            Value = idString,
            Ordinal = 0
        });
        if (dto.ExternalIds is { ImdbId: { Length: > 0 } imdbId })
        {
            contribution.ExternalIds.Add(new ExternalId
            {
                Source = "imdb",
                Value = imdbId,
                Ordinal = contribution.ExternalIds.Count
            });
        }

        if (dto.ExternalIds?.WikidataId.NullIfBlank() is { } wikidataId)
        {
            contribution.ExternalIds.Add(new ExternalId
            {
                Source = "wikidata",
                Value = wikidataId,
                Ordinal = contribution.ExternalIds.Count
            });
        }

        contribution.AddSelfReferences("person", idString, dto.ExternalIds?.ImdbId, "name");
        contribution.AddSocialReferences(dto.ExternalIds);

        var images = new List<Image>();
        var profile = dto.ProfilePath.ResolveImageUrl(imageBaseUrl, ProfileSize);
        if (profile is not null)
        {
            images.Add(new Image
            {
                Type = ProfileType,
                Url = profile,
                Source = Constants.ProviderType,
                Ordinal = 0
            });
        }

        images.AppendGallery(
            imageBaseUrl,
            dto.Images?.Profiles,
            ProfileType,
            ProfileSize,
            dto.ProfilePath);
        foreach (var image in images)
        {
            contribution.Images.Add(image);
        }

        AddAliases(contribution, dto.AlsoKnownAs);
        AddCustomMetadata(contribution, dto);
        return contribution;
    }

    private static void AddAliases(MetadataContribution contribution, IReadOnlyList<string>? aliases)
    {
        if (aliases is null)
        {
            return;
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (contribution.Title is not null)
        {
            seen.Add(contribution.Title);
        }

        foreach (var alias in aliases)
        {
            if (string.IsNullOrWhiteSpace(alias) || !seen.Add(alias.Trim()))
            {
                continue;
            }

            contribution.AlternateTexts.Add(new AlternateText
            {
                Kind = NameAliasKind,
                Text = alias.Trim(),
                Ordinal = contribution.AlternateTexts.Count
            });
        }
    }

    private static void AddCustomMetadata(MetadataContribution contribution, PersonDetailDto dto)
    {
        if (GenderName(dto.Gender) is { } gender)
        {
            contribution.CustomMetadata.Add(new CustomMetadataValue { Key = "gender", Value = gender });
        }

        if (dto.PlaceOfBirth.NullIfBlank() is { } placeOfBirth)
        {
            contribution.CustomMetadata.Add(new CustomMetadataValue
            {
                Key = "placeOfBirth",
                Value = placeOfBirth.Trim()
            });
        }

        if (dto.KnownForDepartment.NullIfBlank() is { } department)
        {
            contribution.CustomMetadata.Add(new CustomMetadataValue
            {
                Key = "knownForDepartment",
                Value = department.Trim()
            });
        }
    }

    private static string? GenderName(int gender)
    {
        return gender switch
        {
            1 => "female",
            2 => "male",
            3 => "non-binary",
            _ => null
        };
    }

    public static IReadOnlyList<Reference> ToReferences(
        this string tmdbPathSegment,
        string idString,
        string? imdbId,
        string imdbPathSegment,
        string? homepage = null)
    {
        var references = new List<Reference>
        {
            new()
            {
                Kind = "database",
                Source = Constants.ProviderType,
                Url = new Uri($"https://www.themoviedb.org/{tmdbPathSegment}/{idString}"),
                DisplayName = "TMDB",
                Ordinal = 0
            }
        };

        if (!string.IsNullOrEmpty(imdbId))
        {
            references.Add(new Reference
            {
                Kind = "database",
                Source = "imdb",
                Url = new Uri($"https://www.imdb.com/{imdbPathSegment}/{imdbId}/"),
                DisplayName = "IMDb",
                Ordinal = references.Count
            });
        }

        if (Uri.TryCreate(homepage, UriKind.Absolute, out var homepageUrl) && homepageUrl.Scheme is "http" or "https")
        {
            references.Add(new Reference
            {
                Kind = "official",
                Source = Constants.ProviderType,
                Url = homepageUrl,
                Ordinal = references.Count
            });
        }

        return references;
    }

    public static void AddSocialReferences(this MetadataContribution contribution, ExternalIdsDto? externalIds)
    {
        if (externalIds is null)
        {
            return;
        }

        AddSocialReference(contribution, "facebook", "Facebook", "https://www.facebook.com/", externalIds.FacebookId);
        AddSocialReference(
            contribution,
            "instagram",
            "Instagram",
            "https://www.instagram.com/",
            externalIds.InstagramId);
        AddSocialReference(contribution, "twitter", "Twitter", "https://twitter.com/", externalIds.TwitterId);
        AddSocialReference(contribution, "tiktok", "TikTok", "https://www.tiktok.com/@", externalIds.TiktokId);
        AddSocialReference(contribution, "youtube", "YouTube", "https://www.youtube.com/", externalIds.YoutubeId);
    }

    private static void AddSocialReference(
        this MetadataContribution contribution,
        string source,
        string displayName,
        string urlPrefix,
        string? id)
    {
        if (string.IsNullOrWhiteSpace(id) ||
            !Uri.TryCreate(urlPrefix + Uri.EscapeDataString(id!.Trim()), UriKind.Absolute, out var url))
        {
            return;
        }

        contribution.References.Add(new Reference
        {
            Kind = SocialReferenceKind,
            Source = source,
            Url = url,
            DisplayName = displayName,
            Ordinal = contribution.References.Count
        });
    }

    public static void AddSelfReferences(
        this MetadataContribution contribution,
        string tmdbPathSegment,
        string idString,
        string? imdbId,
        string imdbPathSegment,
        string? homepage = null)
    {
        foreach (var reference in ToReferences(tmdbPathSegment, idString, imdbId, imdbPathSegment, homepage))
        {
            contribution.References.Add(reference);
        }
    }

    public static IReadOnlyList<SubjectRelationshipDefinition> ToSubjectRelationships(
        this IReadOnlyList<ProductionCompanyDto>? companies,
        string imageBaseUrl,
        int startOrdinal,
        DateTime utcNow)
    {
        return Project(
            companies,
            imageBaseUrl,
            startOrdinal,
            utcNow,
            Constants.Studio,
            Constants.Studio,
            "tmdb:company:");
    }

    public static IReadOnlyList<SubjectRelationshipDefinition> ToNetworkSubjectRelationships(
        this IReadOnlyList<ProductionCompanyDto>? networks,
        string imageBaseUrl,
        int startOrdinal,
        DateTime utcNow)
    {
        return Project(
            networks,
            imageBaseUrl,
            startOrdinal,
            utcNow,
            Constants.Network,
            Constants.Network,
            "tmdb:network:");
    }

    private static IReadOnlyList<SubjectRelationshipDefinition> Project(
        IReadOnlyList<ProductionCompanyDto>? companies,
        string imageBaseUrl,
        int startOrdinal,
        DateTime utcNow,
        string relationshipKind,
        string subjectKind,
        string sourcePrefix)
    {
        if (companies is null || companies.Count == 0)
        {
            return [];
        }

        var result = new List<SubjectRelationshipDefinition>();
        var emitted = 0;
        foreach (var company in companies)
        {
            if (emitted >= StudioCap)
            {
                break;
            }

            if (company is null || company.Id <= 0 || string.IsNullOrWhiteSpace(company.Name))
            {
                continue;
            }

            var idString = company.Id.ToString(CultureInfo.InvariantCulture);
            var contribution = BuildContribution(
                idString,
                company.Name!,
                company.LogoPath,
                imageBaseUrl,
                utcNow,
                sourcePrefix);
            if (!string.IsNullOrWhiteSpace(company.OriginCountry))
            {
                contribution.CustomMetadata.Add(new CustomMetadataValue
                {
                    Key = "originCountry",
                    Value = company.OriginCountry!.Trim()
                });
            }

            result.Add(new SubjectRelationshipDefinition
            {
                RelationshipKind = relationshipKind,
                RelationshipOrdinal = startOrdinal + emitted,
                SubjectKind = subjectKind,
                SubjectName = company.Name!.Trim(),
                SubjectExternalIds = new Dictionary<string, string> { [Constants.ProviderType] = idString },
                InitialSubjectContribution = contribution
            });
            emitted++;
        }

        return result;
    }

    private static MetadataContribution BuildContribution(
        string idString,
        string name,
        string? logoPath,
        string imageBaseUrl,
        DateTime utcNow,
        string sourcePrefix)
    {
        var contribution = new MetadataContribution
        {
            ProviderType = Constants.ProviderType,
            Source = sourcePrefix + idString,
            Title = name.Trim(),
            DateLastRefreshed = utcNow
        };

        contribution.ExternalIds.Add(new ExternalId
        {
            Source = Constants.ProviderType,
            Value = idString,
            Ordinal = 0
        });

        var logo = logoPath.ResolveImageUrl(imageBaseUrl, "w185");
        if (logo is not null)
        {
            contribution.Images.Add(new Image
            {
                Type = "logo",
                Url = logo,
                Source = Constants.ProviderType,
                Ordinal = 0
            });
        }

        return contribution;
    }

    public static ChildHierarchyDefinition? ToChildHierarchyDefinition(
        this TvSeasonSummaryDto? season,
        string imageBaseUrl,
        IReadOnlyList<ChildHierarchyDefinition> episodes,
        ExternalIdsDto? externalIds = null)
    {
        if (season is null || season.Id <= 0)
        {
            return null;
        }

        var idString = season.Id.ToString(CultureInfo.InvariantCulture);
        var slug = string.Format(CultureInfo.InvariantCulture, "season-{0}", season.SeasonNumber);
        var name = string.IsNullOrWhiteSpace(season.Name)
            ? (season.SeasonNumber == 0
                ? "Specials"
                : string.Format(CultureInfo.InvariantCulture, "Season {0}", season.SeasonNumber))
            : season.Name!;

        return new ChildHierarchyDefinition
        {
            Level = SeasonLevelSlug,
            Name = name,
            Slug = slug,
            Ordinal = season.SeasonNumber,
            AlignmentKey = string.Format(CultureInfo.InvariantCulture, "season:{0}", season.SeasonNumber),
            ExternalIds = new Dictionary<string, string>
            {
                [Constants.ProviderType] = idString
            },
            InitialContribution = BuildSeasonContribution(season, imageBaseUrl, idString, externalIds),
            Children = episodes
        };
    }

    public static IReadOnlyList<ChildHierarchyDefinition> ToEpisodeDefinitions(
        this SeasonDetailDto? detail,
        string imageBaseUrl)
    {
        if (detail?.Episodes is null || detail.Episodes.Count == 0)
        {
            return [];
        }

        var result = new List<ChildHierarchyDefinition>(detail.Episodes.Count);
        foreach (var episode in detail.Episodes)
        {
            var def = BuildEpisodeDefinition(episode, imageBaseUrl);
            if (def is not null)
            {
                result.Add(def);
            }
        }

        return result;
    }

    public static MetadataContribution? ToSeasonContribution(this SeasonDetailDto? detail, string imageBaseUrl)
    {
        if (detail is null || detail.Id <= 0)
        {
            return null;
        }

        var summary = new TvSeasonSummaryDto
        {
            Id = detail.Id,
            SeasonNumber = detail.SeasonNumber,
            Name = detail.Name,
            Overview = detail.Overview,
            AirDate = detail.AirDate,
            PosterPath = detail.PosterPath
        };
        return BuildSeasonContribution(
            summary,
            imageBaseUrl,
            detail.Id.ToString(CultureInfo.InvariantCulture),
            detail.ExternalIds,
            detail.Images);
    }

    public static MetadataContribution? ToEpisodeContribution(this EpisodeDto? episode, string imageBaseUrl)
    {
        if (episode is null || episode.Id <= 0)
        {
            return null;
        }

        return BuildEpisodeContribution(episode, imageBaseUrl, episode.Id.ToString(CultureInfo.InvariantCulture));
    }

    public static CreditsDto? ToEpisodeCredits(this EpisodeDto? episode)
    {
        if (episode is null)
        {
            return null;
        }

        var cast = new List<CastMemberDto>();
        if (episode.Credits?.Cast is not null)
        {
            cast.AddRange(episode.Credits.Cast);
        }

        if (episode.GuestStars is not null)
        {
            cast.AddRange(episode.GuestStars);
        }

        var crew = new List<CrewMemberDto>();
        if (episode.Credits?.Crew is not null)
        {
            crew.AddRange(episode.Credits.Crew);
        }

        if (episode.Crew is not null)
        {
            crew.AddRange(episode.Crew);
        }

        if (cast.Count == 0 && crew.Count == 0)
        {
            return null;
        }

        return new CreditsDto { Cast = cast, Crew = crew };
    }

    private static ChildHierarchyDefinition? BuildEpisodeDefinition(EpisodeDto? episode, string imageBaseUrl)
    {
        if (episode is null || episode.Id <= 0)
        {
            return null;
        }

        var idString = episode.Id.ToString(CultureInfo.InvariantCulture);
        var slug = string.Format(
            CultureInfo.InvariantCulture,
            "s{0:D2}e{1:D2}",
            episode.SeasonNumber,
            episode.EpisodeNumber);
        var name = string.IsNullOrWhiteSpace(episode.Name)
            ? string.Format(CultureInfo.InvariantCulture, "Episode {0}", episode.EpisodeNumber)
            : episode.Name!;

        return new ChildHierarchyDefinition
        {
            Level = EpisodeLevelSlug,
            Name = name,
            Slug = slug,
            Ordinal = episode.EpisodeNumber,
            AlignmentKey = string.Format(
                CultureInfo.InvariantCulture,
                "episode:{0}:{1}",
                episode.SeasonNumber,
                episode.EpisodeNumber),
            ExternalIds = new Dictionary<string, string>
            {
                [Constants.ProviderType] = idString
            },
            InitialContribution = BuildEpisodeContribution(episode, imageBaseUrl, idString)
        };
    }

    private static MetadataContribution BuildSeasonContribution(
        TvSeasonSummaryDto season,
        string imageBaseUrl,
        string idString,
        ExternalIdsDto? externalIds = null,
        ImagesPayloadDto? images = null)
    {
        var contribution = new MetadataContribution
        {
            ProviderType = Constants.ProviderType,
            Source = string.Format(CultureInfo.InvariantCulture, "tmdb:season:{0}", idString),
            Title = season.Name.NullIfBlank(),
            Overview = season.Overview.NullIfBlank(),
            PublishDate = season.AirDate.ParseDateOnly(),
            SequenceNumber = season.SeasonNumber
        };
        contribution.ExternalIds.Add(new ExternalId
        {
            Source = Constants.ProviderType,
            Value = idString,
            Ordinal = 0
        });
        AddPayloadExternalIds(contribution, externalIds);

        var seasonImages = new List<Image>();
        var poster = season.PosterPath.ResolveImageUrl(imageBaseUrl, "w500");
        if (poster is not null)
        {
            seasonImages.Add(new Image
            {
                Type = PosterType,
                Url = poster,
                Source = Constants.ProviderType,
                Ordinal = 0
            });
        }

        seasonImages.AppendGallery(
            imageBaseUrl,
            images?.Posters,
            PosterType,
            PosterSize,
            season.PosterPath);
        foreach (var image in seasonImages)
        {
            contribution.Images.Add(image);
        }

        return contribution;
    }

    private static MetadataContribution BuildEpisodeContribution(
        EpisodeDto episode,
        string imageBaseUrl,
        string idString)
    {
        var contribution = new MetadataContribution
        {
            ProviderType = Constants.ProviderType,
            Source = string.Format(CultureInfo.InvariantCulture, "tmdb:episode:{0}", idString),
            Title = episode.Name.NullIfBlank(),
            Overview = episode.Overview.NullIfBlank(),
            PublishDate = episode.AirDate.ParseDateOnly(),
            SequenceNumber = episode.EpisodeNumber
        };
        contribution.ExternalIds.Add(new ExternalId
        {
            Source = Constants.ProviderType,
            Value = idString,
            Ordinal = 0
        });
        AddPayloadExternalIds(contribution, episode.ExternalIds);

        if (episode.Runtime is int runtimeMinutes && runtimeMinutes > 0)
        {
            contribution.Duration = TimeSpan.FromMinutes(runtimeMinutes);
        }

        var episodeImages = new List<Image>();
        var still = episode.StillPath.ResolveImageUrl(imageBaseUrl, StillSize);
        if (still is not null)
        {
            episodeImages.Add(new Image
            {
                Type = StillType,
                Url = still,
                Source = Constants.ProviderType,
                Ordinal = 0
            });
        }

        episodeImages.AppendGallery(
            imageBaseUrl,
            episode.Images?.Stills,
            StillType,
            StillSize,
            episode.StillPath);
        foreach (var image in episodeImages)
        {
            contribution.Images.Add(image);
        }

        return contribution;
    }

    private static void AddPayloadExternalIds(MetadataContribution contribution, ExternalIdsDto? externalIds)
    {
        if (externalIds is null)
        {
            return;
        }

        if (externalIds.ImdbId.NullIfBlank() is { } imdbId)
        {
            contribution.ExternalIds.Add(new ExternalId
            {
                Source = "imdb",
                Value = imdbId,
                Ordinal = contribution.ExternalIds.Count
            });
        }

        if (externalIds.TvdbId is > 0 and var tvdbId)
        {
            contribution.ExternalIds.Add(new ExternalId
            {
                Source = "tvdb",
                Value = tvdbId.ToString(CultureInfo.InvariantCulture),
                Ordinal = contribution.ExternalIds.Count
            });
        }

        if (externalIds.WikidataId.NullIfBlank() is { } wikidataId)
        {
            contribution.ExternalIds.Add(new ExternalId
            {
                Source = "wikidata",
                Value = wikidataId,
                Ordinal = contribution.ExternalIds.Count
            });
        }
    }

    public static MetadataContribution ToContribution(this TvDetailDto dto, string imageBaseUrl, DateTime utcNow)
    {
        return "tv".ToContribution(
            dto.Id,
            imageBaseUrl,
            utcNow,
            dto.Name,
            dto.OriginalName,
            dto.Overview,
            dto.FirstAirDate.ParseDateOnly(),
            ResolveShowRuntime(dto.EpisodeRunTime),
            dto.OriginalLanguage,
            dto.Status,
            dto.ExternalIds,
            dto.PosterPath,
            dto.BackdropPath,
            dto.Homepage,
            dto.Genres,
            dto.Images,
            dto.AlternativeTitles.ToAlternateTexts(),
            dto.ContentRatings.ToContentRatings(),
            dto.Videos.ToPreviews());
    }

    private static TimeSpan? ResolveShowRuntime(IReadOnlyList<int>? runtimes)
    {
        if (runtimes is null || runtimes.Count == 0)
        {
            return null;
        }

        var first = runtimes[0];
        return first > 0 ? TimeSpan.FromMinutes(first) : null;
    }

    public static IReadOnlyList<Preview> ToPreviews(this VideosDto? payload)
    {
        if (payload?.Results is null || payload.Results.Count == 0)
        {
            return [];
        }

        var candidates = new List<(int Rank, int Index, VideoDto Video, string Kind)>();
        var index = 0;
        foreach (var video in payload.Results)
        {
            var current = index++;
            if (video is null || string.IsNullOrWhiteSpace(video.Key))
            {
                continue;
            }

            if (!string.Equals(video.Site, YouTubeSite, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var kind = NormalizeKind(video.Type);
            if (kind is null)
            {
                continue;
            }

            var rank = kind switch
            {
                "trailer" => video.Official ? 0 : 1,
                _ => video.Official ? 2 : 3
            };
            candidates.Add((rank, current, video, kind));
        }

        if (candidates.Count == 0)
        {
            return [];
        }

        candidates.Sort((a, b) =>
        {
            var byRank = a.Rank.CompareTo(b.Rank);
            return byRank != 0 ? byRank : a.Index.CompareTo(b.Index);
        });

        var previews = new List<Preview>(candidates.Count);
        var ordinal = 0;
        foreach (var (_, _, video, kind) in candidates)
        {
            var url = BuildYouTubeUrl(video.Key!);
            if (url is null)
            {
                continue;
            }

            previews.Add(new Preview
            {
                Kind = kind,
                EmbedKind = YouTubeEmbedKind,
                Url = url,
                Source = Constants.ProviderType,
                Title = video.Name.NullIfBlank(),
                Language = video.Iso6391.NullIfBlank(),
                ReleaseDate = ParseTimestamp(video.PublishedAt),
                Ordinal = ordinal++
            });
        }

        return previews;
    }

    private static string? NormalizeKind(string? type)
    {
        if (string.IsNullOrWhiteSpace(type))
        {
            return null;
        }

        if (string.Equals(type, "Trailer", StringComparison.OrdinalIgnoreCase))
        {
            return "trailer";
        }

        if (string.Equals(type, "Teaser", StringComparison.OrdinalIgnoreCase))
        {
            return "teaser";
        }

        return null;
    }

    private static Uri? BuildYouTubeUrl(string key)
    {
        var composed = "https://www.youtube.com/watch?v=" + Uri.EscapeDataString(key.Trim());
        return Uri.TryCreate(composed, UriKind.Absolute, out var uri) ? uri : null;
    }

    private static DateTime? ParseTimestamp(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (DateTime.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var parsed))
        {
            return parsed;
        }

        return null;
    }
}
