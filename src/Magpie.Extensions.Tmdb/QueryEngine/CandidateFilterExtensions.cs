using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

using Magpie.Extensions.Tmdb.Mapping;
using Magpie.Models;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Filtering;
using Magpie.Sdk.Content;

namespace Magpie.Extensions.Tmdb.QueryEngine;

internal static class CandidateFilterExtensions
{
    internal static IReadOnlyList<int> ResolveGenreIds(
        this GenreFilter filter,
        IReadOnlyDictionary<string, int> genreIds)
    {
        return [.. filter.Alternatives
            .SelectMany(alternative => ResolveGenreAlternative(alternative, genreIds))
            .Distinct()];
    }

    internal static IReadOnlyList<int> ResolveGenreAlternative(
        this IReadOnlyList<GenrePredicate> alternative,
        IReadOnlyDictionary<string, int> genreIds)
    {
        return [.. genreIds.Where(pair => alternative.All(predicate =>
            predicate.Negate != MatchesText(pair.Key, predicate.Operator, predicate.Values)))
            .Select(pair => pair.Value)
            .Distinct()];
    }

    internal static bool Matches(
        this SearchHit hit,
        FilterPlanNode evaluation,
        IReadOnlyDictionary<string, int> genreIds,
        IReadOnlyList<ResolvedSubjectGroup> subjects,
        bool deferDetailFilters = false,
        DiscoverRequest? appliedCriteria = null,
        ISet<ResolvedSubjectGroup>? satisfiedSubjectGroups = null,
        string? imageBaseUrl = null,
        Action<string>? rejected = null)
    {
        foreach (var filter in evaluation.TextFilters)
        {
            if (appliedCriteria?.AppliedTextFilters.Contains(filter) == true ||
                deferDetailFilters && filter.Field == TextField.ProductionStatus)
            {
                continue;
            }

            var candidate = filter.Field switch
            {
                TextField.Title => hit.Title,
                TextField.OriginalTitle => hit.OriginalTitle,
                TextField.Language => hit.OriginalLanguage,
                TextField.Overview => hit.Overview,
                TextField.ProductionStatus => hit.ProductionStatus,
                _ => null
            };
            var matches = filter.Field == TextField.Language
                ? MatchesLanguage(candidate, filter.Operator, filter.Values)
                : MatchesText(candidate, filter.Operator, filter.Values);
            if (filter.Negate ? matches : !matches)
            {
                rejected?.Invoke("text:" + filter.Field);
                return false;
            }
        }

        foreach (var filter in evaluation.PublishDateFilters)
        {
            if (appliedCriteria?.AppliedPublishDateFilters.Contains(filter) == true)
            {
                continue;
            }

            var matches = filter.Operator switch
            {
                CriteriaValueOperator.IsNull => hit.ReleaseDate is null,
                CriteriaValueOperator.IsNotNull => hit.ReleaseDate is not null,
                _ => hit.ReleaseDate is { } date &&
                    new TemporalValue
                    {
                        Value = DateTime.SpecifyKind(date.Date, DateTimeKind.Utc),
                        Precision = TemporalPrecision.Day
                    }.Compare(
                        filter.Operator,
                        filter.Values)
            };
            if (filter.Negate ? matches : !matches)
            {
                rejected?.Invoke("publishDate");
                return false;
            }
        }

        foreach (var filter in evaluation.DurationFilters)
        {
            if (appliedCriteria?.AppliedDurationFilters.Contains(filter) == true || deferDetailFilters)
            {
                continue;
            }

            var matches = filter.Operator switch
            {
                CriteriaValueOperator.IsNull => hit.Duration is null,
                CriteriaValueOperator.IsNotNull => hit.Duration is not null,
                _ => hit.Duration is { } duration && MatchesDuration(duration, filter)
            };
            if (filter.Negate ? matches : !matches)
            {
                rejected?.Invoke("duration");
                return false;
            }
        }

        foreach (var filter in evaluation.GenreFilters)
        {
            if (appliedCriteria?.AppliedGenreFilters.Contains(filter) == true)
            {
                continue;
            }

            var requested = ResolveGenreIds(filter, genreIds);
            var any = requested.Any(hit.GenreIds.Contains);
            if (filter.Excluded ? any : !any)
            {
                rejected?.Invoke("genre");
                return false;
            }
        }

        foreach (var group in subjects)
        {
            if (deferDetailFilters ||
                appliedCriteria?.AppliedSubjectGroups.Contains(group, ResolvedSubjectGroupComparer.Instance) == true ||
                satisfiedSubjectGroups?.Contains(group, ResolvedSubjectGroupComparer.Instance) == true)
            {
                continue;
            }

            var any = hit.SubjectReferences.Any(reference =>
                group.Subjects.Any(subject => MatchesSubject(reference, subject)) ||
                group.Alternatives.Any(alternative => MatchesSubject(reference, alternative)));
            if (group.Excluded ? any : !any)
            {
                rejected?.Invoke("relationship");
                return false;
            }
        }

        foreach (var filter in evaluation.ContentIdFilters.Where(filter => filter.Excluded))
        {
            var any = filter.Alternatives.Any(id => hit.ExternalIds.TryGetValue(id.Source, out var value) &&
                value.Equals(id.Value, StringComparison.OrdinalIgnoreCase));
            if (any)
            {
                rejected?.Invoke("contentIdentity");
                return false;
            }
        }

        foreach (var filter in evaluation.ElementFilters)
        {
            if (appliedCriteria?.AppliedElementFilters.Contains(filter) == true || deferDetailFilters)
            {
                continue;
            }

            var any = filter.Alternatives.Any(alternative =>
                AlternativeMatches(hit, filter.Family, alternative, imageBaseUrl));
            if (filter.Excluded ? any : !any)
            {
                rejected?.Invoke("element:" + filter.Family);
                return false;
            }
        }

        return true;
    }

