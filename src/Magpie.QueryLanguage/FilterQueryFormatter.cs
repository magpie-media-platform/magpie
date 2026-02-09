using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Filtering;

namespace Magpie.QueryLanguage;

/// <summary>
/// Formats filter criteria as human-readable query strings.
/// </summary>
public static class FilterQueryFormatter
{
    private const string OrMarker = "\x00OR\x00";

    /// <summary>
    /// Formats <see cref="ContentFilterCriteria"/> as a query string.
    /// </summary>
    /// <param name="criteria">The criteria to format.</param>
    /// <returns>The formatted query string.</returns>
    public static string Format(ContentFilterCriteria criteria)
    {
        var parts = new List<string>();

        foreach (var item in criteria.ContentHierarchyItem.And)
        {
            parts.AddRange(FormatContentHierarchyItemCriteria(item));
        }

        foreach (var item in criteria.MediaFiles.And)
        {
            parts.AddRange(FormatMediaFileCriteria(item));
        }

        return JoinWithAnd(parts);
    }

    /// <summary>
    /// Formats <see cref="SubjectFilterCriteria"/> as a query string.
    /// </summary>
    /// <param name="criteria">The criteria to format.</param>
    /// <returns>The formatted query string.</returns>
    public static string Format(SubjectFilterCriteria criteria)
    {
        var parts = new List<string>();

        parts.AddRange(FormatPropertyCriteria(nameof(criteria.Kind), criteria.Kind));
        parts.AddRange(FormatPropertyCriteria(nameof(criteria.DateLastRefreshed), criteria.DateLastRefreshed));

        foreach (var item in criteria.Metadata.And)
        {
            parts.AddRange(FormatMetadataCriteria(item));
        }

        return JoinWithAnd(parts);
    }

    private static IEnumerable<string> FormatContentHierarchyItemCriteria(ContentHierarchyItemCriteria item)
    {
        var parts = new List<string>();

        parts.AddRange(FormatPropertyCriteria(nameof(item.ParentId), item.ParentId));
        parts.AddRange(FormatPropertyCriteria(nameof(item.RootFolderId), item.RootFolderId));
        parts.AddRange(FormatPropertyCriteria(nameof(item.ContentType), item.ContentType));
        parts.AddRange(FormatPropertyCriteria(nameof(item.Level), item.Level));
        parts.AddRange(FormatPropertyCriteria(nameof(item.Slug), item.Slug));
        parts.AddRange(FormatPropertyCriteria(nameof(item.Name), item.Name));
        parts.AddRange(FormatPropertyCriteria(nameof(item.Monitoring), item.Monitoring));
        parts.AddRange(FormatPropertyCriteria(nameof(item.Path), item.Path));

        foreach (var meta in item.Metadata.And)
        {
            parts.AddRange(FormatMetadataCriteria(meta));
        }

        return parts;
    }

    private static IEnumerable<string> FormatMediaFileCriteria(MediaFileCriteria item)
    {
        var parts = new List<string>();

        parts.AddRange(FormatPropertyCriteria(nameof(item.FileKind), item.FileKind));
        parts.AddRange(FormatPropertyCriteria(nameof(item.MimeType), item.MimeType));
        parts.AddRange(FormatPropertyCriteria(nameof(item.FilePath), item.FilePath));
        parts.AddRange(FormatPropertyCriteria(nameof(item.FileSize), item.FileSize));

        return parts;
    }

