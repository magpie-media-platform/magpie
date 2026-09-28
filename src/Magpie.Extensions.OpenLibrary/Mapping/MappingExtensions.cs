using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

using Magpie.Extensions.OpenLibrary.QueryEngine;
using Magpie.Models;
using Magpie.Models.Data;
using Magpie.Models.Filtering;
using Magpie.Models.MetadataSearch;
using Magpie.Sdk.Content;

namespace Magpie.Extensions.OpenLibrary.Mapping;

internal static class MappingExtensions
{
    private const int GenreCap = 20;
    private const int AlternateNameCap = 40;
    private const int IdentifierCap = 10;
    private const int IsbnCap = 20;

    private static readonly string[] MonthFormats = ["yyyy-MM", "MMMM yyyy", "MMM yyyy"];

    private static readonly string[] DayFormats =
    [
        "yyyy-MM-dd",
        "MMMM d, yyyy",
        "MMM d, yyyy",
        "d MMMM yyyy",
        "d MMM yyyy"
    ];

    private static readonly string[] FinerThanYearFormats = [.. MonthFormats, .. DayFormats];

    internal static string? FindExternalId(this IEnumerable<MetadataContribution> contributions, string source)
    {
        var fallback = (string?)null;
        foreach (var contribution in contributions)
        {
            foreach (var externalId in contribution.ExternalIds)
            {
                if (!string.Equals(externalId.Source, source, StringComparison.OrdinalIgnoreCase) ||
                    string.IsNullOrWhiteSpace(externalId.Value))
                {
                    continue;
                }

                if (contribution.ProviderType == Constants.ProviderType)
                {
                    return externalId.Value.Trim();
                }

                fallback ??= externalId.Value.Trim();
            }
        }

        return fallback;
    }

    internal static SourcePage ToSourcePage(this SearchResponseDto response)
    {
        var documents = response.Docs ?? [];
        var hits = new List<WorkHit>(documents.Count);
        foreach (var document in documents)
        {
            var hit = document.ToWorkHit();
            if (hit is not null)
            {
                hits.Add(hit);
            }
        }

        return new SourcePage
        {
            NumFound = Math.Max(0, response.NumFound),
            NumFoundExact = response.NumFoundExact,
            Offset = Math.Max(0, response.Start),
            Hits = hits,
            SourceRowCount = documents.Count
        };
    }

    private static WorkHit? ToWorkHit(this SearchDocumentDto document)
    {
        var workOlid = document.Key.NormalizeOlid('W');
        var title = document.Title?.Trim();
        var authors = AlignAuthors(document.AuthorKeys, document.AuthorNames);
        if (workOlid is null || string.IsNullOrEmpty(title))
        {
            return null;
        }

        return new WorkHit
        {
            WorkOlid = workOlid,
            Title = title,
            Subtitle = document.Subtitle?.Trim(),
            AlternativeTitles = Distinct(document.AlternativeTitles),
            Authors = authors,
            FirstPublishYear = document.FirstPublishYear is >= 1 and <= 9999
                ? document.FirstPublishYear
                : null,
            Languages = Distinct(document.Languages, normalize: value => value.NormalizeLanguage()),
            Subjects = Distinct(document.Subjects),
            SubjectKeys = Distinct(document.SubjectKeys),
            Isbns = ValidIsbns(document.Isbns),
            CoverId = document.CoverId is > 0 ? document.CoverId : null,
            CoverEditionOlid = document.CoverEditionKey.NormalizeOlid('M'),
            NumberOfPagesMedian = document.NumberOfPagesMedian is > 0 ? document.NumberOfPagesMedian : null,
            Publishers = Distinct(document.Publishers),
            Description = FlexibleText(document.Description),
            Persons = AlignSubjectFacets(document.Persons, document.PersonKeys),
            Places = AlignSubjectFacets(document.Places, document.PlaceKeys),
            Times = AlignSubjectFacets(document.Times, document.TimeKeys),
            PublishDates = Distinct(document.PublishDates),
            Oclcs = Distinct(document.Oclcs),
            Lccns = Distinct(document.Lccns),
            GoodreadsIds = Distinct(document.GoodreadsIds)
        };
    }