    private static bool AlternativeMatches(
        SearchHit hit,
        ElementFamily family,
        IReadOnlyList<ElementPredicate> alternative,
        string? imageBaseUrl)
    {
        return family switch
        {
            ElementFamily.ContentRating => hit.ContentRatings.Any(rating =>
                alternative.All(predicate => MatchesContentRating(rating, predicate))),
            ElementFamily.AlternateText => hit.AlternateTexts.Any(text =>
                alternative.All(predicate => MatchesAlternateText(text, predicate))),
            ElementFamily.Preview => hit.Previews.Any(preview =>
                alternative.All(predicate => MatchesPreview(preview, predicate))),
            ElementFamily.Image => imageBaseUrl
                .ToImages(hit.PosterPath, hit.BackdropPath, hit.ImageGallery)
                .Any(image => alternative.All(predicate => MatchesImage(image, predicate))),
            ElementFamily.Reference => References(hit)
                .Any(reference => alternative.All(predicate => MatchesReference(reference, predicate))),
            _ => false
        };
    }

    private static IReadOnlyList<Reference> References(SearchHit hit)
    {
        hit.ExternalIds.TryGetValue("imdb", out var imdbId);
        return (hit.Route == ContentRoute.Movie ? "movie" : "tv").ToReferences(
            hit.Id.ToString(CultureInfo.InvariantCulture),
            imdbId,
            "title",
            hit.Homepage);
    }

    private static bool MatchesContentRating(ContentRating rating, ElementPredicate predicate)
    {
        var matches = predicate.Field switch
        {
            ElementField.RatingBody => MatchesText(rating.Body, predicate.Operator, predicate.StringValues),
            ElementField.RatingValue => MatchesText(rating.Rating, predicate.Operator, predicate.StringValues),
            _ => false
        };
        return predicate.Negate ? !matches : matches;
    }

    private static bool MatchesAlternateText(AlternateText text, ElementPredicate predicate)
    {
        var matches = predicate.Field switch
        {
            ElementField.TextKind => MatchesText(text.Kind, predicate.Operator, predicate.StringValues),
            ElementField.TextLanguage => MatchesText(text.Language, predicate.Operator, predicate.StringValues),
            ElementField.TextValue => MatchesText(text.Text, predicate.Operator, predicate.StringValues),
            _ => false
        };
        return predicate.Negate ? !matches : matches;
    }