    private static IEnumerable<string> FormatMetadataCriteria(MetadataCriteria meta)
    {
        var parts = new List<string>();

        parts.AddRange(FormatPropertyCriteria(nameof(meta.Title), meta.Title));
        parts.AddRange(FormatPropertyCriteria(nameof(meta.OriginalTitle), meta.OriginalTitle));
        parts.AddRange(FormatPropertyCriteria(nameof(meta.Overview), meta.Overview));
        parts.AddRange(FormatPropertyCriteria(nameof(meta.Language), meta.Language));
        parts.AddRange(FormatPropertyCriteria(nameof(meta.PublishDate), meta.PublishDate));
        parts.AddRange(FormatPropertyCriteria(nameof(meta.Duration), meta.Duration));
        parts.AddRange(FormatPropertyCriteria(nameof(meta.ProductionStatus), meta.ProductionStatus));
        parts.AddRange(FormatPropertyCriteria(nameof(meta.BirthDate), meta.BirthDate));
        parts.AddRange(FormatPropertyCriteria(nameof(meta.DeathDate), meta.DeathDate));
        parts.AddRange(FormatPropertyCriteria(nameof(meta.Established), meta.Established));
        parts.AddRange(FormatPropertyCriteria(nameof(meta.Dissolved), meta.Dissolved));

        parts.AddRange(FormatCollectionCriteria(
            nameof(Genre),
            nameof(meta.Genres),
            meta.Genres,
            FormatGenreCriteria,
            IsSimpleGenreCriteria));
        parts.AddRange(FormatCollectionCriteria(
            nameof(Tag),
            nameof(meta.Tags),
            meta.Tags,
            FormatTagCriteria,
            IsSimpleTagCriteria));
        parts.AddRange(FormatCollectionCriteria(
            nameof(Award),
            nameof(meta.Awards),
            meta.Awards,
            FormatAwardCriteria,
            _ => false));
        parts.AddRange(FormatCollectionCriteria(
            nameof(ExternalId),
            nameof(meta.ExternalIds),
            meta.ExternalIds,
            FormatExternalIdCriteria,
            _ => false));
        parts.AddRange(FormatCollectionCriteria(
            nameof(ContentRating),
            nameof(meta.ContentRatings),
            meta.ContentRatings,
            FormatContentRatingCriteria,
            _ => false));
        parts.AddRange(FormatCollectionCriteria(
            nameof(Image),
            nameof(meta.Images),
            meta.Images,
            FormatImageCriteria,
            _ => false));

        return parts;
    }

    private static IEnumerable<string> FormatPropertyCriteria<T>(
        string fieldName,
        CriteriaSet<PropertyCriterion<T>> criteriaSet)
    {
        foreach (var criterion in criteriaSet.And)
        {
            yield return FormatPropertyCriterion(fieldName, criterion);
        }
    }

    private static string FormatPropertyCriterion<T>(string fieldName, PropertyCriterion<T> criterion)
    {
        var op = FormatOperator(criterion.Operator);

        if (criterion.Operator is CriteriaValueOperator.IsNull
            or CriteriaValueOperator.IsNotNull)
        {
            return $"{fieldName} {op}";
        }

        if (criterion.Operator == CriteriaValueOperator.In || criterion.Operator == CriteriaValueOperator.NotIn)
        {
            var values = criterion.Values ?? Enumerable.Empty<T>();
            var formattedValues = string.Join(", ", values.Select(v => FormatValue(v)));
            return $"{fieldName} {op} ({formattedValues})";
        }

        return $"{fieldName} {op} {FormatValue(criterion.Value)}";
    }

    private static readonly Dictionary<CriteriaValueOperator, FilterQueryToken> OperatorTokenMap = new()
    {
        [CriteriaValueOperator.Equals] = FilterQueryToken.Equals,
        [CriteriaValueOperator.NotEquals] = FilterQueryToken.NotEquals,
        [CriteriaValueOperator.GreaterThan] = FilterQueryToken.GreaterThan,
        [CriteriaValueOperator.GreaterThanOrEqual] = FilterQueryToken.GreaterThanOrEqual,
        [CriteriaValueOperator.LessThan] = FilterQueryToken.LessThan,
        [CriteriaValueOperator.LessThanOrEqual] = FilterQueryToken.LessThanOrEqual,
        [CriteriaValueOperator.ContainsString] = FilterQueryToken.Contains,
        [CriteriaValueOperator.NotContainsString] = FilterQueryToken.NotContains,
        [CriteriaValueOperator.StartsWithString] = FilterQueryToken.StartsWith,
        [CriteriaValueOperator.NotStartsWithString] = FilterQueryToken.NotStartsWith,
        [CriteriaValueOperator.EndsWithString] = FilterQueryToken.EndsWith,
        [CriteriaValueOperator.NotEndsWithString] = FilterQueryToken.NotEndsWith,
        [CriteriaValueOperator.In] = FilterQueryToken.In,
        [CriteriaValueOperator.NotIn] = FilterQueryToken.NotIn,
        [CriteriaValueOperator.IsNull] = FilterQueryToken.Null,
        [CriteriaValueOperator.IsNotNull] = FilterQueryToken.NotNull,
    };

