using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Text.Json;

using Magpie.Extensions.Wikidata.QueryEngine;
using Magpie.Models;
using Magpie.Models.Data;
using Magpie.Models.Filtering;

namespace Magpie.Extensions.Wikidata.Mapping;

internal static class EntityMappingExtensions
{
    internal const string GregorianCalendarQid = "Q1985727";

    internal static bool TryNormalizeQid(this string? value, [NotNullWhen(true)] out string? qid)
    {
        qid = value.ToQid();
        return qid is not null;
    }

    internal static string? ToQid(this string? value)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrEmpty(normalized))
        {
            return null;
        }

        var slash = normalized.LastIndexOf('/');
        normalized = normalized.Substring(slash + 1);
        if (normalized.Length < 2 ||
            normalized[0] is not ('Q' or 'q') ||
            normalized[1] == '0' ||
            normalized.Skip(1).Any(character => character is < '0' or > '9'))
        {
            return null;
        }

        return "Q" + normalized.Substring(1);
    }

    internal static string? ToPrimaryLanguage(this string value)
    {
        var trimmed = value.Trim();
        var separator = trimmed.IndexOf('-');
        var primary = separator > 0 ? trimmed.Substring(0, separator) : trimmed;
        return primary.Length is >= 2 and <= 12 && primary.All(char.IsLetter)
            ? primary.ToLowerInvariant()
            : null;
    }

    public static bool MatchesKind(this EntityDto entity, EntityKind kind)
    {
        if (kind == EntityKind.Studio)
        {
            return !string.IsNullOrWhiteSpace(entity.Id);
        }

        var accepted = SparqlQueryBuilder.AcceptedTypes(kind);
        var hasAcceptedType = entity.EntityClaimQids(Constants.InstanceOfPropertyId)
            .Any(value => accepted.Contains(value, StringComparer.Ordinal));
        return hasAcceptedType &&
            kind switch
            {
                EntityKind.Season => entity.EntityClaimQids(Constants.PartOfSeriesPropertyId).Any(),
                EntityKind.Episode => entity.EntityClaimQids(Constants.PartOfSeriesPropertyId).Any() &&
                    entity.EntityClaimQids(Constants.SeasonPropertyId).Any(),
                _ => true
            };
    }

    public static IReadOnlyList<string> RequiredSupportQids(this EntityDto entity)
    {
        var result = new List<string>();
        result.AddRange(entity.EntityClaimQids(Constants.OriginalLanguagePropertyId));
        result.AddRange(entity.EntityClaimQids(Constants.GenrePropertyId).Take(30));
        foreach (var statement in entity.TruthyStatements(Constants.AwardReceivedPropertyId).Take(30))
        {
            var awardQid = statement.Mainsnak.EntityId();
            if (awardQid is not null)
            {
                result.Add(awardQid);
            }

            result.AddRange(
                RelationshipMappingExtensions.QualifierEntityIds(statement, Constants.ConferredByPropertyId));
        }

        foreach (var (propertyId, _) in Constants.RatingSystems)
        {
            result.AddRange(entity.EntityClaimQids(propertyId));
        }

        return [.. result.Distinct(StringComparer.OrdinalIgnoreCase)];
    }

    public static IReadOnlyList<string> RequiredAwardBodyQids(
        this EntityDto entity,
        IReadOnlyDictionary<string, EntityDto> supportEntities)
    {
        var result = new List<string>();
        foreach (var awardQid in entity.EntityClaimQids(Constants.AwardReceivedPropertyId).Take(30))
        {
            if (supportEntities.TryGetValue(awardQid, out var award))
            {
                result.AddRange(award.EntityClaimQids(Constants.ConferredByPropertyId).Take(1));
            }
        }

        return
        [
            .. result.Where(qid => !supportEntities.ContainsKey(qid)).Distinct(StringComparer.OrdinalIgnoreCase)
        ];
    }

    public static string? FindParentQid(this EntityDto entity, EntityKind kind)
    {
        var propertyId = kind == EntityKind.Episode
            ? Constants.SeasonPropertyId
            : Constants.PartOfSeriesPropertyId;
        return entity.EntityClaimQids(propertyId).FirstOrDefault();
    }

    public static MetadataContribution? ToMetadataContribution(
        this EntityDto entity,
        EntityKind kind,
        string language,
        IReadOnlyDictionary<string, EntityDto> supportEntities,
        DateTime utcNow,
        string? parentQid = null)
    {
        if (!entity.MatchesKind(kind) || !entity.Id.TryNormalizeQid(out var qid))
        {
            return null;
        }

        var contribution = new MetadataContribution
        {
            ProviderType = Constants.ProviderType,
            Source = "wikidata:" + KindSlug(kind) + ":" + qid,
            Title = entity.Labels.Resolve(language),
            Overview = entity.Descriptions.Resolve(language),
            DateLastRefreshed = utcNow
        };
        AddExternalId(contribution, Constants.ProviderType, qid, 0);

        switch (kind)
        {
            case EntityKind.Movie:
                MapMovie(contribution, entity, language, supportEntities);
                break;
            case EntityKind.Show:
                MapShow(contribution, entity, language, supportEntities);
                break;
            case EntityKind.Season:
                MapSeason(contribution, entity, parentQid);
                break;
            case EntityKind.Episode:
                MapEpisode(contribution, entity, language, supportEntities, parentQid);
                break;
            case EntityKind.Person:
                MapPerson(contribution, entity, language, supportEntities);
                break;
            case EntityKind.Studio:
                MapStudio(contribution, entity, language, supportEntities);
                break;
        }

        if (kind is EntityKind.Movie
            or EntityKind.Show
            or EntityKind.Season
            or EntityKind.Episode)
        {
            AddTrailerPreviews(contribution, entity);
        }

        AddOfficialWebsiteReferences(contribution, entity);
        return contribution;
    }

    public static IReadOnlyList<ChildHierarchyDefinition> ToChildDefinitions(
        this IReadOnlyList<HierarchyRow> rows,
        int? seasonNumber,
        DateTime utcNow)
    {
        var children = new List<ChildHierarchyDefinition>(rows.Count);
        foreach (var row in rows)
        {
            if (row.Ordinal <= 0 || row.EntityKind == EntityKind.Episode && seasonNumber is not > 0)
            {
                continue;
            }

            var contribution = ToHierarchyContribution(row, utcNow);
            var isSeason = row.EntityKind == EntityKind.Season;
            var slug = isSeason
                ? "season-" + row.Ordinal.ToString(CultureInfo.InvariantCulture)
                : string.Format(CultureInfo.InvariantCulture, "s{0:D2}e{1:D2}", seasonNumber, row.Ordinal);
            var alignment = isSeason
                ? "season:" + row.Ordinal.ToString(CultureInfo.InvariantCulture)
                : string.Format(CultureInfo.InvariantCulture, "episode:{0}:{1}", seasonNumber, row.Ordinal);
            children.Add(new ChildHierarchyDefinition
            {
                Level = isSeason ? Constants.SeasonLevel : Constants.EpisodeLevel,
                Name = row.Title,
                Slug = slug,
                Ordinal = row.Ordinal,
                AlignmentKey = alignment,
                ExternalIds = row.ExternalIds,
                InitialContribution = contribution
            });
        }

        return children;
    }

    private static void MapMovie(
        MetadataContribution contribution,
        EntityDto entity,
        string language,
        IReadOnlyDictionary<string, EntityDto> supportEntities)
    {
        var originalLanguage = ResolveOriginalLanguage(entity, supportEntities);
        contribution.Language = originalLanguage;
        contribution.OriginalTitle = ResolveMonolingualClaim(
            entity,
            Constants.OriginalTitlePropertyId,
            originalLanguage);
        var published = EarliestTime(entity, Constants.PublicationDatePropertyId);
        contribution.PublishDate = published?.PeriodStart();
        contribution.PublishDatePrecision = published?.Precision;
        contribution.Duration = ResolveDuration(entity);
        AddGenres(contribution, entity, supportEntities, language);
        AddAwards(contribution, entity, supportEntities, language);
        AddContentRatings(contribution, entity, supportEntities, language);
        AddExternalId(
            contribution,
            Constants.ImdbSource,
            entity.StringClaimValues(Constants.ImdbPropertyId).FirstOrDefault());
        AddExternalId(
            contribution,
            Constants.TmdbSource,
            entity.StringClaimValues(Constants.TmdbMoviePropertyId).FirstOrDefault());
        AddAliases(contribution, entity, language);
        AddContentArtwork(contribution, entity);
    }

    private static void MapShow(
        MetadataContribution contribution,
        EntityDto entity,
        string language,
        IReadOnlyDictionary<string, EntityDto> supportEntities)
    {
        var originalLanguage = ResolveOriginalLanguage(entity, supportEntities);
        contribution.Language = originalLanguage;
        contribution.OriginalTitle = ResolveMonolingualClaim(
            entity,
            Constants.OriginalTitlePropertyId,
            originalLanguage);
        var start = EarliestTime(entity, Constants.SeriesStartPropertyId);
        var end = EarliestTime(entity, Constants.SeriesEndPropertyId);
        contribution.PublishDate = start?.PeriodStart();
        contribution.PublishDatePrecision = start?.Precision;
        contribution.Established = start?.PeriodStart();
        contribution.Dissolved = end?.PeriodStart();
        contribution.ProductionStatus = end is null ? null : Constants.EndedProductionStatus;
        contribution.Duration = ResolveDuration(entity);
        AddGenres(contribution, entity, supportEntities, language);
        AddAwards(contribution, entity, supportEntities, language);
        AddContentRatings(contribution, entity, supportEntities, language);
        AddExternalId(
            contribution,
            Constants.ImdbSource,
            entity.StringClaimValues(Constants.ImdbPropertyId).FirstOrDefault());
        AddExternalId(
            contribution,
            Constants.TmdbSource,
            entity.StringClaimValues(Constants.TmdbShowPropertyId).FirstOrDefault());
        AddExternalId(
            contribution,
            Constants.TvdbSource,
            entity.StringClaimValues(Constants.TvdbShowPropertyId).FirstOrDefault());
        AddAliases(contribution, entity, language);
        AddContentArtwork(contribution, entity);
    }

    private static void MapSeason(
        MetadataContribution contribution,
        EntityDto entity,
        string? parentQid)
    {
        contribution.SequenceNumber = ResolveOrdinal(
            entity,
            Constants.PartOfSeriesPropertyId,
            parentQid);
        var start = EarliestTime(entity, Constants.SeriesStartPropertyId);
        var end = EarliestTime(entity, Constants.SeriesEndPropertyId);
        contribution.PublishDate = start?.PeriodStart();
        contribution.PublishDatePrecision = start?.Precision;
        contribution.Established = start?.PeriodStart();
        contribution.Dissolved = end?.PeriodStart();
        AddExternalId(
            contribution,
            Constants.TmdbSource,
            entity.StringClaimValues(Constants.TmdbSeasonPropertyId).FirstOrDefault());
        AddExternalId(
            contribution,
            Constants.TvdbSource,
            entity.StringClaimValues(Constants.TvdbSeasonPropertyId).FirstOrDefault());
        AddContentArtwork(contribution, entity);
    }

    private static void MapEpisode(
        MetadataContribution contribution,
        EntityDto entity,
        string language,
        IReadOnlyDictionary<string, EntityDto> supportEntities,
        string? parentQid)
    {
        var originalLanguage = ResolveOriginalLanguage(entity, supportEntities);
        contribution.Language = originalLanguage;
        contribution.OriginalTitle = ResolveMonolingualClaim(
            entity,
            Constants.OriginalTitlePropertyId,
            originalLanguage ?? language);
        contribution.SequenceNumber = ResolveOrdinal(entity, Constants.SeasonPropertyId, parentQid);
        var published = EarliestTime(entity, Constants.PublicationDatePropertyId);
        contribution.PublishDate = published?.PeriodStart();
        contribution.PublishDatePrecision = published?.Precision;
        contribution.Duration = ResolveDuration(entity);
        var productionCode = entity.StringClaimValues(Constants.ProductionCodePropertyId).FirstOrDefault();
        if (productionCode is not null)
        {
            contribution.CustomMetadata.Add(new CustomMetadataValue { Key = "productionCode", Value = productionCode });
        }

        AddAwards(contribution, entity, supportEntities, language);
        AddExternalId(
            contribution,
            Constants.ImdbSource,
            entity.StringClaimValues(Constants.ImdbPropertyId).FirstOrDefault());
        AddExternalId(
            contribution,
            Constants.TmdbSource,
            entity.StringClaimValues(Constants.TmdbEpisodePropertyId).FirstOrDefault());
        AddExternalId(
            contribution,
            Constants.TvdbSource,
            entity.StringClaimValues(Constants.TvdbEpisodePropertyId).FirstOrDefault());
        contribution.Images.AddImage(
            entity.StringClaimValues(Constants.ImagePropertyId).FirstOrDefault(),
            "still",
            primary: true);
    }

    private static void MapPerson(
        MetadataContribution contribution,
        EntityDto entity,
        string language,
        IReadOnlyDictionary<string, EntityDto> supportEntities)
    {
        contribution.BirthDate = EarliestTime(entity, Constants.BirthDatePropertyId)?.PeriodStart();
        contribution.DeathDate = EarliestTime(entity, Constants.DeathDatePropertyId)?.PeriodStart();
        AddAwards(contribution, entity, supportEntities, language);
        AddExternalId(
            contribution,
            Constants.ImdbSource,
            entity.StringClaimValues(Constants.ImdbPropertyId).FirstOrDefault());
        AddExternalId(
            contribution,
            Constants.TmdbSource,
            entity.StringClaimValues(Constants.TmdbPersonPropertyId).FirstOrDefault());
        AddAliases(contribution, entity, language);
        contribution.Images.AddImage(
            entity.StringClaimValues(Constants.ImagePropertyId).FirstOrDefault(),
            "profile",
            primary: true);
    }

    private static void MapStudio(
        MetadataContribution contribution,
        EntityDto entity,
        string language,
        IReadOnlyDictionary<string, EntityDto> supportEntities)
    {
        contribution.OriginalTitle = ResolveMonolingualClaim(
            entity,
            Constants.OfficialNamePropertyId,
            language);
        contribution.Established = EarliestTime(entity, Constants.InceptionPropertyId)?.PeriodStart();
        contribution.Dissolved = EarliestTime(entity, Constants.DissolutionPropertyId)?.PeriodStart();
        AddAwards(contribution, entity, supportEntities, language);
        AddExternalId(
            contribution,
            Constants.TmdbSource,
            entity.StringClaimValues(Constants.TmdbCompanyPropertyId).FirstOrDefault());
        contribution.Images.AddImage(
            entity.StringClaimValues(Constants.LogoPropertyId).FirstOrDefault(),
            "logo",
            primary: true);
        contribution.Images.AddImage(
            entity.StringClaimValues(Constants.ImagePropertyId).FirstOrDefault(),
            "backdrop",
            primary: false);
    }

    private static MetadataContribution ToHierarchyContribution(HierarchyRow row, DateTime utcNow)
    {
        var contribution = new MetadataContribution
        {
            ProviderType = Constants.ProviderType,
            Source = "wikidata:" + KindSlug(row.EntityKind) + ":" + row.Qid,
            Title = row.Title,
            Overview = row.Overview,
            PublishDate = row.PublishDate?.PeriodStart(),
            PublishDatePrecision = row.PublishDate?.Precision,
            SequenceNumber = row.Ordinal,
            DateLastRefreshed = utcNow
        };
        var ordinal = 0;
        foreach (var external in row.ExternalIds.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            AddExternalId(contribution, external.Key, external.Value, ordinal++);
        }

        contribution.Images.AddImage(
            row.ImageFilename,
            row.EntityKind == EntityKind.Season ? "poster" : "still",
            primary: true);
        return contribution;
    }

    private static void AddGenres(
        MetadataContribution contribution,
        EntityDto entity,
        IReadOnlyDictionary<string, EntityDto> supportEntities,
        string language)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var qid in entity.EntityClaimQids(Constants.GenrePropertyId).Take(30))
        {
            if (!supportEntities.TryGetValue(qid, out var genre))
            {
                continue;
            }

            var label = genre.Labels.Resolve(language);
            if (label is null || !seen.Add(label))
            {
                continue;
            }

            contribution.Genres.Add(new Genre { Name = label, Ordinal = contribution.Genres.Count });
        }
    }

    public static IReadOnlyList<AwardCandidate> ToAwardCandidates(this EntityDto entity)
    {
        var result = new List<AwardCandidate>();
        foreach (var (statement, sourceStatementOrdinal) in entity
            .TruthyStatements(Constants.AwardReceivedPropertyId)
            .Take(30)
            .Select((statement, index) => (statement, index)))
        {
            var awardQid = statement.Mainsnak.EntityId();
            if (awardQid is not null)
            {
                result.Add(new AwardCandidate(
                    awardQid,
                    AwardYear(statement),
                    sourceStatementOrdinal,
                    statement));
            }
        }

        return result;
    }

    public static IReadOnlyList<MappedEntityValue<Award>> ToAwards(
        this EntityDto entity,
        IReadOnlyDictionary<string, EntityDto> supportEntities,
        string language)
    {
        var result = new List<MappedEntityValue<Award>>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in entity.ToAwardCandidates())
        {
            if (!supportEntities.TryGetValue(candidate.EntityQid, out var award))
            {
                continue;
            }

            var name = award.Labels.Resolve(language);
            if (name is null)
            {
                continue;
            }

            if (!seen.Add(name + "|" + candidate.Year?.ToString(CultureInfo.InvariantCulture)))
            {
                continue;
            }

            result.Add(new MappedEntityValue<Award>(
                candidate.EntityQid,
                new Award
                {
                    Body = ResolveAwardBody(candidate.Statement, award, supportEntities, language) ?? name,
                    Name = name,
                    Status = Constants.WonAwardStatus,
                    Year = candidate.Year,
                    Ordinal = result.Count
                },
                candidate.SourceStatementOrdinal));
        }

        return result;
    }

    private static void AddAwards(
        MetadataContribution contribution,
        EntityDto entity,
        IReadOnlyDictionary<string, EntityDto> supportEntities,
        string language)
    {
        foreach (var mapped in entity.ToAwards(supportEntities, language))
        {
            contribution.Awards.Add(mapped.Value);
        }
    }

    private static string? ResolveAwardBody(
        StatementDto statement,
        EntityDto award,
        IReadOnlyDictionary<string, EntityDto> supportEntities,
        string language)
    {
        var bodyQids = RelationshipMappingExtensions.QualifierEntityIds(statement, Constants.ConferredByPropertyId)
            .Concat(award.EntityClaimQids(Constants.ConferredByPropertyId));
        foreach (var bodyQid in bodyQids)
        {
            if (supportEntities.TryGetValue(bodyQid, out var body))
            {
                var label = body.Labels.Resolve(language);
                if (label is not null)
                {
                    return label;
                }
            }
        }

        return null;
    }

    private static int? AwardYear(StatementDto statement)
    {
        if (statement.Qualifiers is null ||
            !statement.Qualifiers.TryGetValue(Constants.PointInTimePropertyId, out var qualifiers))
        {
            return null;
        }

        return (from qualifier in qualifiers
                let value = ParseTime(qualifier?.DataValue?.Value)
                where value is not null
                select (int?)value.Value.Year).FirstOrDefault();
    }

    public static IReadOnlyList<ContentRatingCandidate> ToContentRatingCandidates(this EntityDto entity)
    {
        var result = new List<ContentRatingCandidate>();
        foreach (var (propertyId, body) in Constants.RatingSystems)
        {
            result.AddRange(entity.EntityClaimQids(propertyId)
                .Select(qid => new ContentRatingCandidate(propertyId, body, qid)));
        }

        return result;
    }

    public static IReadOnlyList<MappedEntityValue<ContentRating>> ToContentRatings(
        this EntityDto entity,
        IReadOnlyDictionary<string, EntityDto> supportEntities,
        string language)
    {
        var result = new List<MappedEntityValue<ContentRating>>();
        var selectedSystems = new HashSet<string>(StringComparer.Ordinal);
        foreach (var candidate in entity.ToContentRatingCandidates())
        {
            if (selectedSystems.Contains(candidate.PropertyId) ||
                !supportEntities.TryGetValue(candidate.EntityQid, out var rating))
            {
                continue;
            }

            var label = rating.Labels.Resolve(language);
            if (label is null)
            {
                continue;
            }

            result.Add(new MappedEntityValue<ContentRating>(
                candidate.EntityQid,
                new ContentRating
                {
                    Body = candidate.Body,
                    Rating = label,
                    Ordinal = result.Count
                }));
            selectedSystems.Add(candidate.PropertyId);
        }

        return result;
    }

    private static void AddContentRatings(
        MetadataContribution contribution,
        EntityDto entity,
        IReadOnlyDictionary<string, EntityDto> supportEntities,
        string language)
    {
        foreach (var mapped in entity.ToContentRatings(supportEntities, language))
        {
            contribution.ContentRatings.Add(mapped.Value);
        }
    }

    private static void AddOfficialWebsiteReferences(MetadataContribution contribution, EntityDto entity)
    {
        foreach (var website in entity.TruthyStatements(Constants.OfficialWebsitePropertyId)
            .Select(statement => statement.Mainsnak.StringValue()))
        {
            if (!Uri.TryCreate(website, UriKind.Absolute, out var uri) ||
                uri.Scheme is not ("http" or "https") ||
                contribution.References.Any(reference => reference.Url == uri))
            {
                continue;
            }

            contribution.References.Add(new Reference
            {
                Kind = "official",
                Source = Constants.ProviderType,
                Url = uri,
                Ordinal = contribution.References.Count
            });
        }
    }

    private static void AddTrailerPreviews(MetadataContribution contribution, EntityDto entity)
    {
        foreach (var videoId in entity.TruthyStatements(Constants.TrailerPropertyId)
            .Select(statement => statement.Mainsnak.StringValue()))
        {
            var uri = YouTubeUri(videoId);
            if (uri is null || contribution.Previews.Any(preview => preview.Url == uri))
            {
                continue;
            }

            contribution.Previews.Add(new Preview
            {
                Kind = "trailer",
                EmbedKind = "youtube",
                Url = uri,
                Source = Constants.ProviderType,
                Ordinal = contribution.Previews.Count
            });
        }
    }

    private static string? ResolveOriginalLanguage(
        EntityDto entity,
        IReadOnlyDictionary<string, EntityDto> supportEntities)
    {
        foreach (var qid in entity.EntityClaimQids(Constants.OriginalLanguagePropertyId))
        {
            if (supportEntities.TryGetValue(qid, out var language))
            {
                var code = language.StringClaimValues(Constants.LanguageCodePropertyId).FirstOrDefault();
                if (IsLanguageCode(code))
                {
                    return code.Trim().ToLowerInvariant();
                }
            }
        }

        return null;
    }

    private static void AddContentArtwork(MetadataContribution contribution, EntityDto entity)
    {
        foreach (var image in entity.StringClaimValues(Constants.PosterPropertyId).FirstOrDefault()
                     .ToContentArtwork(
                         entity.StringClaimValues(Constants.ImagePropertyId).FirstOrDefault(),
                         entity.StringClaimValues(Constants.LogoPropertyId).FirstOrDefault()))
        {
            contribution.Images.Add(image);
        }
    }

    private static Uri? YouTubeUri(string? videoId)
    {
        if (string.IsNullOrWhiteSpace(videoId))
        {
            return null;
        }

        var composed = "https://www.youtube.com/watch?v=" + Uri.EscapeDataString(videoId.Trim());
        return Uri.TryCreate(composed, UriKind.Absolute, out var uri) ? uri : null;
    }

    private static void AddAliases(MetadataContribution contribution, EntityDto entity, string language)
    {
        foreach (var alternateText in entity.ToAlternateTexts(language, includeAllAvailableLanguages: false))
        {
            contribution.AlternateTexts.Add(alternateText);
        }
    }

    internal static IReadOnlyList<AlternateText> ToAlternateTexts(
        this EntityDto entity,
        string language,
        bool includeAllAvailableLanguages)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var title = entity.Labels.Resolve(language);
        if (!includeAllAvailableLanguages && title is not null)
        {
            seen.Add(title);
        }

        var aliases = includeAllAvailableLanguages
            ? entity.Aliases.AllAliases()
            : entity.Aliases.Aliases(language);
        var result = new List<AlternateText>();
        foreach (var alias in aliases)
        {
            var identity = includeAllAvailableLanguages ? alias.Language + "\0" + alias.Value : alias.Value;
            if (!seen.Add(identity!))
            {
                continue;
            }

            result.Add(new AlternateText
            {
                Kind = "Title",
                Language = alias.Language,
                Text = alias.Value!,
                Ordinal = result.Count
            });
        }

        return result;
    }

    private static void AddExternalId(
        MetadataContribution contribution,
        string source,
        string? value,
        int? ordinal = null)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            contribution.ExternalIds.Any(external => string.Equals(
                external.Source,
                source,
                StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        contribution.ExternalIds.Add(new ExternalId
        {
            Source = source,
            Value = value.Trim(),
            Ordinal = ordinal ?? contribution.ExternalIds.Count
        });
    }

    private static int? ResolveOrdinal(EntityDto entity, string propertyId, string? parentQid)
    {
        foreach (var statement in entity.TruthyStatements(propertyId))
        {
            var target = statement.Mainsnak.EntityId();
            if (parentQid is not null && !string.Equals(target, parentQid, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (statement.Qualifiers is null ||
                !statement.Qualifiers.TryGetValue(Constants.SeriesOrdinalPropertyId, out var qualifiers))
            {
                continue;
            }

            var values = (from qualifier in qualifiers
                          let value = qualifier.StringValue()
                          let parsed = int.TryParse(
                              value,
                              NumberStyles.None,
                              CultureInfo.InvariantCulture,
                              out var result)
                              ? result
                              : 0
                          where parsed > 0
                          select parsed)
                .Distinct()
                .ToArray();
            if (values.Length == 1)
            {
                return values[0];
            }
        }

        return null;
    }

    private static TimeSpan? ResolveDuration(EntityDto entity)
    {
        foreach (var statement in entity.TruthyStatements(Constants.DurationPropertyId))
        {
            var value = statement.Mainsnak?.DataValue?.Value;
            if (value is not { ValueKind: JsonValueKind.Object } quantity ||
                !quantity.TryGetProperty("amount", out var amountElement) ||
                amountElement.ValueKind != JsonValueKind.String ||
                !decimal.TryParse(
                    amountElement.GetString(),
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out var amount) ||
                amount <= 0 ||
                !quantity.TryGetProperty("unit", out var unitElement))
            {
                continue;
            }

            var unitQid = unitElement.GetString().ToQid();
            var multiplier = unitQid switch
            {
                Constants.SecondUnitQid => 1m,
                Constants.MinuteUnitQid => 60m,
                Constants.HourUnitQid => 3600m,
                _ => 0m
            };
            if (multiplier == 0)
            {
                continue;
            }

            var maxAmount = TimeSpan.MaxValue.Ticks / (decimal)TimeSpan.TicksPerSecond / multiplier;
            if (amount > maxAmount)
            {
                continue;
            }

            var ticks = amount * multiplier * TimeSpan.TicksPerSecond;
            var roundedTicks = decimal.Round(ticks, 0);
            if (roundedTicks < 1)
            {
                continue;
            }

            return TimeSpan.FromTicks(decimal.ToInt64(roundedTicks));
        }

        return null;
    }

    private static TemporalValue? EarliestTime(EntityDto entity, string propertyId)
    {
        var values =
            from statement in entity.TruthyStatements(propertyId)
            let value = ParseTime(statement.Mainsnak?.DataValue?.Value)
            where value is not null
            orderby value.PeriodStart()
            select value;
        return values.FirstOrDefault();
    }

    private static TemporalValue? ParseTime(JsonElement? value)
    {
        if (value is not { ValueKind: JsonValueKind.Object } time ||
            !time.TryGetProperty("time", out var timeText) ||
            timeText.ValueKind != JsonValueKind.String ||
            !time.TryGetProperty("precision", out var precisionElement) ||
            !precisionElement.TryGetInt32(out var precision) ||
            !IsZero(time, "timezone") ||
            !IsZero(time, "before") ||
            !IsZero(time, "after") ||
            !time.TryGetProperty("calendarmodel", out var calendar) ||
            calendar.GetString().ToQid() != GregorianCalendarQid)
        {
            return null;
        }

        var text = timeText.GetString();
        if (string.IsNullOrWhiteSpace(text) || text![0] != '+')
        {
            return null;
        }

        var normalized = text.Substring(1);
        var separator = normalized.IndexOf('T');
        var date = separator > 0 ? normalized.Substring(0, separator) : normalized;
        var parts = date.Split('-');
        if (parts.Length != 3 ||
            !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var year) ||
            !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var month) ||
            !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var day) ||
            year is < 1 or > 9999)
        {
            return null;
        }

        if (precision == 9)
        {
            return new TemporalValue
            {
                Value = new(year, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                Precision = TemporalPrecision.Year
            };
        }

        if (precision == 10 && month is >= 1 and <= 12)
        {
            return new TemporalValue
            {
                Value = new(year, month, 1, 0, 0, 0, DateTimeKind.Utc),
                Precision = TemporalPrecision.Month
            };
        }

        if (precision == 11 && IsValidDate(year, month, day))
        {
            return new TemporalValue
            {
                Value = new(year, month, day, 0, 0, 0, DateTimeKind.Utc),
                Precision = TemporalPrecision.Day
            };
        }

        if (precision > 11 &&
            IsValidDate(year, month, day) &&
            DateTime.TryParse(
                normalized,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var instant))
        {
            return new TemporalValue
            {
                Value = DateTime.SpecifyKind(instant, DateTimeKind.Utc),
                Precision = TemporalPrecision.Instant
            };
        }

        return null;
    }

    private static bool IsZero(JsonElement value, string property)
    {
        return value.TryGetProperty(property, out var element) && element.TryGetInt32(out var number) && number == 0;
    }

    private static bool IsValidDate(int year, int month, int day)
    {
        return month is >= 1 and <= 12 && day >= 1 && day <= DateTime.DaysInMonth(year, month);
    }

    private static string? ResolveMonolingualClaim(EntityDto entity, string propertyId, string? language)
    {
        string? fallback = null;
        foreach (var statement in entity.TruthyStatements(propertyId))
        {
            var value = statement.Mainsnak?.DataValue?.Value;
            if (value is not { ValueKind: JsonValueKind.Object } monolingual ||
                !monolingual.TryGetProperty("text", out var text) ||
                text.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(text.GetString()))
            {
                continue;
            }

            var result = text.GetString()!.Trim();
            fallback ??= result;
            if (language is not null &&
                monolingual.TryGetProperty("language", out var code) &&
                string.Equals(code.GetString(), language, StringComparison.OrdinalIgnoreCase))
            {
                return result;
            }
        }

        return fallback;
    }

    internal static string? EntityId(this SnakDto? snak)
    {
        var value = snak is { SnakType: null or "value" } ? snak.DataValue?.Value : null;
        return value is { ValueKind: JsonValueKind.Object } element &&
            element.TryGetProperty("id", out var id) &&
            id.ValueKind == JsonValueKind.String
            ? id.GetString().ToQid()
            : null;
    }

    internal static string? StringValue(this SnakDto? snak)
    {
        var value = snak is { SnakType: null or "value" } ? snak.DataValue?.Value : null;
        return value is { ValueKind: JsonValueKind.String } element && !string.IsNullOrWhiteSpace(element.GetString())
            ? element.GetString()!.Trim()
            : null;
    }

    private static bool IsLanguageCode(string? value)
    {
        return value is not null && value.Length is >= 2 and <= 12 && value.All(char.IsLetter);
    }

    private static string KindSlug(EntityKind kind) => kind.ToString().ToLowerInvariant();
}