    private static bool MatchesPreview(Preview preview, ElementPredicate predicate)
    {
        var matches = predicate.Field switch
        {
            ElementField.PreviewMimeType => MatchesText(preview.MimeType, predicate.Operator, predicate.StringValues),
            ElementField.PreviewUrl => MatchesText(preview.Url.ToString(), predicate.Operator, predicate.StringValues),
            ElementField.PreviewSource => MatchesText(preview.Source, predicate.Operator, predicate.StringValues),
            ElementField.PreviewTitle => MatchesText(preview.Title, predicate.Operator, predicate.StringValues),
            ElementField.PreviewReleaseDate => MatchesOrdered(
                preview.ReleaseDate,
                predicate.Operator,
                predicate.DateValues),
            ElementField.PreviewLanguage => MatchesText(preview.Language, predicate.Operator, predicate.StringValues),
            _ => false
        };
        return predicate.Negate ? !matches : matches;
    }

    private static bool MatchesImage(Image image, ElementPredicate predicate)
    {
        var matches = predicate.Field switch
        {
            ElementField.ImageType => MatchesText(image.Type, predicate.Operator, predicate.StringValues),
            ElementField.ImageMimeType => MatchesText(image.MimeType, predicate.Operator, predicate.StringValues),
            ElementField.ImageUrl => MatchesText(image.Url.ToString(), predicate.Operator, predicate.StringValues),
            ElementField.ImageSource => MatchesText(image.Source, predicate.Operator, predicate.StringValues),
            ElementField.ImageLanguage => MatchesText(image.Language, predicate.Operator, predicate.StringValues),
            ElementField.ImageWidth => MatchesOrdered(image.Width, predicate.Operator, predicate.NumberValues),
            ElementField.ImageHeight => MatchesOrdered(image.Height, predicate.Operator, predicate.NumberValues),
            _ => false
        };
        return predicate.Negate ? !matches : matches;
    }

    private static bool MatchesReference(Reference reference, ElementPredicate predicate)
    {
        var matches = predicate.Field switch
        {
            ElementField.ReferenceOrdinal => MatchesOrdered(
                reference.Ordinal,
                predicate.Operator,
                predicate.NumberValues),
            ElementField.ReferenceKind => MatchesText(reference.Kind, predicate.Operator, predicate.StringValues),
            ElementField.ReferenceUrl => MatchesText(
                reference.Url.ToString(),
                predicate.Operator,
                predicate.StringValues),
            ElementField.ReferenceDisplayName => MatchesText(
                reference.DisplayName,
                predicate.Operator,
                predicate.StringValues),
            ElementField.ReferenceLanguage => MatchesText(
                reference.Language,
                predicate.Operator,
                predicate.StringValues),
            _ => false
        };
        return predicate.Negate ? !matches : matches;
    }

    internal static bool MatchesText(this string? candidate, CriteriaValueOperator op, IReadOnlyList<string> requested)
    {
        if (op == CriteriaValueOperator.IsNull)
        {
            return string.IsNullOrWhiteSpace(candidate);
        }

        if (op == CriteriaValueOperator.IsNotNull)
        {
            return !string.IsNullOrWhiteSpace(candidate);
        }

        if (candidate is null || requested.Count == 0)
        {
            return false;
        }

        return op switch
        {
            CriteriaValueOperator.Equals or CriteriaValueOperator.In => requested.Any(value =>
                candidate.Equals(value, StringComparison.OrdinalIgnoreCase)),
            CriteriaValueOperator.ContainsString => requested.Any(value =>
                candidate.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0),
            CriteriaValueOperator.StartsWithString => requested.Any(value =>
                candidate.StartsWith(value, StringComparison.OrdinalIgnoreCase)),
            CriteriaValueOperator.EndsWithString => requested.Any(value =>
                candidate.EndsWith(value, StringComparison.OrdinalIgnoreCase)),
            CriteriaValueOperator.ContainsWord => requested.Any(value => Regex.IsMatch(
                candidate,
                "\\b" + Regex.Escape(value) + "\\b",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)),
            _ => false
        };
    }