    private static string FormatOperator(CriteriaValueOperator op)
    {
        if (OperatorTokenMap.TryGetValue(op, out var token))
        {
            return token.GetTokenText()
                ?? throw new ArgumentOutOfRangeException(nameof(op), op, "Unknown operator");
        }

        throw new ArgumentOutOfRangeException(nameof(op), op, "Unknown operator");
    }

    private static string FormatValue<T>(T value)
    {
        return value switch
        {
            null => "null",
            string s => $"\"{EscapeString(s)}\"",
            DateTime dt => $"\"{dt:yyyy-MM-dd}\"",
            TimeSpan ts => $"\"{FormatTimeSpan(ts)}\"",
            Guid g => $"\"{g}\"",
            Enum e => e.ToString(),
            bool b => b ? "true" : "false",
            _ => value.ToString() ?? string.Empty
        };
    }

    private static string EscapeString(string s)
    {
        return s.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }

    private static string FormatTimeSpan(TimeSpan ts)
    {
        if (ts.Days > 0)
        {
            return ts.ToString(@"d\.hh\:mm\:ss");
        }

        return ts.ToString(@"hh\:mm\:ss");
    }

    private static IEnumerable<string> FormatCollectionCriteria<T>(
        string singularName,
        string pluralName,
        CollectionCriteria<T> criteria,
        Func<T, string> formatItem,
        Func<T, bool> isSimple)
    {
        var parts = new List<string>();

        if (criteria.Some.Count > 0)
        {
            parts.AddRange(FormatCollectionSome(singularName, pluralName, criteria.Some, formatItem, isSimple));
        }

        if (criteria.None.Count > 0)
        {
            parts.AddRange(FormatCollectionNone(singularName, pluralName, criteria.None, formatItem, isSimple));
        }

        return parts;
    }

    private static IEnumerable<string> FormatCollectionSome<T>(
        string singularName,
        string pluralName,
        List<T> items,
        Func<T, string> formatItem,
        Func<T, bool> isSimple)
    {
        var formatted = items.Select(item =>
        {
            var content = formatItem(item);
            if (isSimple(item))
            {
                return $"{singularName} has {content}";
            }

            return $"{pluralName} has ({content})";
        }).ToList();

        if (formatted.Count == 1)
        {
            yield return formatted[0];
        }
        else if (formatted.Count > 1)
        {
            yield return OrMarker + string.Join(" or ", formatted);
        }
    }

    private static IEnumerable<string> FormatCollectionNone<T>(
        string singularName,
        string pluralName,
        List<T> items,
        Func<T, string> formatItem,
        Func<T, bool> isSimple)
    {
        foreach (var item in items)
        {
            var content = formatItem(item);
            if (isSimple(item))
            {
                yield return $"{singularName} !has {content}";
            }
            else
            {
                yield return $"{pluralName} !has ({content})";
            }
        }
    }

    private static bool IsSimpleGenreCriteria(GenreCriteria criteria)
    {
        return criteria.Name.And.Count() == 1 &&
               criteria.Name.And.First().Operator == CriteriaValueOperator.Equals;
    }

