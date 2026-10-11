using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Filtering;

namespace Magpie.QueryLanguage;

internal static class FilterCriteriaFormattingExtensions
{
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

    public static string? FormatFilterQueryOrDefault(this ContentFilterCriteria criteria)
    {
        if (criteria.MediaFiles.And.Any())
        {
            return null;
        }

        var parts = new List<QueryPart>();

        foreach (var item in criteria.ContentHierarchyItem.And)
        {
            parts.AddRange(FormatContentHierarchyItemCriteria(item));
        }

        return JoinWithAnd(parts);
    }

    public static string FormatFilterQuery(this SubjectFilterCriteria criteria)
    {
        var parts = new List<QueryPart>();

        parts.AddRange(FormatPropertyCriteria(nameof(criteria.Kind), criteria.Kind));
        parts.AddRange(FormatPropertyCriteria(nameof(criteria.DateLastRefreshed), criteria.DateLastRefreshed));

        foreach (var item in criteria.Metadata.And)
        {
            parts.AddRange(FormatMetadataCriteria(item));
        }

        return JoinWithAnd(parts);
    }

    private static IEnumerable<QueryPart> FormatContentHierarchyItemCriteria(ContentHierarchyItemCriteria item)
    {
        var parts = new List<QueryPart>();

        parts.AddRange(FormatPropertyCriteria(nameof(item.ParentId), item.ParentId));
        parts.AddRange(FormatPropertyCriteria(nameof(item.RootFolderId), item.RootFolderId));
        parts.AddRange(FormatPropertyCriteria(nameof(item.ContentType), item.ContentType));
        parts.AddRange(FormatPropertyCriteria(nameof(item.Level), item.Level));
        parts.AddRange(FormatPropertyCriteria(nameof(item.Slug), item.Slug));
        parts.AddRange(FormatPropertyCriteria(nameof(item.Name), item.Name));
        parts.AddRange(FormatPropertyCriteria(nameof(item.Monitoring), item.Monitoring));

        if (item.PendingMetadataRefresh is { } pendingMetadataRefresh)
        {
            parts.Add(new QueryPart(
                $"{nameof(item.PendingMetadataRefresh)} = {FormatValue(pendingMetadataRefresh)}",
                false));
        }

        if (item.HasSearchableTitle is { } hasSearchableTitle)
        {
            parts.Add(new QueryPart($"{nameof(item.HasSearchableTitle)} = {FormatValue(hasSearchableTitle)}", false));
        }

        if (item.ImportPending is { } importPending)
        {
            parts.Add(new QueryPart($"{nameof(item.ImportPending)} = {FormatValue(importPending)}", false));
        }

        parts.AddRange(FormatPropertyCriteria(nameof(item.Fulfillment), item.Fulfillment));
        parts.AddRange(FormatPropertyCriteria(nameof(item.ActiveAcquisitionStatus), item.ActiveAcquisitionStatus));
        parts.AddRange(FormatPropertyCriteria(nameof(item.QualityStatus), item.QualityStatus));
        parts.AddRange(FormatPropertyCriteria(nameof(item.PrimaryMediaSize), item.PrimaryMediaSize));
        parts.AddRange(FormatPropertyCriteria(nameof(item.PrimaryMediaFileCount), item.PrimaryMediaFileCount));
        parts.AddRange(FormatPropertyCriteria(nameof(item.Path), item.Path));
        parts.AddRange(FormatCollectionCriteria(
            nameof(MediaFile),
            nameof(item.MediaFiles),
            item.MediaFiles,
            FormatMediaFileCriteria,
            _ => false));
        parts.AddRange(FormatCollectionCriteria(
            nameof(item.Subtree),
            nameof(item.Subtree),
            item.Subtree,
            subtree => JoinWithAnd([.. FormatContentHierarchyItemCriteria(subtree)]),
            _ => false));

        foreach (var meta in item.Metadata.And)
        {
            parts.AddRange(FormatMetadataCriteria(meta));
        }

        return parts;
    }