    private static bool MatchesOrdered<T>(T? candidate, CriteriaValueOperator op, IReadOnlyList<T> requested)
        where T : struct, IComparable<T>
    {
        if (op == CriteriaValueOperator.IsNull)
        {
            return candidate is null;
        }

        if (op == CriteriaValueOperator.IsNotNull)
        {
            return candidate is not null;
        }

        if (candidate is null || requested.Count == 0)
        {
            return false;
        }

        return op switch
        {
            CriteriaValueOperator.Equals => candidate.Value.CompareTo(requested[0]) == 0,
            CriteriaValueOperator.In => requested.Contains(candidate.Value),
            CriteriaValueOperator.GreaterThan => candidate.Value.CompareTo(requested[0]) > 0,
            CriteriaValueOperator.GreaterThanOrEqual => candidate.Value.CompareTo(requested[0]) >= 0,
            CriteriaValueOperator.LessThan => candidate.Value.CompareTo(requested[0]) < 0,
            CriteriaValueOperator.LessThanOrEqual => candidate.Value.CompareTo(requested[0]) <= 0,
            _ => false
        };
    }

    private static bool MatchesDuration(TimeSpan candidate, DurationFilter filter)
    {
        if (filter.Values.Count == 0)
        {
            return false;
        }

        var requested = filter.Values[0];
        return filter.Operator switch
        {
            CriteriaValueOperator.Equals => candidate == requested,
            CriteriaValueOperator.GreaterThan => candidate > requested,
            CriteriaValueOperator.GreaterThanOrEqual => candidate >= requested,
            CriteriaValueOperator.LessThan => candidate < requested,
            CriteriaValueOperator.LessThanOrEqual => candidate <= requested,
            _ => false
        };
    }