    internal static MetadataSearchCandidate ToMetadataSearchCandidate(
        this WorkHit hit,
        BookHierarchy hierarchy,
        string languagePreference,
        bool projectAnyLanguage = false)
    {
        return new MetadataSearchCandidate
        {
            ProviderType = Constants.ProviderType,
            ExternalIdSource = Constants.ProviderType,
            ExternalId = hit.WorkOlid,
            ContentType = hierarchy.ContentTypeSlug,
            Level = hierarchy.BookLevelSlug,
            Title = hit.Title,
            Year = hit.FirstPublishYear,
            ReleaseDate = RefinedPublishDate(hit),
            Overview = hit.Description ?? AuthorOverview(hit.Authors),
            Language = PreferredLanguage(hit.Languages, languagePreference, projectAnyLanguage),
            ThumbnailUrl = CoverUri(hit, "M"),
            SubjectReferences = SearchSubjectReferences(hit)
        };
    }

    internal static MetadataContribution ToAuthorContribution(this AuthorDto author, string authorOlid, DateTime utcNow)
    {
        var contribution = new MetadataContribution
        {
            ProviderType = Constants.ProviderType,
            Source = string.Format(CultureInfo.InvariantCulture, "openlibrary:author:{0}", authorOlid),
            Title = (author.Name ?? author.PersonalName)?.Trim(),
            Overview = FlexibleText(author.Bio).NormalizeHtmlToText(),
            BirthDate = ParseDate(author.BirthDate),
            DeathDate = ParseDate(author.DeathDate),
            DateLastRefreshed = utcNow
        };
        contribution.ExternalIds.Add(new ExternalId
        {
            Source = Constants.ProviderType,
            Value = authorOlid,
            Ordinal = 0
        });
        contribution.References.Add(new Reference
        {
            Kind = "canonical",
            Source = Constants.ProviderType,
            Url = new Uri(string.Format(
                CultureInfo.InvariantCulture,
                "https://openlibrary.org/authors/{0}",
                authorOlid)),
            DisplayName = "Open Library",
            Ordinal = 0
        });

        foreach (var (source, value) in new[]
        {
            (Constants.WikidataSource, author.RemoteIds?.Wikidata),
            (Constants.ViafSource, author.RemoteIds?.Viaf),
            (Constants.IsniSource, author.RemoteIds?.Isni),
            (Constants.GoodreadsSource, author.RemoteIds?.Goodreads),
            (Constants.AmazonSource, author.RemoteIds?.Amazon)
        })
        {
            var identifier = value?.Trim();
            if (identifier is not null)
            {
                contribution.ExternalIds.Add(new ExternalId
                {
                    Source = source,
                    Value = identifier,
                    Ordinal = contribution.ExternalIds.Count
                });
            }
        }

        var wikipedia = author.Wikipedia?.Trim();
        if (wikipedia is not null && Uri.TryCreate(wikipedia, UriKind.Absolute, out var wikipediaUrl))
        {
            contribution.References.Add(new Reference
            {
                Kind = "link",
                Source = Constants.ProviderType,
                Url = wikipediaUrl,
                DisplayName = "Wikipedia",
                Ordinal = contribution.References.Count
            });
        }

        foreach (var link in author.Links ?? [])
        {
            var url = link.Url?.Trim();
            if (url is null || !Uri.TryCreate(url, UriKind.Absolute, out var linkUrl))
            {
                continue;
            }

            contribution.References.Add(new Reference
            {
                Kind = "link",
                Source = Constants.ProviderType,
                Url = linkUrl,
                DisplayName = link.Title?.Trim() ?? linkUrl.Host,
                Ordinal = contribution.References.Count
            });
        }

        foreach (var photo in (author.Photos ?? []).Where(value => value > 0).Distinct())
        {
            contribution.Images.Add(new Image
            {
                Type = "photo",
                Source = Constants.ProviderType,
                Url = new Uri(string.Format(
                    CultureInfo.InvariantCulture,
                    "https://covers.openlibrary.org/a/id/{0}-L.jpg?default=false",
                    photo)),
                IsPrimary = contribution.Images.Count == 0,
                Ordinal = contribution.Images.Count
            });
        }

        var alternateNames = Distinct(author.AlternateNames, AlternateNameCap);
        for (var index = 0; index < alternateNames.Count; index++)
        {
            contribution.AlternateTexts.Add(new AlternateText
            {
                Kind = "name",
                Text = alternateNames[index],
                Ordinal = index
            });
        }

        return contribution;
    }