    private static string FormatGenreCriteria(GenreCriteria criteria)
    {
        if (IsSimpleGenreCriteria(criteria))
        {
            return FormatValue(criteria.Name.And.First().Value);
        }

        return FormatCriteriaFields(nameof(criteria.Name), criteria.Name);
    }

    private static bool IsSimpleTagCriteria(TagCriteria criteria)
    {
        return criteria.Name.And.Count() == 1 &&
               criteria.Name.And.First().Operator == CriteriaValueOperator.Equals;
    }

    private static string FormatTagCriteria(TagCriteria criteria)
    {
        if (IsSimpleTagCriteria(criteria))
        {
            return FormatValue(criteria.Name.And.First().Value);
        }

        return FormatCriteriaFields(nameof(criteria.Name), criteria.Name);
    }

    private static string FormatAwardCriteria(AwardCriteria criteria)
    {
        var parts = new List<string>();

        parts.AddRange(FormatPropertyCriteria(nameof(criteria.Body), criteria.Body));
        parts.AddRange(FormatPropertyCriteria(nameof(criteria.Name), criteria.Name));
        parts.AddRange(FormatPropertyCriteria(nameof(criteria.Category), criteria.Category));
        parts.AddRange(FormatPropertyCriteria(nameof(criteria.Status), criteria.Status));
        parts.AddRange(FormatPropertyCriteria(nameof(criteria.Year), criteria.Year));
        parts.AddRange(FormatPropertyCriteria(nameof(criteria.Language), criteria.Language));
        parts.AddRange(FormatPropertyCriteria(nameof(criteria.Country), criteria.Country));

        return JoinWithAnd(parts);
    }

    private static string FormatExternalIdCriteria(ExternalIdCriteria criteria)
    {
        var parts = new List<string>();

        parts.AddRange(FormatPropertyCriteria(nameof(criteria.Source), criteria.Source));
        parts.AddRange(FormatPropertyCriteria(nameof(criteria.Value), criteria.Value));

        return JoinWithAnd(parts);
    }

    private static string FormatContentRatingCriteria(ContentRatingCriteria criteria)
    {
        var parts = new List<string>();

        parts.AddRange(FormatPropertyCriteria(nameof(criteria.Body), criteria.Body));
        parts.AddRange(FormatPropertyCriteria(nameof(criteria.Rating), criteria.Rating));

        return JoinWithAnd(parts);
    }

    private static string FormatImageCriteria(ImageCriteria criteria)
    {
        var parts = new List<string>();

        parts.AddRange(FormatPropertyCriteria(nameof(criteria.Type), criteria.Type));
        parts.AddRange(FormatPropertyCriteria(nameof(criteria.Url), criteria.Url));
        parts.AddRange(FormatPropertyCriteria(nameof(criteria.Language), criteria.Language));

        return JoinWithAnd(parts);
    }

    private static string FormatCriteriaFields<T>(string fieldName, CriteriaSet<PropertyCriterion<T>> criteriaSet)
    {
        var parts = FormatPropertyCriteria(fieldName, criteriaSet).ToList();
        return JoinWithAnd(parts);
    }

    private static string JoinWithAnd(IEnumerable<string> parts)
    {
        return JoinWithAnd(parts.ToList());
    }

    private static string JoinWithAnd(List<string> parts)
    {
        if (parts.Count == 0)
        {
            return string.Empty;
        }

        if (parts.Count == 1)
        {
            var single = parts[0];
            if (single.StartsWith(OrMarker))
            {
                return single.Substring(OrMarker.Length);
            }

            return single;
        }

        var sb = new StringBuilder();
        for (var i = 0; i < parts.Count; i++)
        {
            if (i > 0)
            {
                sb.Append(" and ");
            }

            var part = parts[i];
            if (part.StartsWith(OrMarker))
            {
                sb.Append($"({part.Substring(OrMarker.Length)})");
            }
            else
            {
                sb.Append(part);
            }
        }

        return sb.ToString();
    }
}