    internal static bool MatchesSubject(
        this ProviderMetadataSearchSubjectReference reference,
        ResolvedSubject subject)
    {
        if (subject.Role == SubjectRole.Network && subject.ViaCompanySearch)
        {
            return reference.RelationshipKind == Constants.Network &&
                reference.DisplayName is { } name &&
                string.Equals(name.Trim(), subject.Name.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        if (!reference.ExternalIds.TryGetValue(Constants.ProviderType, out var value) ||
            !int.TryParse(value, out var id) ||
            id != subject.Id)
        {
            return false;
        }

        return MatchesSubjectRole(reference, subject.Role);
    }

    private static bool MatchesLanguage(string? candidate, CriteriaValueOperator op, IReadOnlyList<string> requested)
    {
        if (op is not (CriteriaValueOperator.Equals or CriteriaValueOperator.In))
        {
            return MatchesText(candidate, op, requested);
        }

        var normalizedCandidate = candidate.NormalizeTwoLetterLanguageCode() ?? candidate;
        var normalizedRequested = requested
            .Select(value => value.NormalizeTwoLetterLanguageCode() ?? value)
            .ToList();
        return MatchesText(normalizedCandidate, op, normalizedRequested);
    }

    private static bool MatchesSubject(ProviderMetadataSearchSubjectReference reference, SubjectAlternative alternative)
    {
        if (alternative.Criteria is not { } criteria ||
            !alternative.Roles.Any(role => MatchesSubjectRole(reference, role)))
        {
            return false;
        }

        return criteria.Kind.And.All(criterion => MatchesRawText(reference.RelationshipKind, criterion)) &&
            criteria.Type.And.All(criterion => MatchesRawText(reference.SubjectKind, criterion)) &&
            criteria.Subject.And.All(criterion => MatchesRawText(reference.DisplayName, criterion)) &&
            criteria.Qualifier.And.All(criterion => MatchesRawText(reference.RelationshipQualifier, criterion)) &&
            criteria.RelatedMetadataContainerId.And.All(criterion => MatchesAbsentValue(criterion)) &&
            criteria.ExternalIds.Some.All(group => group.Any(criterion =>
                reference.ExternalIds.Any(pair => MatchesExternalId(pair, criterion)))) &&
            criteria.ExternalIds.None.All(criterion =>
                reference.ExternalIds.All(pair => !MatchesExternalId(pair, criterion)));
    }

    private static bool MatchesSubjectRole(ProviderMetadataSearchSubjectReference reference, SubjectRole role)
    {
        return role switch
        {
            SubjectRole.Company => reference.RelationshipKind == Constants.Studio,
            SubjectRole.Network => reference.RelationshipKind == Constants.Network,
            SubjectRole.Person => reference.SubjectKind == Constants.Person,
            SubjectRole.Cast => reference.RelationshipKind == Constants.Cast,
            SubjectRole.Crew => reference.RelationshipKind is Constants.Crew
                or Constants.Director or Constants.Writer,
            SubjectRole.Director => reference.RelationshipKind == Constants.Director,
            SubjectRole.Writer => reference.RelationshipKind == Constants.Writer,
            _ => false
        };
    }

    private static bool MatchesExternalId(KeyValuePair<string, string> externalId, ExternalIdCriteria criteria)
    {
        return criteria.Source.And.All(criterion => MatchesRawText(externalId.Key, criterion)) &&
        criteria.Value.And.All(criterion => MatchesRawText(externalId.Value, criterion));
    }

    private static bool MatchesRawText(string? candidate, PropertyCriterion<string> criterion)
    {
        var (op, negate) = criterion.Operator.NormalizeOperator(criterion.Negate);
        var values = new List<string>();
        if (!string.IsNullOrWhiteSpace(criterion.Value))
        {
            values.Add(criterion.Value.Trim());
        }

        values.AddRange((criterion.Values ?? [])
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim()));
        var matches = MatchesText(candidate, op, [.. values.Distinct(StringComparer.OrdinalIgnoreCase)]);
        return negate ? !matches : matches;
    }

    private static bool MatchesAbsentValue<T>(PropertyCriterion<T> criterion)
    {
        var (op, negate) = criterion.Operator.NormalizeOperator(criterion.Negate);
        var matches = op == CriteriaValueOperator.IsNull;
        return negate ? !matches : matches;
    }

    internal static bool IsPositiveEquality(this PropertyCriterion<string> criterion)
    {
        var interpreted = criterion.Operator.NormalizeOperator(criterion.Negate);
        return !interpreted.Negate &&
            interpreted.Operator is CriteriaValueOperator.Equals or CriteriaValueOperator.In;
    }

    internal static bool IsPositiveSearchableText(this PropertyCriterion<string> criterion)
    {
        var interpreted = criterion.Operator.NormalizeOperator(criterion.Negate);
        return !interpreted.Negate &&
            interpreted.Operator is CriteriaValueOperator.Equals
                or CriteriaValueOperator.In
                or CriteriaValueOperator.ContainsString
                or CriteriaValueOperator.ContainsWord
                or CriteriaValueOperator.StartsWithString
                or CriteriaValueOperator.EndsWithString;
    }

    internal static (CriteriaValueOperator Operator, bool Negate) NormalizeOperator(
        this CriteriaValueOperator op,
        bool negate)
    {
        var (primitive, wasNegative) = op switch
        {
            CriteriaValueOperator.NotEquals => (CriteriaValueOperator.Equals, true),
            CriteriaValueOperator.NotIn => (CriteriaValueOperator.In, true),
            CriteriaValueOperator.NotContainsString => (CriteriaValueOperator.ContainsString, true),
            CriteriaValueOperator.NotStartsWithString => (CriteriaValueOperator.StartsWithString, true),
            CriteriaValueOperator.NotEndsWithString => (CriteriaValueOperator.EndsWithString, true),
            _ => (op, false)
        };
        var effective = negate ^ wasNegative;
        return (primitive, effective) switch
        {
            (CriteriaValueOperator.IsNull, true) => (CriteriaValueOperator.IsNotNull, false),
            (CriteriaValueOperator.IsNotNull, true) => (CriteriaValueOperator.IsNull, false),
            _ => (primitive, effective)
        };
    }
}