    internal static MetadataContribution ToWorkContribution(
        this WorkHit hit,
        string languagePreference,
        DateTime utcNow,
        EditionDto? edition = null)
    {
        var language = PreferredLanguage(hit.Languages, languagePreference);
        var contribution = new MetadataContribution
        {
            ProviderType = Constants.ProviderType,
            Source = string.Format(CultureInfo.InvariantCulture, "openlibrary:work:{0}", hit.WorkOlid),
            Title = hit.Title,
            OriginalTitle = hit.Title,
            Overview = hit.Description.NormalizeHtmlToText(),
            PublishDate = EditionRefinedPublishDate(hit, edition) ?? RefinedPublishDate(hit),
            Language = language,
            DateLastRefreshed = utcNow
        };
        contribution.ExternalIds.Add(new ExternalId
        {
            Source = Constants.ProviderType,
            Value = hit.WorkOlid,
            Ordinal = 0
        });
        foreach (var externalId in IdentifierExternalIds(hit, contribution.ExternalIds.Count))
        {
            contribution.ExternalIds.Add(externalId);
        }

        if (edition is not null)
        {
            AddEditionExternalIds(contribution, edition);
        }

        if (edition?.NumberOfPages is > 0)
        {
            contribution.CustomMetadata.Add(new CustomMetadataValue
            {
                Key = "pages",
                Value = edition.NumberOfPages.Value.ToString(CultureInfo.InvariantCulture)
            });
        }
        else if (hit.NumberOfPagesMedian is { } pages)
        {
            contribution.CustomMetadata.Add(new CustomMetadataValue
            {
                Key = "pages",
                Value = pages.ToString(CultureInfo.InvariantCulture)
            });
        }

        var format = edition?.PhysicalFormat?.Trim();
        if (format is not null)
        {
            contribution.CustomMetadata.Add(new CustomMetadataValue { Key = "format", Value = format });
        }

        var series = (edition?.Series ?? [])
            .Select(static value => value?.Trim())
            .FirstOrDefault(value => value is not null);
        if (series is not null)
        {
            contribution.CustomMetadata.Add(new CustomMetadataValue { Key = "series", Value = series });
        }

        for (var index = 0; index < Math.Min(GenreCap, hit.Subjects.Count); index++)
        {
            contribution.Genres.Add(new Genre { Name = hit.Subjects[index], Ordinal = index });
        }

        AddAlternateTexts(contribution, hit);
        contribution.References.Add(new Reference
        {
            Kind = "canonical",
            Source = Constants.ProviderType,
            Url = new Uri(string.Format(
                CultureInfo.InvariantCulture,
                "https://openlibrary.org/works/{0}",
                hit.WorkOlid)),
            DisplayName = "Open Library",
            Ordinal = 0
        });
        var cover = CoverUri(hit, "L");
        if (cover is not null)
        {
            contribution.Images.Add(new Image
            {
                Type = "cover",
                Source = Constants.ProviderType,
                Url = cover,
                IsPrimary = true,
                Ordinal = 0
            });
        }

        foreach (var coverId in (edition?.Covers ?? [])
            .Where(value => value > 0 && value != hit.CoverId)
            .Distinct())
        {
            contribution.Images.Add(new Image
            {
                Type = "cover",
                Source = Constants.ProviderType,
                Url = new Uri(string.Format(
                    CultureInfo.InvariantCulture,
                    "https://covers.openlibrary.org/b/id/{0}-L.jpg?default=false",
                    coverId)),
                IsPrimary = contribution.Images.Count == 0,
                Ordinal = contribution.Images.Count
            });
        }

        return contribution;
    }

    private static DateTime? EditionRefinedPublishDate(this WorkHit hit, EditionDto? edition)
    {
        if (edition is null || hit.FirstPublishYear is not { } year)
        {
            return null;
        }

        var parsed = ParseDateFinerThanYear(edition.PublishDate);
        return parsed is { } date && date.Year == year ? date : null;
    }