    private static IEnumerable<QueryPart> FormatMetadataCriteria(MetadataCriteria meta)
    {
        var parts = new List<QueryPart>();

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
            genre => FormatNameCriteria(genre.Name),
            genre => IsSimpleNameCriteria(genre.Name)));
        parts.AddRange(FormatCollectionCriteria(
            nameof(Tag),
            nameof(meta.Tags),
            meta.Tags,
            tag => FormatNameCriteria(tag.Name),
            tag => IsSimpleNameCriteria(tag.Name)));
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
        parts.AddRange(FormatCollectionCriteria(
            nameof(Relationship),
            nameof(meta.Relationships),
            meta.Relationships,
            FormatRelationshipCriteria,
            IsSimpleRelationshipCriteria));

        return parts;
    }

    private static IEnumerable<QueryPart> FormatPropertyCriteria<T>(
        string fieldName,
        CriteriaSet<PropertyCriterion<T>> criteriaSet)
    {
        foreach (var criterion in criteriaSet.And)
        {
            yield return new QueryPart(FormatPropertyCriterion(fieldName, criterion), false);
        }
    }

    private static string FormatTemporalText(TemporalValue value)
    {
        return value.Precision switch
        {
            TemporalPrecision.Year => value.Value.ToString("yyyy", CultureInfo.InvariantCulture),
            TemporalPrecision.Month => value.Value.ToString("yyyy-MM", CultureInfo.InvariantCulture),
            TemporalPrecision.Day => value.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            _ => DateTime.SpecifyKind(value.Value, DateTimeKind.Utc).ToString("O", CultureInfo.InvariantCulture)
        };
    }

    private static string FormatPropertyCriterion<T>(string fieldName, PropertyCriterion<T> criterion)
    {
        var op = FormatOperator(criterion.Operator);

        if (criterion.Operator is CriteriaValueOperator.IsNull or CriteriaValueOperator.IsNotNull)
        {
            return $"{fieldName} {op}";
        }

        if (criterion.Operator is CriteriaValueOperator.In or CriteriaValueOperator.NotIn)
        {
            var values = criterion.Values ?? [];
            var formattedValues = string.Join(", ", values.Select(v => FormatValue(v)));
            return $"{fieldName} {op} ({formattedValues})";
        }

        return $"{fieldName} {op} {FormatValue(criterion.Value)}";
    }

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
            DateTime dt => $"\"{FormatDateTime(dt)}\"",
            TimeSpan ts => $"\"{FormatTimeSpan(ts)}\"",
            TemporalValue t => $"\"{FormatTemporalText(t)}\"",
            Guid g => $"\"{g}\"",
            Enum e => e.ToString(),
            bool b => b ? "true" : "false",
            _ => value.ToString() ?? string.Empty
        };
    }

    private static string FormatDateTime(DateTime dt)
    {
        return dt.TimeOfDay == TimeSpan.Zero
            ? dt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            : dt.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture);
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

    private static IEnumerable<QueryPart> FormatCollectionCriteria<T>(
        string singularName,
        string pluralName,
        CollectionCriteria<T> criteria,
        Func<T, string> formatItem,
        Func<T, bool> isSimple)
    {
        var parts = new List<QueryPart>();

        foreach (var group in criteria.Some)
        {
            parts.AddRange(FormatCollectionSome(singularName, pluralName, group, formatItem, isSimple));
        }

        if (criteria.None.Count > 0)
        {
            parts.AddRange(FormatCollectionNone(singularName, pluralName, criteria.None, formatItem, isSimple));
        }

        return parts;
    }

    private static IEnumerable<QueryPart> FormatCollectionSome<T>(
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
            yield return new QueryPart(formatted[0], false);
        }
        else if (formatted.Count > 1)
        {
            yield return new QueryPart(string.Join(" or ", formatted), true);
        }
    }

    private static IEnumerable<QueryPart> FormatCollectionNone<T>(
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
                yield return new QueryPart($"{singularName} !has {content}", false);
            }
            else
            {
                yield return new QueryPart($"{pluralName} !has ({content})", false);
            }
        }
    }

    private static bool IsSimpleNameCriteria(CriteriaSet<PropertyCriterion<string>> name)
    {
        return name.And.Count() == 1 && name.And.First().Operator == CriteriaValueOperator.Equals;
    }

    private static string FormatNameCriteria(CriteriaSet<PropertyCriterion<string>> name)
    {
        if (IsSimpleNameCriteria(name))
        {
            return FormatValue(name.And.First().Value);
        }

        return FormatCriteriaFields(nameof(GenreCriteria.Name), name);
    }

    private static string FormatAwardCriteria(AwardCriteria criteria)
    {
        var parts = new List<QueryPart>();

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
        var parts = new List<QueryPart>();

        parts.AddRange(FormatPropertyCriteria(nameof(criteria.Source), criteria.Source));
        parts.AddRange(FormatPropertyCriteria(nameof(criteria.Value), criteria.Value));

        return JoinWithAnd(parts);
    }

    private static string FormatContentRatingCriteria(ContentRatingCriteria criteria)
    {
        var parts = new List<QueryPart>();

        parts.AddRange(FormatPropertyCriteria(nameof(criteria.Body), criteria.Body));
        parts.AddRange(FormatPropertyCriteria(nameof(criteria.Rating), criteria.Rating));

        return JoinWithAnd(parts);
    }

    private static string FormatImageCriteria(ImageCriteria criteria)
    {
        var parts = new List<QueryPart>();

        parts.AddRange(FormatPropertyCriteria(nameof(criteria.Type), criteria.Type));
        parts.AddRange(FormatPropertyCriteria(nameof(criteria.MimeType), criteria.MimeType));
        parts.AddRange(FormatPropertyCriteria(nameof(criteria.Url), criteria.Url));
        parts.AddRange(FormatPropertyCriteria(nameof(criteria.Source), criteria.Source));
        parts.AddRange(FormatPropertyCriteria(nameof(criteria.Language), criteria.Language));
        parts.AddRange(FormatPropertyCriteria(nameof(criteria.Width), criteria.Width));
        parts.AddRange(FormatPropertyCriteria(nameof(criteria.Height), criteria.Height));

        return JoinWithAnd(parts);
    }

    private static string FormatMediaFileCriteria(MediaFileCriteria criteria)
    {
        var parts = new List<QueryPart>();

        parts.AddRange(FormatPropertyCriteria(nameof(criteria.FileKind), criteria.FileKind));
        parts.AddRange(FormatPropertyCriteria(nameof(criteria.MimeType), criteria.MimeType));
        parts.AddRange(FormatPropertyCriteria(nameof(criteria.FilePath), criteria.FilePath));
        parts.AddRange(FormatPropertyCriteria(nameof(criteria.FileSize), criteria.FileSize));

        return JoinWithAnd(parts);
    }

    private static bool IsSimpleRelationshipCriteria(RelationshipCriteria criteria)
    {
        return criteria.Kind.And.Count() == 1 &&
            criteria.Kind.And.First().Operator == CriteriaValueOperator.Equals &&
            !criteria.Type.And.Any() &&
            !criteria.Qualifier.And.Any() &&
            !criteria.Subject.And.Any() &&
            !criteria.RelatedMetadataContainerId.And.Any() &&
            criteria.ExternalIds.Some.Count == 0 &&
            criteria.ExternalIds.None.Count == 0;
    }

    private static string FormatRelationshipCriteria(RelationshipCriteria criteria)
    {
        if (IsSimpleRelationshipCriteria(criteria))
        {
            return FormatValue(criteria.Kind.And.First().Value);
        }

        var parts = new List<QueryPart>();

        parts.AddRange(FormatPropertyCriteria(nameof(criteria.Kind), criteria.Kind));
        parts.AddRange(FormatPropertyCriteria(nameof(criteria.Type), criteria.Type));
        parts.AddRange(FormatPropertyCriteria(nameof(criteria.Qualifier), criteria.Qualifier));
        parts.AddRange(FormatPropertyCriteria(nameof(criteria.Subject), criteria.Subject));
        parts.AddRange(FormatPropertyCriteria(
            nameof(criteria.RelatedMetadataContainerId),
            criteria.RelatedMetadataContainerId));

        foreach (var externalId in criteria.ExternalIds.SomeFlat)
        {
            parts.AddRange(FormatPropertyCriteria("ExternalId", externalId.Value));
            parts.AddRange(FormatPropertyCriteria("ExternalIdSource", externalId.Source));
        }

        return JoinWithAnd(parts);
    }

    private static string FormatCriteriaFields<T>(string fieldName, CriteriaSet<PropertyCriterion<T>> criteriaSet)
    {
        var parts = FormatPropertyCriteria(fieldName, criteriaSet).ToList();
        return JoinWithAnd(parts);
    }

    private static string JoinWithAnd(List<QueryPart> parts)
    {
        return parts.Count == 1
            ? parts[0].Text
            : string.Join(" and ", parts.Select(p => p.IsOrGroup ? $"({p.Text})" : p.Text));
    }

    private readonly record struct QueryPart(string Text, bool IsOrGroup);
}
