using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

using Magpie.Extensions.Wikidata.QueryEngine;
using Magpie.Models.Data;
using Magpie.Models.Filtering;
using Magpie.Models.MetadataSearch;

namespace Magpie.Extensions.Wikidata.Mapping;

internal static class CandidateMappingExtensions
{
    public static SearchHit? ToSearchHit(
        this IReadOnlyDictionary<string, SparqlBindingDto> binding)
    {
        var qid = ExtractQid(Read(binding, "item"));
        var typeQid = ExtractQid(Read(binding, "type"));
        var label = Read(binding, "itemLabel");
        if (!IsQid(qid) || !IsQid(typeQid) || string.IsNullOrWhiteSpace(label))
        {
            return null;
        }

        var endDate = Read(binding, "endDate").ToTemporalValue();
        return new SearchHit
        {
            Qid = qid!,
            TypeQid = typeQid!,
            Label = label!.Trim(),
            Description = Clean(Read(binding, "itemDescription")),
            PublishDate = Read(binding, "publishDate").ToTemporalValue(),
            EndDate = endDate,
            HasEndDate = string.Equals(Read(binding, "hasEndDate"), "1", StringComparison.Ordinal) ||
                endDate is not null,
            Duration = ParseDurationSeconds(Read(binding, "durationSeconds")),
            Language = Clean(Read(binding, "languageCode"))?.ToLowerInvariant(),
            PosterFilename = Read(binding, "posterFilename").ToCommonsFilename(),
            ImageFilename = Read(binding, "imageFilename").ToCommonsFilename(),
            Genres = ParseGenres(Read(binding, "genres")),
            CandidateProjectionApplied = HasCandidateProjection(binding),
            SearchOrdinal = ParseInt(Read(binding, "searchOrdinal"))
        };
    }

    public static SearchHit ApplyProjection(
        this SearchHit hit,
        IReadOnlyDictionary<string, SparqlBindingDto> binding)
    {
        var endDate = Read(binding, "endDate").ToTemporalValue();
        return hit with
        {
            PublishDate = Read(binding, "publishDate").ToTemporalValue(),
            EndDate = endDate,
            HasEndDate = string.Equals(Read(binding, "hasEndDate"), "1", StringComparison.Ordinal) ||
                endDate is not null,
            Duration = ParseDurationSeconds(Read(binding, "durationSeconds")),
            Language = Clean(Read(binding, "languageCode"))?.ToLowerInvariant(),
            PosterFilename = Read(binding, "posterFilename").ToCommonsFilename(),
            ImageFilename = Read(binding, "imageFilename").ToCommonsFilename(),
            Genres = ParseGenres(Read(binding, "genres")),
            CandidateProjectionApplied = true
        };
    }

    private static bool HasCandidateProjection(IReadOnlyDictionary<string, SparqlBindingDto> binding)
    {
        return binding.ContainsKey("publishDate") ||
            binding.ContainsKey("endDate") ||
            binding.ContainsKey("hasEndDate") ||
            binding.ContainsKey("durationSeconds") ||
            binding.ContainsKey("languageCode") ||
            binding.ContainsKey("posterFilename") ||
            binding.ContainsKey("imageFilename") ||
            binding.ContainsKey("genres");
    }

    public static MetadataSearchCandidate ToMetadataSearchCandidate(
        this SearchHit hit,
        EntityKind kind,
        string level)
    {
        return new()
        {
            ProviderType = Constants.ProviderType,
            ExternalIdSource = Constants.ProviderType,
            ExternalId = hit.Qid,
            ContentType = kind == EntityKind.Movie
                ? Constants.MovieContentType
                : Constants.TvContentType,
            Level = level,
            Title = hit.Label,
            Year = hit.PublishDate?.Value.Year,
            ReleaseDate = PeriodStartDate(hit.PublishDate),
            Overview = hit.Description,
            Language = hit.Language,
            ThumbnailUrl = CommonsUri(hit.PosterFilename ?? hit.ImageFilename)
        };
    }

    public static MetadataSearchCandidate? ToMetadataSearchCandidate(
        this MetadataContribution contribution,
        MetadataSearchCandidate seed,
        EntityKind kind)
    {
        var qid = contribution.ExternalIds.FirstOrDefault(external => string.Equals(
            external.Source,
            Constants.ProviderType,
            StringComparison.OrdinalIgnoreCase))?.Value;
        if (string.IsNullOrWhiteSpace(qid))
        {
            return null;
        }

        var thumbnail = (
            from image in contribution.Images
            orderby image.IsPrimary descending, image.Ordinal
            select image.Url).FirstOrDefault();
        return new MetadataSearchCandidate
        {
            ProviderType = Constants.ProviderType,
            ExternalIdSource = Constants.ProviderType,
            ExternalId = qid.Trim(),
            ContentType = kind == EntityKind.Movie
                ? Constants.MovieContentType
                : Constants.TvContentType,
            Level = seed.Level,
            Title = string.IsNullOrWhiteSpace(contribution.Title) ? seed.Title : contribution.Title,
            Year = contribution.PublishDate?.Year ?? seed.Year,
            ReleaseDate = contribution.PublishDate ?? seed.ReleaseDate,
            Overview = string.IsNullOrWhiteSpace(contribution.Overview) ? seed.Overview : contribution.Overview,
            Language = string.IsNullOrWhiteSpace(contribution.Language) ? seed.Language : contribution.Language,
            ThumbnailUrl = thumbnail ?? seed.ThumbnailUrl,
            SubjectReferences = seed.SubjectReferences
        };
    }