    private static void AddEditionExternalIds(this MetadataContribution contribution, EditionDto edition)
    {
        var identifiers = edition.Identifiers ?? new Dictionary<string, IReadOnlyList<string>>();
        foreach (var (key, source) in new[]
        {
            ("goodreads", Constants.GoodreadsSource),
            ("librarything", Constants.LibraryThingSource)
        })
        {
            if (!identifiers.TryGetValue(key, out var values))
            {
                continue;
            }

            foreach (var candidate in values
                .Select(static value => value?.Trim())
                .Where(value => value is not null)
                .Cast<string>())
            {
                if (contribution.ExternalIds.Any(existing =>
                    string.Equals(existing.Source, source, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(existing.Value, candidate, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                contribution.ExternalIds.Add(new ExternalId
                {
                    Source = source,
                    Value = candidate,
                    Ordinal = contribution.ExternalIds.Count
                });
            }
        }
    }

    private static IReadOnlyList<AuthorPair> AlignAuthors(
        this IReadOnlyList<string>? keys,
        IReadOnlyList<string>? names)
    {
        if (keys is null || names is null)
        {
            return [];
        }

        var authors = new List<AuthorPair>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var count = Math.Min(keys.Count, names.Count);
        for (var index = 0; index < count; index++)
        {
            var olid = keys[index].NormalizeOlid('A');
            var name = names[index]?.Trim();
            if (olid is null || name is null || !seen.Add(string.Concat(olid, "\0", name)))
            {
                continue;
            }

            authors.Add(new AuthorPair(olid, name));
        }

        return authors;
    }

    private static IReadOnlyList<SubjectFacetPair> AlignSubjectFacets(
        IReadOnlyList<string>? names,
        IReadOnlyList<string>? keys)
    {
        // Open Library sorts and deduplicates key arrays apart from name arrays, so positions do not pair them.
        var knownKeys = new HashSet<string>(
            (keys ?? []).Where(key => !string.IsNullOrWhiteSpace(key)).Select(key => key.Trim()),
            StringComparer.Ordinal);
        var facets = new List<SubjectFacetPair>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in (names ?? []).Where(name => name is not null).Select(name => name.Trim()))
        {
            if (seen.Add(name))
            {
                var key = name.ToOpenLibrarySubjectKey();
                facets.Add(new SubjectFacetPair(name, key.Length > 0 && knownKeys.Contains(key) ? key : null));
            }
        }

        return facets;
    }

    private static IReadOnlyList<ExternalId> IdentifierExternalIds(this WorkHit hit, int firstOrdinal)
    {
        var externalIds = new List<ExternalId>();
        var ordinal = firstOrdinal;
        foreach (var isbn in hit.Isbns
            .OrderByDescending(value => value.Length)
            .Take(IsbnCap))
        {
            externalIds.Add(new ExternalId
            {
                Source = Constants.IsbnSource,
                Value = isbn,
                Ordinal = ordinal++
            });
        }

        foreach (var (source, values) in new[]
        {
            (Constants.OclcSource, hit.Oclcs),
            (Constants.LccnSource, hit.Lccns),
            (Constants.GoodreadsSource, hit.GoodreadsIds)
        })
        {
            foreach (var value in values.Take(IdentifierCap))
            {
                externalIds.Add(new ExternalId
                {
                    Source = source,
                    Value = value,
                    Ordinal = ordinal++
                });
            }
        }

        return externalIds;
    }

    private static IReadOnlyList<ProviderMetadataSearchSubjectReference> AuthorReferences(
        this IReadOnlyList<AuthorPair> authors)
    {
        var references = new List<ProviderMetadataSearchSubjectReference>(authors.Count);
        foreach (var author in authors)
        {
            references.Add(new ProviderMetadataSearchSubjectReference
            {
                SubjectKind = Constants.PersonSubjectKind,
                DisplayName = author.Name,
                RelationshipKind = Constants.AuthorRelationshipKind,
                ExternalIds = new Dictionary<string, string>
                {
                    [Constants.ProviderType] = author.Olid
                }
            });
        }

        return references;
    }

    private static IReadOnlyList<ProviderMetadataSearchSubjectReference> SearchSubjectReferences(this WorkHit hit)
    {
        var references = AuthorReferences(hit.Authors).ToList();
        foreach (var publisher in hit.Publishers
            .Where(publisher => !string.IsNullOrEmpty(publisher.NormalizeSubjectKey()))
            .Take(Constants.PublisherCap))
        {
            var identity = publisher.NormalizeSubjectKey();
            references.Add(new ProviderMetadataSearchSubjectReference
            {
                SubjectKind = Constants.PublisherSubjectKind,
                DisplayName = publisher,
                RelationshipKind = Constants.PublisherRelationshipKind,
                ExternalIds = new Dictionary<string, string>
                {
                    [Constants.ProviderType] = identity
                }
            });
        }

        AddFacetReferences(references, hit.Persons, Constants.PersonSubjectKind, Constants.ProviderType);
        AddFacetReferences(references, hit.Places, Constants.PlaceSubjectKind, Constants.ProviderType);
        AddFacetReferences(references, hit.Times, Constants.TimeSubjectKind, Constants.ProviderType);
        return references;
    }

    internal static string FacetIdentity(this SubjectFacetPair facet)
    {
        return !string.IsNullOrWhiteSpace(facet.Key) ? facet.Key.Trim() : facet.Name.NormalizeSubjectKey();
    }

    private static void AddFacetReferences(
        this List<ProviderMetadataSearchSubjectReference> references,
        IReadOnlyList<SubjectFacetPair> facets,
        string kind,
        string externalIdSource)
    {
        var identified = facets
            .Select(facet => (Facet: facet, Identity: facet.FacetIdentity()))
            .Where(pair => !string.IsNullOrEmpty(pair.Identity))
            .Take(Constants.SubjectFacetCap);
        foreach (var (facet, identity) in identified)
        {
            references.Add(new ProviderMetadataSearchSubjectReference
            {
                SubjectKind = kind,
                DisplayName = facet.Name,
                RelationshipKind = kind,
                ExternalIds = new Dictionary<string, string>
                {
                    [externalIdSource] = identity
                }
            });
        }
    }

    private static void AddAlternateTexts(this MetadataContribution contribution, WorkHit hit)
    {
        var ordinal = 0;
        if (hit.Subtitle is not null)
        {
            contribution.AlternateTexts.Add(new AlternateText
            {
                Kind = "subtitle",
                Text = hit.Subtitle,
                Ordinal = ordinal++
            });
        }

        foreach (var title in hit.AlternativeTitles.Take(AlternateNameCap))
        {
            if (string.Equals(title, hit.Title, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(title, hit.Subtitle, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            contribution.AlternateTexts.Add(new AlternateText
            {
                Kind = "alternative_title",
                Text = title,
                Ordinal = ordinal++
            });
        }
    }

    private static IReadOnlyList<string> ValidIsbns(this IReadOnlyList<string>? candidates)
    {
        if (candidates is null)
        {
            return [];
        }

        var values = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in candidates)
        {
            var isbn = candidate.NormalizeIsbn();
            if (isbn is not null && seen.Add(isbn))
            {
                values.Add(isbn);
            }
        }

        return values;
    }

    internal static string? RepresentativeIsbn(this IReadOnlyList<string> isbns)
    {
        return isbns.FirstOrDefault(value => value.Length == 13)
            ?? isbns.FirstOrDefault(value => value.Length == 10);
    }

    private static Uri? CoverUri(this WorkHit hit, string size)
    {
        if (hit.CoverId is { } coverId)
        {
            return new Uri(string.Format(
                CultureInfo.InvariantCulture,
                "https://covers.openlibrary.org/b/id/{0}-{1}.jpg?default=false",
                coverId,
                size));
        }

        return hit.CoverEditionOlid is null
            ? null
            : new Uri(string.Format(
                CultureInfo.InvariantCulture,
                "https://covers.openlibrary.org/b/olid/{0}-{1}.jpg?default=false",
                hit.CoverEditionOlid,
                size));
    }

    private static string? PreferredLanguage(
        this IReadOnlyList<string> languages,
        string preference,
        bool fallbackToFirst = false)
    {
        if (languages.Count == 0)
        {
            return null;
        }

        var preferred = preference.NormalizeLanguage();
        if (preferred is not null)
        {
            foreach (var language in languages)
            {
                if (string.Equals(language, preferred, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(
                        language.NormalizeLanguage(),
                        preferred,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return language;
                }
            }
        }

        return languages.Count == 1 || fallbackToFirst ? languages[0] : null;
    }

    private static IReadOnlyList<string> Distinct(
        this IReadOnlyList<string>? values,
        int? cap = null,
        Func<string?, string?>? normalize = null)
    {
        if (values is null)
        {
            return [];
        }

        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in values)
        {
            var value = normalize is null ? candidate?.Trim() : normalize(candidate);
            if (value is null || !seen.Add(value))
            {
                continue;
            }

            result.Add(value);
            if (cap is { } maximum && result.Count == maximum)
            {
                break;
            }
        }

        return result;
    }

    private static string? FlexibleText(this JsonElement? element)
    {
        if (element is null)
        {
            return null;
        }

        var value = element.Value;
        if (value.ValueKind == JsonValueKind.String)
        {
            return value.GetString()?.Trim();
        }

        if (value.ValueKind == JsonValueKind.Object &&
            value.TryGetProperty("value", out var property) &&
            property.ValueKind == JsonValueKind.String)
        {
            return property.GetString()?.Trim();
        }

        return null;
    }

    private static string? AuthorOverview(this IReadOnlyList<AuthorPair> authors)
    {
        if (authors.Count == 0)
        {
            return null;
        }

        var names = string.Join(", ", authors.Select(author => author.Name));
        return string.Format(CultureInfo.InvariantCulture, "By {0}.", names);
    }

    private static DateTime? RefinedPublishDate(this WorkHit hit)
    {
        return RefinedPublishTemporal(hit)?.Value;
    }

    internal static TemporalValue? RefinedPublishTemporal(this WorkHit hit)
    {
        var year = hit.FirstPublishYear;
        TemporalValue? refined = null;
        foreach (var candidate in hit.PublishDates)
        {
            var parsed = ParseTemporal(candidate);
            if (parsed is null ||
                parsed.Precision == TemporalPrecision.Year ||
                year is not null && parsed.Value.Year != year)
            {
                continue;
            }

            if (refined is null ||
                parsed.Value < refined.Value ||
                parsed.Value == refined.Value && parsed.Precision > refined.Precision)
            {
                refined = parsed;
            }
        }

        if (refined is not null)
        {
            return refined;
        }

        if (year is >= 1 and <= 9999)
        {
            return new TemporalValue
            {
                Value = FromYear(year).GetValueOrDefault(),
                Precision = TemporalPrecision.Year
            };
        }

        return hit.PublishDates.Select(ParseTemporal)
            .Where(value => value is not null)
            .Cast<TemporalValue>()
            .OrderBy(value => value.Value)
            .ThenByDescending(value => value.Precision)
            .FirstOrDefault();
    }

    private static DateTime? FromYear(this int? year)
    {
        return year is >= 1 and <= 9999
            ? new DateTime(year.Value, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            : null;
    }

    private static DateTime? ParseDate(this string? value) => ParseTemporal(value)?.Value;

    internal static TemporalValue? ParseTemporal(this string? value)
    {
        var text = value?.Trim();
        if (text is null)
        {
            return null;
        }

        foreach (var prefix in new[] { "circa", "ca.", "c." })
        {
            if (text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
                text.Length > prefix.Length)
            {
                text = text[prefix.Length..].Trim();
                break;
            }
        }

        var decade = Regex.Match(text, "^([0-9]{3})(?:\\?|0s)$", RegexOptions.CultureInvariant);
        if (decade.Success)
        {
            var start = int.Parse(decade.Groups[1].Value, CultureInfo.InvariantCulture) * 10;
            return start >= 1
                ? new TemporalValue
                {
                    Value = new DateTime(start, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                    Precision = TemporalPrecision.Year
                }
                : null;
        }

        if (ParseExact(text, DayFormats) is { } day)
        {
            return new TemporalValue { Value = day, Precision = TemporalPrecision.Day };
        }

        if (ParseExact(text, MonthFormats) is { } month)
        {
            return new TemporalValue { Value = month, Precision = TemporalPrecision.Month };
        }

        if (ParseExact(text, ["yyyy"]) is { } year)
        {
            return new TemporalValue { Value = year, Precision = TemporalPrecision.Year };
        }

        return null;
    }

    private static DateTime? ParseDateFinerThanYear(this string? value)
    {
        var text = value?.Trim();
        return text is null ? null : ParseExact(text, FinerThanYearFormats);
    }

    private static DateTime? ParseExact(this string text, string[] formats)
    {
        if (!DateTime.TryParseExact(
            text,
            formats,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AllowWhiteSpaces,
            out var parsed) ||
            parsed.Year < 1)
        {
            return null;
        }

        return DateTime.SpecifyKind(parsed, DateTimeKind.Utc);
    }
}
