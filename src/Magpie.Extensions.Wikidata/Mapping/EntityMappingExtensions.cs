using System;
using System.Collections.Generic;
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
    private const string GregorianCalendarQid = "Q1985727";

    public static bool MatchesKind(this EntityDto entity, EntityKind kind)
    {
        if (kind == EntityKind.Studio)
        {
            return !string.IsNullOrWhiteSpace(entity.Id);
        }

        IReadOnlyList<string> accepted = kind switch
        {
            EntityKind.Movie =>
            [
                Constants.MovieQid,
                Constants.ShortFilmQid,
                Constants.TelevisionFilmQid
            ],
            EntityKind.Show =>
            [
                Constants.TelevisionSeriesQid,
                Constants.AnimatedSeriesQid
            ],
            EntityKind.Season => [Constants.SeasonQid],
            EntityKind.Episode => [Constants.EpisodeQid],
            EntityKind.Person => [Constants.HumanQid],
            _ => []
        };
        var hasAcceptedType = EntityIds(entity, Constants.InstanceOfPropertyId)
            .Any(value => accepted.Contains(value, StringComparer.Ordinal));
        return hasAcceptedType &&
            kind switch
            {
                EntityKind.Season => EntityIds(entity, Constants.PartOfSeriesPropertyId).Any(),
                EntityKind.Episode => EntityIds(entity, Constants.PartOfSeriesPropertyId).Any() &&
                    EntityIds(entity, Constants.SeasonPropertyId).Any(),
                _ => true
            };
    }

    public static IReadOnlyList<string> RequiredSupportQids(this EntityDto entity)
    {
        var result = new List<string>();
        result.AddRange(EntityIds(entity, Constants.OriginalLanguagePropertyId));
        result.AddRange(EntityIds(entity, Constants.GenrePropertyId).Take(30));
        foreach (var statement in entity.TruthyStatements(Constants.AwardReceivedPropertyId).Take(30))
        {
            var awardQid = EntityId(statement.Mainsnak);
            if (awardQid is not null)
            {
                result.Add(awardQid);
            }

            result.AddRange(QualifierEntityIds(statement, Constants.ConferredByPropertyId));
        }

        foreach (var (propertyId, _) in Constants.RatingSystems)
        {
            result.AddRange(EntityIds(entity, propertyId));
        }

        return [.. result.Distinct(StringComparer.OrdinalIgnoreCase)];
    }

    public static IReadOnlyList<string> RequiredAwardBodyQids(
        this EntityDto entity,
        IReadOnlyDictionary<string, EntityDto> supportEntities)
    {
        var result = new List<string>();
        foreach (var awardQid in EntityIds(entity, Constants.AwardReceivedPropertyId).Take(30))
        {
            if (supportEntities.TryGetValue(awardQid, out var award))
            {
                result.AddRange(EntityIds(award, Constants.ConferredByPropertyId).Take(1));
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
        return EntityIds(entity, propertyId).FirstOrDefault();
    }

    public static MetadataContribution? ToMetadataContribution(
        this EntityDto entity,
        EntityKind kind,
        string language,
        IReadOnlyDictionary<string, EntityDto> supportEntities,
        DateTime utcNow,
        string? parentQid = null)
    {
        if (!entity.MatchesKind(kind) || !TryNormalizeQid(entity.Id, out var qid))
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
        AddExternalId(contribution, Constants.ProviderType, qid!, 0);

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
        contribution.PublishDate
            = ContributionDate(EarliestTime(entity, Constants.PublicationDatePropertyId));
        contribution.Duration = ResolveDuration(entity);
        AddGenres(contribution, entity, supportEntities, language);
        AddAwards(contribution, entity, supportEntities, language);
        AddContentRatings(contribution, entity, supportEntities, language);
        AddExternalId(
            contribution,
            Constants.ImdbSource,
            FirstString(entity, Constants.ImdbPropertyId));
        AddExternalId(
            contribution,
            Constants.TmdbSource,
            FirstString(entity, Constants.TmdbMoviePropertyId));
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
        contribution.PublishDate = ContributionDate(start);
        contribution.Established = ContributionDate(start);
        contribution.Dissolved = ContributionDate(end);
        contribution.ProductionStatus = end is null ? null : Constants.EndedProductionStatus;
        contribution.Duration = ResolveDuration(entity);
        AddGenres(contribution, entity, supportEntities, language);
        AddAwards(contribution, entity, supportEntities, language);
        AddContentRatings(contribution, entity, supportEntities, language);
        AddExternalId(
            contribution,
            Constants.ImdbSource,
            FirstString(entity, Constants.ImdbPropertyId));
        AddExternalId(
            contribution,
            Constants.TmdbSource,
            FirstString(entity, Constants.TmdbShowPropertyId));
        AddExternalId(
            contribution,
            Constants.TvdbSource,
            FirstString(entity, Constants.TvdbShowPropertyId));
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
        contribution.PublishDate = ContributionDate(start);
        contribution.Established = ContributionDate(start);
        contribution.Dissolved = ContributionDate(end);
        AddExternalId(
            contribution,
            Constants.TmdbSource,
            FirstString(entity, Constants.TmdbSeasonPropertyId));
        AddExternalId(
            contribution,
            Constants.TvdbSource,
            FirstString(entity, Constants.TvdbSeasonPropertyId));
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
        contribution.PublishDate
            = ContributionDate(EarliestTime(entity, Constants.PublicationDatePropertyId));
        contribution.Duration = ResolveDuration(entity);
        var productionCode = FirstString(entity, Constants.ProductionCodePropertyId);
        if (productionCode is not null)
        {
            contribution.CustomMetadata.Add(new CustomMetadataValue { Key = "productionCode", Value = productionCode });
        }

        AddAwards(contribution, entity, supportEntities, language);
        AddExternalId(
            contribution,
            Constants.ImdbSource,
            FirstString(entity, Constants.ImdbPropertyId));
        AddExternalId(
            contribution,
            Constants.TmdbSource,
            FirstString(entity, Constants.TmdbEpisodePropertyId));
        AddExternalId(
            contribution,
            Constants.TvdbSource,
            FirstString(entity, Constants.TvdbEpisodePropertyId));
        AddImage(contribution, FirstString(entity, Constants.ImagePropertyId), "still", primary: true);
    }

    private static void MapPerson(
        MetadataContribution contribution,
        EntityDto entity,
        string language,
        IReadOnlyDictionary<string, EntityDto> supportEntities)
    {
        contribution.BirthDate = ContributionDate(EarliestTime(entity, Constants.BirthDatePropertyId));
        contribution.DeathDate = ContributionDate(EarliestTime(entity, Constants.DeathDatePropertyId));
        AddAwards(contribution, entity, supportEntities, language);
        AddExternalId(
            contribution,
            Constants.ImdbSource,
            FirstString(entity, Constants.ImdbPropertyId));
        AddExternalId(
            contribution,
            Constants.TmdbSource,
            FirstString(entity, Constants.TmdbPersonPropertyId));
        AddAliases(contribution, entity, language);
        AddImage(contribution, FirstString(entity, Constants.ImagePropertyId), "profile", primary: true);
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
        contribution.Established = ContributionDate(EarliestTime(entity, Constants.InceptionPropertyId));
        contribution.Dissolved = ContributionDate(EarliestTime(entity, Constants.DissolutionPropertyId));
        AddAwards(contribution, entity, supportEntities, language);
        AddExternalId(
            contribution,
            Constants.TmdbSource,
            FirstString(entity, Constants.TmdbCompanyPropertyId));
        AddImage(contribution, FirstString(entity, Constants.LogoPropertyId), "logo", primary: true);
        AddImage(contribution, FirstString(entity, Constants.ImagePropertyId), "backdrop", primary: false);
    }

    private static MetadataContribution ToHierarchyContribution(HierarchyRow row, DateTime utcNow)
    {
        var contribution = new MetadataContribution
        {
            ProviderType = Constants.ProviderType,
            Source = "wikidata:" + KindSlug(row.EntityKind) + ":" + row.Qid,
            Title = row.Title,
            Overview = row.Overview,
            PublishDate = ContributionDate(row.PublishDate),
            SequenceNumber = row.Ordinal,
            DateLastRefreshed = utcNow
        };
        var ordinal = 0;
        foreach (var external in row.ExternalIds.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            AddExternalId(contribution, external.Key, external.Value, ordinal++);
        }

        AddImage(
            contribution,
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
        foreach (var qid in EntityIds(entity, Constants.GenrePropertyId).Take(30))
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
            var awardQid = EntityId(statement.Mainsnak);
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
        var bodyQids = QualifierEntityIds(statement, Constants.ConferredByPropertyId)
            .Concat(EntityIds(award, Constants.ConferredByPropertyId));
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
                select (int?)value!.Value.Year).FirstOrDefault();
    }

    private static IReadOnlyList<string> QualifierEntityIds(StatementDto statement, string propertyId)
    {
        if (statement.Qualifiers is null || !statement.Qualifiers.TryGetValue(propertyId, out var qualifiers))
        {
            return [];
        }

        var qids =
            from qualifier in qualifiers
            let value = EntityId(qualifier)
            where value is not null
            select value!;
        return [.. qids.Distinct(StringComparer.OrdinalIgnoreCase)];
    }

    public static IReadOnlyList<ContentRatingCandidate> ToContentRatingCandidates(this EntityDto entity)
    {
        var result = new List<ContentRatingCandidate>();
        foreach (var (propertyId, body) in Constants.RatingSystems)
        {
            result.AddRange(EntityIds(entity, propertyId)
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
            .Select(statement => StringValue(statement.Mainsnak)))
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
            .Select(statement => StringValue(statement.Mainsnak)))
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
        foreach (var qid in EntityIds(entity, Constants.OriginalLanguagePropertyId))
        {
            if (supportEntities.TryGetValue(qid, out var language))
            {
                var code = FirstString(language, Constants.LanguageCodePropertyId);
                if (IsLanguageCode(code))
                {
                    return code!.Trim().ToLowerInvariant();
                }
            }
        }

        return null;
    }

    private static void AddContentArtwork(MetadataContribution contribution, EntityDto entity)
    {
        foreach (var image in FirstString(entity, Constants.PosterPropertyId)
                     .ToContentArtwork(
                         FirstString(entity, Constants.ImagePropertyId),
                         FirstString(entity, Constants.LogoPropertyId)))
        {
            contribution.Images.Add(image);
        }
    }

    private static void AddImage(
        MetadataContribution contribution,
        string? filename,
        string type,
        bool primary) => contribution.Images.AddImage(filename, type, primary);

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
            Value = value!.Trim(),
            Ordinal = ordinal ?? contribution.ExternalIds.Count
        });
    }

    private static int? ResolveOrdinal(EntityDto entity, string propertyId, string? parentQid)
    {
        foreach (var statement in entity.TruthyStatements(propertyId))
        {
            var target = EntityId(statement.Mainsnak);
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
                          let value = StringValue(qualifier)
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

            var unitQid = ExtractEntityId(unitElement.GetString());
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
            orderby value!.PeriodStart()
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
            ExtractEntityId(calendar.GetString()) != GregorianCalendarQid)
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
                Value = new DateTime(year, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                Precision = TemporalPrecision.Year
            };
        }

        if (precision == 10 && month is >= 1 and <= 12)
        {
            return new TemporalValue
            {
                Value = new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Utc),
                Precision = TemporalPrecision.Month
            };
        }

        if (precision == 11 && IsValidDate(year, month, day))
        {
            return new TemporalValue
            {
                Value = new DateTime(year, month, day, 0, 0, 0, DateTimeKind.Utc),
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

    private static DateTime? ContributionDate(TemporalValue? value) => value?.PeriodStart();

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

    private static IEnumerable<string> EntityIds(EntityDto entity, string propertyId)
    {
        return
            from statement in entity.TruthyStatements(propertyId)
            let value = EntityId(statement.Mainsnak)
            where value is not null
            select value!;
    }

    private static string? EntityId(SnakDto? snak)
    {
        var value = snak is { SnakType: null or "value" } ? snak.DataValue?.Value : null;
        return value is { ValueKind: JsonValueKind.Object } element &&
            element.TryGetProperty("id", out var id)
            ? NormalizeQid(id.GetString())
            : null;
    }

    private static string? FirstString(EntityDto entity, string propertyId)
    {
        return entity.TruthyStatements(propertyId)
            .Select(statement => StringValue(statement.Mainsnak))
            .FirstOrDefault(value => value is not null);
    }

    private static string? StringValue(SnakDto? snak)
    {
        var value = snak is { SnakType: null or "value" } ? snak.DataValue?.Value : null;
        return value is { ValueKind: JsonValueKind.String } element && !string.IsNullOrWhiteSpace(element.GetString())
            ? element.GetString()!.Trim()
            : null;
    }

    private static string? NormalizeQid(string? value) => TryNormalizeQid(value, out var qid) ? qid : null;

    private static bool TryNormalizeQid(string? value, out string? qid)
    {
        qid = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var normalized = ExtractEntityId(value) ?? value!.Trim();
        if (normalized.Length < 2 ||
            char.ToUpperInvariant(normalized[0]) != 'Q' ||
            normalized[1] == '0' ||
            !normalized.Substring(1).All(char.IsDigit))
        {
            return false;
        }

        qid = "Q" + normalized.Substring(1);
        return true;
    }

    private static string? ExtractEntityId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var slash = value!.LastIndexOf('/');
        return slash >= 0 && slash < value.Length - 1 ? value.Substring(slash + 1) : value;
    }

    private static bool IsLanguageCode(string? value)
    {
        return value is not null && value.Length is >= 2 and <= 12 && value.All(char.IsLetter);
    }

    private static string KindSlug(EntityKind kind) => kind.ToString().ToLowerInvariant();
}