    internal static TemporalValue? ToTemporalValue(this string? value)
    {
        var parts = value?.Split('|');
        if (parts is not { Length: 4 } ||
            !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var precision) ||
            ExtractQid(parts[2]) != "Q1985727" ||
            !int.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var timezone) ||
            timezone != 0)
        {
            return null;
        }

        if (!DateTime.TryParse(
            parts[0],
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var parsed))
        {
            return null;
        }

        var normalized = DateTime.SpecifyKind(parsed, DateTimeKind.Utc);
        if (precision == 9 && normalized.TimeOfDay == TimeSpan.Zero)
        {
            return new TemporalValue
            {
                Value = new DateTime(normalized.Year, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                Precision = TemporalPrecision.Year
            };
        }

        if (precision == 10 && normalized.TimeOfDay == TimeSpan.Zero)
        {
            return new TemporalValue
            {
                Value = new DateTime(normalized.Year, normalized.Month, 1, 0, 0, 0, DateTimeKind.Utc),
                Precision = TemporalPrecision.Month
            };
        }

        if (precision == 11 && normalized.TimeOfDay == TimeSpan.Zero)
        {
            return new TemporalValue { Value = normalized.Date, Precision = TemporalPrecision.Day };
        }

        return precision > 11
            ? new TemporalValue { Value = normalized, Precision = TemporalPrecision.Instant }
            : null;
    }

    private static DateTime? PeriodStartDate(TemporalValue? value) => value?.PeriodStart();

    private static TimeSpan? ParseDurationSeconds(string? value)
    {
        if (!decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) ||
            seconds <= 0 ||
            seconds > TimeSpan.MaxValue.Ticks / (decimal)TimeSpan.TicksPerSecond)
        {
            return null;
        }

        var ticks = decimal.Round(seconds * TimeSpan.TicksPerSecond, 0);
        return ticks < 1 ? null : TimeSpan.FromTicks(decimal.ToInt64(ticks));
    }

    private static IReadOnlyList<string> ParseGenres(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        return [.. value!.Split(SparqlQueryBuilder.GenreSeparator)
            .Select(genre => genre.Trim())
            .Where(genre => genre.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)];
    }

    private static int? ParseInt(string? value)
    {
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;
    }

    private static string? Read(IReadOnlyDictionary<string, SparqlBindingDto> binding, string key)
    {
        return binding.TryGetValue(key, out var value) ? value?.Value : null;
    }

    private static string? ExtractQid(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value!.Trim();
        var separator = trimmed.LastIndexOf('/');
        return separator >= 0 && separator < trimmed.Length - 1 ? trimmed.Substring(separator + 1) : trimmed;
    }

    private static bool IsQid(string? value)
    {
        if (value is null || value.Length < 2 || value[0] != 'Q' || value[1] == '0')
        {
            return false;
        }

        for (var index = 1; index < value.Length; index++)
        {
            if (!char.IsDigit(value[index]))
            {
                return false;
            }
        }

        return true;
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value!.Trim();

    internal static string? ToCommonsFilename(this string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var filename = value!.Trim();
        if (filename.Any(char.IsControl))
        {
            return null;
        }

        if (Uri.TryCreate(filename, UriKind.Absolute, out var source) &&
            source.Scheme is "http" or "https")
        {
            const string pathPrefix = "/wiki/Special:FilePath/";
            if (!string.Equals(source.Host, "commons.wikimedia.org", StringComparison.OrdinalIgnoreCase) ||
                !source.AbsolutePath.StartsWith(pathPrefix, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            filename = Uri.UnescapeDataString(source.AbsolutePath.Substring(pathPrefix.Length)).Trim();
            if (filename.Length == 0 || filename.Any(char.IsControl))
            {
                return null;
            }
        }

        return filename;
    }

    internal static Uri? CommonsUri(this string? value)
    {
        var filename = value.ToCommonsFilename();
        if (filename is null)
        {
            return null;
        }

        return Uri.TryCreate(
            "https://commons.wikimedia.org/wiki/Special:Redirect/file/" + Uri.EscapeDataString(filename),
            UriKind.Absolute,
            out var uri)
            ? uri
            : null;
    }
}
