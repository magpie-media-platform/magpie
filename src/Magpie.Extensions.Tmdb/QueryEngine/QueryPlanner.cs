using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Filtering;

namespace Magpie.Extensions.Tmdb.QueryEngine;

internal sealed class QueryPlanner : IQueryPlanner
{
    public QueryPlan Plan(QueryRequest request)
    {
        var warnings = new List<string>();
        var textFilters = new List<TextFilter>();
        var temporalFilters = new List<TemporalFilter>();
        var durationFilters = new List<DurationFilter>();
        var genreFilters = new List<GenreFilter>();
        var subjectFilters = new List<SubjectFilter>();
        var contentIdFilters = new List<ContentIdFilter>();
        var elementFilters = new List<ElementFilter>();
        var knownEmpty = false;

        AddFilters(
            request.Criteria,
            request.Route,
            textFilters,
            temporalFilters,
            durationFilters,
            genreFilters,
            subjectFilters,
            contentIdFilters,
            elementFilters,
            warnings,
            ref knownEmpty);

        var ordinaryTerm = string.IsNullOrWhiteSpace(request.OrdinarySearchTerm)
            ? null
            : request.OrdinarySearchTerm.Trim();
        var requestedPage = Math.Max(1, request.Page);
        var strategy = knownEmpty
            ? QueryStrategy.Empty
            : SelectStrategy(
                request.Route,
                ordinaryTerm,
                textFilters,
                temporalFilters,
                durationFilters,
                genreFilters,
                subjectFilters,
                contentIdFilters,
                elementFilters);

        if (!knownEmpty && requestedPage > QueryLimits.MaximumPage)
        {
            strategy = QueryStrategy.Empty;
            warnings.Add(QueryLimits.PageLimitWarning);
        }

        var fingerprint = BuildFingerprint(
            request.Route,
            ordinaryTerm,
            textFilters,
            temporalFilters,
            durationFilters,
            genreFilters,
            subjectFilters,
            contentIdFilters,
            elementFilters,
            request.MaximumResults);
        var subjectBindings = subjectFilters.Select((filter, index) =>
            new SubjectResolutionBinding($"relationship:{index}", filter)).ToList();
        var plan = new QueryPlan
        {
            Root = BuildPlanTree(
                request.Route,
                strategy,
                ordinaryTerm,
                textFilters,
                temporalFilters,
                durationFilters,
                genreFilters,
                subjectBindings,
                contentIdFilters,
                elementFilters,
                requestedPage,
                request.MaximumResults,
                fingerprint),
            Warnings = knownEmpty ? [] : warnings,
            RequestedPage = requestedPage,
            MaximumResults = request.MaximumResults,
            DefaultLanguagePreference = request.DefaultLanguagePreference,
            Fingerprint = fingerprint
        };
        RecordPlan(plan, request.Route, knownEmpty, requestedPage > QueryLimits.MaximumPage);
        return plan;
    }

    private void AddFilters(
        MetadataCriteria criteria,
        ContentRoute route,
        List<TextFilter> textFilters,
        List<TemporalFilter> temporalFilters,
        List<DurationFilter> durationFilters,
        List<GenreFilter> genreFilters,
        List<SubjectFilter> subjectFilters,
        List<ContentIdFilter> contentIdFilters,
        List<ElementFilter> elementFilters,
        List<string> warnings,
        ref bool knownEmpty)
    {
        foreach (var criterion in criteria.ProviderType.And)
        {
            knownEmpty |= ExcludesProviderType(criterion);
        }

        foreach (var criterion in criteria.Source.And)
        {
            knownEmpty |= ExcludesSource(criterion, route);
        }

        AddTextFilters(criteria.Title, TextField.Title, textFilters, ref knownEmpty);
        AddTextFilters(criteria.OriginalTitle, TextField.OriginalTitle, textFilters, ref knownEmpty);
        AddTextFilters(criteria.Language, TextField.Language, textFilters, ref knownEmpty);
        AddTextFilters(criteria.Overview, TextField.Overview, textFilters, ref knownEmpty);
        AddTextFilters(criteria.ProductionStatus, TextField.ProductionStatus, textFilters, ref knownEmpty);
        AddTemporalFilters(criteria.PublishDate, temporalFilters, ref knownEmpty);
        AddDurationFilters(criteria.Duration, durationFilters, ref knownEmpty);

        AddAbsentPropertyCriteria(criteria.Established.And, ref knownEmpty);
        AddAbsentPropertyCriteria(criteria.Dissolved.And, ref knownEmpty);
        AddAbsentPropertyCriteria(criteria.BirthDate.And, ref knownEmpty);
        AddAbsentPropertyCriteria(criteria.DeathDate.And, ref knownEmpty);
        var customMetadataRequired = criteria.CustomMetadata.And
            .SelectMany(dictionary => dictionary.Items)
            .Any(item => AbsentPropertyExcludes(item.Value));
        if (customMetadataRequired)
        {
            knownEmpty = true;
        }

        if (criteria.Awards.Some.Count > 0)
        {
            knownEmpty = true;
        }

        if (criteria.Tags.Some.Count > 0)
        {
            knownEmpty = true;
        }

        foreach (var group in criteria.Genres.Some)
        {
            AddGenreFilter(group, excluded: false, genreFilters, ref knownEmpty);
        }

        foreach (var exclusion in criteria.Genres.None)
        {
            AddGenreFilter([exclusion], excluded: true, genreFilters, ref knownEmpty);
        }

        foreach (var group in criteria.Relationships.Some)
        {
            AddSubjectFilter(group, route, excluded: false, subjectFilters, ref knownEmpty);
        }

        foreach (var exclusion in criteria.Relationships.None)
        {
            AddSubjectFilter([exclusion], route, excluded: true, subjectFilters, ref knownEmpty);
        }

        foreach (var group in criteria.ExternalIds.Some)
        {
            AddContentIdFilter(group, route, excluded: false, contentIdFilters, warnings, ref knownEmpty);
        }

        foreach (var exclusion in criteria.ExternalIds.None)
        {
            AddContentIdFilter([exclusion], route, excluded: true, contentIdFilters, warnings, ref knownEmpty);
        }

        AddElementFilters(criteria, elementFilters, ref knownEmpty);
    }

    private bool ExcludesProviderType(PropertyCriterion<string> criterion)
    {
        var (op, negate) = criterion.Operator.NormalizeOperator(criterion.Negate);
        if (op == CriteriaValueOperator.IsNull)
        {
            return true;
        }

        if (op == CriteriaValueOperator.IsNotNull)
        {
            return false;
        }

        if (!IsTextOperator(op))
        {
            return false;
        }

        var values = TextValues(criterion);
        if (values.Count == 0)
        {
            return false;
        }

        var matches = values.Any(value => MatchesIdentity(Constants.ProviderType, op, value));
        return matches == negate;
    }

    private bool ExcludesSource(PropertyCriterion<string> criterion, ContentRoute route)
    {
        var (op, negate) = criterion.Operator.NormalizeOperator(criterion.Negate);
        if (op == CriteriaValueOperator.IsNull)
        {
            return true;
        }

        if (op == CriteriaValueOperator.IsNotNull)
        {
            return false;
        }

        if (!IsTextOperator(op))
        {
            return false;
        }

        var values = TextValues(criterion);
        if (values.Count == 0)
        {
            return false;
        }

        var prefix = string.Concat(Constants.ProviderType, ":", RouteSourceSegment(route), ":");
        if (!negate)
        {
            return !values.Any(value => SourceKeySatisfiable(prefix, op, value));
        }

        return values.Any(value => SourceKeyTautology(prefix, op, value));
    }

    private string RouteSourceSegment(ContentRoute route)
    {
        return route switch
        {
            ContentRoute.Movie => "movie",
            ContentRoute.TvShow => "tv",
            ContentRoute.TvSeason => "season",
            _ => "episode"
        };
    }

    private bool SourceKeySatisfiable(string prefix, CriteriaValueOperator op, string value)
    {
        var sample = prefix + SampleSourceId(value);
        return op switch
        {
            CriteriaValueOperator.Equals or CriteriaValueOperator.In =>
                string.Equals(value, Constants.ProviderType, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, sample, StringComparison.OrdinalIgnoreCase),
            CriteriaValueOperator.ContainsString => sample.Contains(value, StringComparison.OrdinalIgnoreCase),
            CriteriaValueOperator.StartsWithString => sample.StartsWith(value, StringComparison.OrdinalIgnoreCase),
            CriteriaValueOperator.EndsWithString => sample.EndsWith(value, StringComparison.OrdinalIgnoreCase),
            CriteriaValueOperator.ContainsWord => ContainsIdentityWord(sample, value),
            _ => true
        };
    }

    private bool SourceKeyTautology(string prefix, CriteriaValueOperator op, string value)
    {
        if (op is CriteriaValueOperator.Equals or CriteriaValueOperator.In)
        {
            return string.Equals(value, Constants.ProviderType, StringComparison.OrdinalIgnoreCase);
        }

        var first = prefix + "1";
        var second = prefix + "2";
        return op switch
        {
            CriteriaValueOperator.ContainsString =>
                first.Contains(value, StringComparison.OrdinalIgnoreCase) &&
                second.Contains(value, StringComparison.OrdinalIgnoreCase),
            CriteriaValueOperator.StartsWithString =>
                first.StartsWith(value, StringComparison.OrdinalIgnoreCase) &&
                second.StartsWith(value, StringComparison.OrdinalIgnoreCase),
            CriteriaValueOperator.EndsWithString =>
                first.EndsWith(value, StringComparison.OrdinalIgnoreCase) &&
                second.EndsWith(value, StringComparison.OrdinalIgnoreCase),
            CriteriaValueOperator.ContainsWord =>
                ContainsIdentityWord(first, value) && ContainsIdentityWord(second, value),
            _ => false
        };
    }

    private string SampleSourceId(string value)
    {
        var boundary = value.Length;
        while (boundary > 0 && value[boundary - 1] is >= '0' and <= '9')
        {
            boundary--;
        }

        var digits = value.Substring(boundary);
        if (digits.Length == 0)
        {
            return "1";
        }

        return digits[0] == '0' ? "1" + digits : digits;
    }

    private bool MatchesIdentity(string identity, CriteriaValueOperator op, string value)
    {
        return op switch
        {
            CriteriaValueOperator.Equals or CriteriaValueOperator.In =>
                string.Equals(identity, value, StringComparison.OrdinalIgnoreCase),
            CriteriaValueOperator.ContainsString => identity.Contains(value, StringComparison.OrdinalIgnoreCase),
            CriteriaValueOperator.StartsWithString => identity.StartsWith(value, StringComparison.OrdinalIgnoreCase),
            CriteriaValueOperator.EndsWithString => identity.EndsWith(value, StringComparison.OrdinalIgnoreCase),
            CriteriaValueOperator.ContainsWord => ContainsIdentityWord(identity, value),
            _ => false
        };
    }

    private bool ContainsIdentityWord(string candidate, string value)
    {
        return Regex.IsMatch(
            candidate,
            string.Concat("(?<![\\p{L}\\p{N}])", Regex.Escape(value.Trim()), "(?![\\p{L}\\p{N}])"),
            RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    }

    private bool AbsentPropertyExcludes<T>(PropertyCriterion<T> criterion)
    {
        var (op, negate) = criterion.Operator.NormalizeOperator(criterion.Negate);
        if (op == CriteriaValueOperator.IsNull)
        {
            return false;
        }

        if (op == CriteriaValueOperator.IsNotNull)
        {
            return true;
        }

        return !negate;
    }

    private void AddAbsentPropertyCriteria<T>(
        IEnumerable<PropertyCriterion<T>> criteria,
        ref bool knownEmpty)
    {
        if (!criteria.Any(AbsentPropertyExcludes))
        {
            return;
        }

        knownEmpty = true;
    }

    private void AddTextFilters(
        CriteriaSet<PropertyCriterion<string>> criteria,
        TextField field,
        List<TextFilter> filters,
        ref bool knownEmpty)
    {
        foreach (var criterion in criteria.And)
        {
            var (op, negate) = criterion.Operator.NormalizeOperator(criterion.Negate);
            var values = TextValues(criterion);
            if (!IsTextOperator(op) ||
                values.Count == 0 &&
                op is not CriteriaValueOperator.IsNull
                    and not CriteriaValueOperator.IsNotNull)
            {
                knownEmpty = true;
                continue;
            }

            filters.Add(new TextFilter(field, op, negate, values));
        }
    }

    private void AddTemporalFilters(
        CriteriaSet<PropertyCriterion<TemporalValue>> criteria,
        List<TemporalFilter> filters,
        ref bool knownEmpty)
    {
        foreach (var criterion in criteria.And)
        {
            var (op, negate) = criterion.Operator.NormalizeOperator(criterion.Negate);
            var values = TemporalValues(criterion);
            var nullCheck = op is CriteriaValueOperator.IsNull or CriteriaValueOperator.IsNotNull;
            if (!nullCheck &&
                (values.Count == 0 ||
                    values.Any(value => value.Precision == TemporalPrecision.Instant) ||
                    op is not (CriteriaValueOperator.Equals
                        or CriteriaValueOperator.In
                        or CriteriaValueOperator.GreaterThan
                        or CriteriaValueOperator.GreaterThanOrEqual
                        or CriteriaValueOperator.LessThan
                        or CriteriaValueOperator.LessThanOrEqual)))
            {
                knownEmpty = true;
                continue;
            }

            filters.Add(new TemporalFilter(op, negate, values));
        }
    }

    private void AddDurationFilters(
        CriteriaSet<PropertyCriterion<TimeSpan>> criteria,
        List<DurationFilter> filters,
        ref bool knownEmpty)
    {
        foreach (var criterion in criteria.And)
        {
            var (op, negate) = criterion.Operator.NormalizeOperator(criterion.Negate);
            var values = DurationValues(criterion);
            var nullCheck = op is CriteriaValueOperator.IsNull or CriteriaValueOperator.IsNotNull;
            if (!nullCheck && (values.Count == 0 || values.Any(value => value < TimeSpan.Zero) || !IsRangeOperator(op)))
            {
                knownEmpty = true;
                continue;
            }

            filters.Add(new DurationFilter(op, negate, values));
        }
    }

    private void AddGenreFilter(
        IReadOnlyList<GenreCriteria> alternatives,
        bool excluded,
        List<GenreFilter> filters,
        ref bool knownEmpty)
    {
        var compiled = new List<IReadOnlyList<GenrePredicate>>();
        var incomplete = false;
        foreach (var alternative in alternatives)
        {
            var predicates = new List<GenrePredicate>();
            var unusable = false;
            foreach (var criterion in alternative.Name.And)
            {
                var (op, negate) = criterion.Operator.NormalizeOperator(criterion.Negate);
                var values = TextValues(criterion);
                if (!IsTextOperator(op) ||
                    values.Count == 0 &&
                    op is not CriteriaValueOperator.IsNull
                        and not CriteriaValueOperator.IsNotNull)
                {
                    unusable = true;
                    incomplete = true;
                    break;
                }

                predicates.Add(new GenrePredicate(op, negate, values));
            }

            if (!unusable && predicates.Count > 0)
            {
                compiled.Add(predicates);
            }
        }

        if (compiled.Count > 0)
        {
            filters.Add(new GenreFilter(excluded, compiled));
        }

        if (compiled.Count == 0 && (!excluded || incomplete))
        {
            knownEmpty = true;
        }
    }

    private void AddSubjectFilter(
        IReadOnlyList<RelationshipCriteria> relationships,
        ContentRoute route,
        bool excluded,
        List<SubjectFilter> filters,
        ref bool knownEmpty)
    {
        var alternatives = new List<SubjectAlternative>();
        var incomplete = false;
        var cannotCompile = false;
        var requiresLocalVerification = false;
        var networkOnlyAlternatives = 0;
        foreach (var relationship in relationships)
        {
            if (!TryMapRoles(
                relationship,
                route,
                out var roles,
                out var rolesIncomplete,
                out var rolesCannotCompile))
            {
                incomplete = true;
                cannotCompile |= rolesCannotCompile;
                continue;
            }

            incomplete |= rolesIncomplete;
            cannotCompile |= rolesCannotCompile;
            requiresLocalVerification |= rolesIncomplete;
            if (route == ContentRoute.Movie && roles.Contains(SubjectRole.Network))
            {
                roles = [.. roles.Where(role => role != SubjectRole.Network)];
                if (roles.Count == 0)
                {
                    networkOnlyAlternatives++;
                    continue;
                }
            }

            if (relationship.Qualifier.And.Any() || relationship.RelatedMetadataContainerId.And.Any())
            {
                incomplete = true;
                cannotCompile |= relationship.Qualifier.And.Any(criterion =>
                    !IsTextOperator(criterion.Operator.NormalizeOperator(criterion.Negate).Operator));
                requiresLocalVerification = true;
            }

            var identityClauses = new List<IReadOnlyList<SubjectIdentity>>();
            foreach (var criterion in relationship.Subject.And)
            {
                var (op, negate) = criterion.Operator.NormalizeOperator(criterion.Negate);
                if (negate || !IsTextOperator(op))
                {
                    incomplete = true;
                    cannotCompile |= !IsTextOperator(op);
                    requiresLocalVerification = true;
                    continue;
                }

                var clause = TextValues(criterion)
                    .Select(name => new SubjectIdentity(name, null, null, op, negate))
                    .ToList();
                if (clause.Count > 0)
                {
                    identityClauses.Add(clause);
                }
                else
                {
                    incomplete = true;
                    requiresLocalVerification = true;
                }
            }

            foreach (var group in relationship.ExternalIds.Some)
            {
                if (group.Any(externalId =>
                    externalId.Source.And.Any(criterion =>
                        !criterion.IsPositiveEquality()) ||
                    externalId.Value.And.Any(criterion =>
                        !criterion.IsPositiveEquality())))
                {
                    incomplete = true;
                    cannotCompile |= group.Any(externalId =>
                        externalId.Source.And.Concat(externalId.Value.And).Any(criterion =>
                            !IsTextOperator(criterion.Operator.NormalizeOperator(criterion.Negate).Operator)));
                    requiresLocalVerification = true;
                }

                var clause = ExternalIdIdentities(group);
                if (clause.Count > 0)
                {
                    identityClauses.Add(clause);
                }
                else
                {
                    incomplete = true;
                    requiresLocalVerification = true;
                }
            }

            if (relationship.ExternalIds.None.Any())
            {
                incomplete = true;
                cannotCompile |= relationship.ExternalIds.None.Any(externalId =>
                    externalId.Source.And.Concat(externalId.Value.And).Any(criterion =>
                        !IsTextOperator(criterion.Operator.NormalizeOperator(criterion.Negate).Operator)));
                requiresLocalVerification = true;
            }

            if (identityClauses.Count == 0)
            {
                incomplete = true;
                requiresLocalVerification = true;
            }

            alternatives.Add(new SubjectAlternative(roles, identityClauses, relationship));
        }

        if (networkOnlyAlternatives > 0 && alternatives.Count == 0 && !incomplete)
        {
            if (!excluded)
            {
                knownEmpty = true;
            }

            return;
        }

        if (alternatives.Count > 0)
        {
            filters.Add(new SubjectFilter(excluded, alternatives, requiresLocalVerification));
        }

        if (incomplete && (excluded ? cannotCompile : alternatives.Count == 0))
        {
            knownEmpty = true;
        }
    }

    private void AddContentIdFilter(
        IReadOnlyList<ExternalIdCriteria> ids,
        ContentRoute route,
        bool excluded,
        List<ContentIdFilter> filters,
        List<string> warnings,
        ref bool knownEmpty)
    {
        var alternatives = new List<ContentId>();
        var invalid = false;
        var unsupportedSource = false;
        foreach (var id in ids)
        {
            if (id.Source.And.Any(criterion => !criterion.IsPositiveEquality()) ||
                id.Value.And.Any(criterion => !criterion.IsPositiveEquality()))
            {
                invalid = true;
            }

            var sources = PositiveEqualsValues(id.Source);
            var values = PositiveEqualsValues(id.Value);
            foreach (var source in sources)
            {
                if (!IsContentIdSource(source) || route == ContentRoute.Movie && IsTvdbSource(source))
                {
                    unsupportedSource = true;
                    continue;
                }

                foreach (var value in values)
                {
                    if (IsValidContentId(source, value))
                    {
                        alternatives.Add(new ContentId(source, value));
                    }
                    else
                    {
                        invalid = true;
                    }
                }
            }
        }

        if (alternatives.Count > 0)
        {
            filters.Add(new ContentIdFilter(excluded, alternatives));
        }

        if ((alternatives.Count > 0 || excluded) && (invalid || alternatives.Count == 0 && !unsupportedSource))
        {
            warnings.Add("TMDb ignored an invalid exact content identifier.");
        }

        if (excluded && (invalid || unsupportedSource) ||
            alternatives.Count == 0 && (!excluded || invalid || unsupportedSource))
        {
            knownEmpty = true;
        }
    }

    private void AddElementFilters(
        MetadataCriteria criteria,
        List<ElementFilter> filters,
        ref bool knownEmpty)
    {
        foreach (var group in criteria.ContentRatings.Some)
        {
            AddElementFilter(
                ElementFamily.ContentRating,
                CompileContentRatings(group),
                excluded: false,
                filters,
                ref knownEmpty);
        }

        foreach (var exclusion in criteria.ContentRatings.None)
        {
            AddElementFilter(
                ElementFamily.ContentRating,
                CompileContentRatings([exclusion]),
                excluded: true,
                filters,
                ref knownEmpty);
        }

        foreach (var group in criteria.AlternateTexts.Some)
        {
            AddElementFilter(
                ElementFamily.AlternateText,
                CompileAlternateTexts(group),
                excluded: false,
                filters,
                ref knownEmpty);
        }

        foreach (var exclusion in criteria.AlternateTexts.None)
        {
            AddElementFilter(
                ElementFamily.AlternateText,
                CompileAlternateTexts([exclusion]),
                excluded: true,
                filters,
                ref knownEmpty);
        }

        foreach (var group in criteria.Previews.Some)
        {
            AddElementFilter(
                ElementFamily.Preview,
                CompilePreviews(group),
                excluded: false,
                filters,
                ref knownEmpty);
        }

        foreach (var exclusion in criteria.Previews.None)
        {
            AddElementFilter(
                ElementFamily.Preview,
                CompilePreviews([exclusion]),
                excluded: true,
                filters,
                ref knownEmpty);
        }

        foreach (var group in criteria.Images.Some)
        {
            AddElementFilter(
                ElementFamily.Image,
                CompileImages(group),
                excluded: false,
                filters,
                ref knownEmpty);
        }

        foreach (var exclusion in criteria.Images.None)
        {
            AddElementFilter(
                ElementFamily.Image,
                CompileImages([exclusion]),
                excluded: true,
                filters,
                ref knownEmpty);
        }

        foreach (var group in criteria.References.Some)
        {
            AddElementFilter(
                ElementFamily.Reference,
                CompileReferences(group),
                excluded: false,
                filters,
                ref knownEmpty);
        }

        foreach (var exclusion in criteria.References.None)
        {
            AddElementFilter(
                ElementFamily.Reference,
                CompileReferences([exclusion]),
                excluded: true,
                filters,
                ref knownEmpty);
        }
    }

    private void AddElementFilter(
        ElementFamily family,
        IReadOnlyList<IReadOnlyList<ElementPredicate>?> compiled,
        bool excluded,
        List<ElementFilter> filters,
        ref bool knownEmpty)
    {
        var usable = new List<IReadOnlyList<ElementPredicate>>();
        var incomplete = false;
        var impossible = false;
        foreach (var alternative in compiled)
        {
            if (alternative is null)
            {
                incomplete = true;
                continue;
            }

            var folded = FoldConstantPredicates(alternative, out var alternativeImpossible);
            if (alternativeImpossible)
            {
                impossible = true;
                continue;
            }

            usable.Add(folded);
        }

        if (usable.Count > 0)
        {
            filters.Add(new ElementFilter { Family = family, Excluded = excluded, Alternatives = usable });
            return;
        }

        if (incomplete || (!excluded && impossible))
        {
            knownEmpty = true;
        }
    }

    private IReadOnlyList<ElementPredicate> FoldConstantPredicates(
        IReadOnlyList<ElementPredicate> alternative,
        out bool impossible)
    {
        impossible = false;
        var remaining = new List<ElementPredicate>();
        foreach (var predicate in alternative)
        {
            if (TryGetConstantText(predicate.Field, out var constantText))
            {
                var matches = constantText.MatchesText(predicate.Operator, predicate.StringValues);
                if (predicate.Negate ? matches : !matches)
                {
                    impossible = true;
                    return [];
                }

                continue;
            }

            remaining.Add(predicate);
        }

        return remaining;
    }

    private bool TryGetConstantText(ElementField field, out string? constant)
    {
        switch (field)
        {
            case ElementField.PreviewSource:
            case ElementField.ImageSource:
                constant = Constants.ProviderType;
                return true;
            case ElementField.PreviewMimeType:
            case ElementField.ReferenceLanguage:
            case ElementField.TextLanguage:
                constant = null;
                return true;
            default:
                constant = null;
                return false;
        }
    }

    private List<IReadOnlyList<ElementPredicate>?> CompileContentRatings(
        IReadOnlyList<ContentRatingCriteria> alternatives)
    {
        var compiled = new List<IReadOnlyList<ElementPredicate>?>();
        foreach (var alternative in alternatives)
        {
            var predicates = new List<ElementPredicate>();
            compiled.Add(
                TryAddTextPredicates(alternative.Body, ElementField.RatingBody, predicates) &&
                TryAddTextPredicates(alternative.Rating, ElementField.RatingValue, predicates)
                    ? predicates
                    : null);
        }

        return compiled;
    }

    private List<IReadOnlyList<ElementPredicate>?> CompileAlternateTexts(
        IReadOnlyList<AlternateTextCriteria> alternatives)
    {
        var compiled = new List<IReadOnlyList<ElementPredicate>?>();
        foreach (var alternative in alternatives)
        {
            var predicates = new List<ElementPredicate>();
            compiled.Add(
                TryAddTextPredicates(alternative.Kind, ElementField.TextKind, predicates) &&
                TryAddTextPredicates(alternative.Language, ElementField.TextLanguage, predicates) &&
                TryAddTextPredicates(alternative.Text, ElementField.TextValue, predicates)
                    ? predicates
                    : null);
        }

        return compiled;
    }

    private List<IReadOnlyList<ElementPredicate>?> CompilePreviews(IReadOnlyList<PreviewMetadataCriteria> alternatives)
    {
        var compiled = new List<IReadOnlyList<ElementPredicate>?>();
        foreach (var alternative in alternatives)
        {
            var predicates = new List<ElementPredicate>();
            compiled.Add(
                TryAddTextPredicates(alternative.MimeType, ElementField.PreviewMimeType, predicates) &&
                TryAddTextPredicates(alternative.Url, ElementField.PreviewUrl, predicates) &&
                TryAddTextPredicates(alternative.Source, ElementField.PreviewSource, predicates) &&
                TryAddTextPredicates(alternative.Title, ElementField.PreviewTitle, predicates) &&
                TryAddDatePredicates(alternative.ReleaseDate, ElementField.PreviewReleaseDate, predicates) &&
                TryAddTextPredicates(alternative.Language, ElementField.PreviewLanguage, predicates)
                    ? predicates
                    : null);
        }

        return compiled;
    }

    private List<IReadOnlyList<ElementPredicate>?> CompileImages(IReadOnlyList<ImageCriteria> alternatives)
    {
        var compiled = new List<IReadOnlyList<ElementPredicate>?>();
        foreach (var alternative in alternatives)
        {
            var predicates = new List<ElementPredicate>();
            compiled.Add(
                TryAddTextPredicates(alternative.Type, ElementField.ImageType, predicates) &&
                TryAddTextPredicates(alternative.MimeType, ElementField.ImageMimeType, predicates) &&
                TryAddTextPredicates(alternative.Url, ElementField.ImageUrl, predicates) &&
                TryAddTextPredicates(alternative.Source, ElementField.ImageSource, predicates) &&
                TryAddTextPredicates(alternative.Language, ElementField.ImageLanguage, predicates) &&
                TryAddNumberPredicates(alternative.Width, ElementField.ImageWidth, predicates) &&
                TryAddNumberPredicates(alternative.Height, ElementField.ImageHeight, predicates)
                    ? predicates
                    : null);
        }

        return compiled;
    }

    private List<IReadOnlyList<ElementPredicate>?> CompileReferences(
        IReadOnlyList<ReferenceMetadataCriteria> alternatives)
    {
        var compiled = new List<IReadOnlyList<ElementPredicate>?>();
        foreach (var alternative in alternatives)
        {
            var predicates = new List<ElementPredicate>();
            compiled.Add(
                TryAddNumberPredicates(alternative.Ordinal, ElementField.ReferenceOrdinal, predicates) &&
                TryAddTextPredicates(alternative.Kind, ElementField.ReferenceKind, predicates) &&
                TryAddUriPredicates(alternative.Url, ElementField.ReferenceUrl, predicates) &&
                TryAddTextPredicates(alternative.DisplayName, ElementField.ReferenceDisplayName, predicates) &&
                TryAddTextPredicates(alternative.Language, ElementField.ReferenceLanguage, predicates)
                    ? predicates
                    : null);
        }

        return compiled;
    }

    private bool TryAddTextPredicates(
        CriteriaSet<PropertyCriterion<string>> criteria,
        ElementField field,
        List<ElementPredicate> predicates)
    {
        return TryAddPredicates(
            criteria,
            field,
            predicates,
            TextValues,
            IsTextOperator,
            (predicate, values) => predicate with { StringValues = values });
    }

    private bool TryAddUriPredicates(
        CriteriaSet<PropertyCriterion<Uri>> criteria,
        ElementField field,
        List<ElementPredicate> predicates)
    {
        return TryAddPredicates(
            criteria,
            field,
            predicates,
            UriValues,
            IsTextOperator,
            (predicate, values) => predicate with { StringValues = values });
    }

    private bool TryAddDatePredicates(
        CriteriaSet<PropertyCriterion<DateTime>> criteria,
        ElementField field,
        List<ElementPredicate> predicates)
    {
        return TryAddPredicates(
            criteria,
            field,
            predicates,
            DateValues,
            IsElementOrderedOperator,
            (predicate, values) => predicate with { DateValues = values });
    }

    private bool TryAddNumberPredicates(
        CriteriaSet<PropertyCriterion<int>> criteria,
        ElementField field,
        List<ElementPredicate> predicates)
    {
        return TryAddPredicates(
            criteria,
            field,
            predicates,
            NumberValues,
            IsElementOrderedOperator,
            (predicate, values) => predicate with { NumberValues = values });
    }

    private bool TryAddPredicates<T, TValue>(
        CriteriaSet<PropertyCriterion<T>> criteria,
        ElementField field,
        List<ElementPredicate> predicates,
        Func<PropertyCriterion<T>, IReadOnlyList<TValue>> readValues,
        Func<CriteriaValueOperator, bool> isValidOperator,
        Func<ElementPredicate, IReadOnlyList<TValue>, ElementPredicate> assignValues)
    {
        foreach (var criterion in criteria.And)
        {
            var (op, negate) = criterion.Operator.NormalizeOperator(criterion.Negate);
            var values = readValues(criterion);
            if (!isValidOperator(op) ||
                values.Count == 0 &&
                op is not CriteriaValueOperator.IsNull
                    and not CriteriaValueOperator.IsNotNull)
            {
                return false;
            }

            predicates.Add(assignValues(
                new ElementPredicate
                {
                    Field = field,
                    Operator = op,
                    Negate = negate
                },
                values));
        }

        return true;
    }

    private bool IsElementOrderedOperator(CriteriaValueOperator op)
    {
        return op is CriteriaValueOperator.Equals
            or CriteriaValueOperator.In
            or CriteriaValueOperator.GreaterThan
            or CriteriaValueOperator.GreaterThanOrEqual
            or CriteriaValueOperator.LessThan
            or CriteriaValueOperator.LessThanOrEqual
            or CriteriaValueOperator.IsNull
            or CriteriaValueOperator.IsNotNull;
    }

    private IReadOnlyList<string> UriValues(PropertyCriterion<Uri> criterion)
    {
        var values = new List<Uri?> { criterion.Value };
        values.AddRange(criterion.Values ?? []);
        return [.. values.Where(value => value is not null)
            .Select(value => value!.ToString())
            .Distinct(StringComparer.OrdinalIgnoreCase)];
    }

    private IReadOnlyList<DateTime> DateValues(PropertyCriterion<DateTime> criterion)
    {
        var values = (criterion.Values ?? []).ToList();
        if (criterion.Value != default || values.Count == 0)
        {
            values.Insert(0, criterion.Value);
        }

        return [.. values.Distinct()];
    }

    private IReadOnlyList<int> NumberValues(PropertyCriterion<int> criterion)
    {
        var values = (criterion.Values ?? []).ToList();
        if (criterion.Value != 0 || values.Count == 0)
        {
            values.Insert(0, criterion.Value);
        }

        return [.. values.Distinct()];
    }

    private QueryStrategy SelectStrategy(
        ContentRoute route,
        string? ordinaryTerm,
        IReadOnlyList<TextFilter> textFilters,
        IReadOnlyList<TemporalFilter> temporalFilters,
        IReadOnlyList<DurationFilter> durationFilters,
        IReadOnlyList<GenreFilter> genreFilters,
        IReadOnlyList<SubjectFilter> subjectFilters,
        IReadOnlyList<ContentIdFilter> contentIdFilters,
        IReadOnlyList<ElementFilter> elementFilters)
    {
        if (route is not ContentRoute.Movie and not ContentRoute.TvShow)
        {
            return QueryStrategy.Empty;
        }

        if (ordinaryTerm is not null)
        {
            return route == ContentRoute.Movie ? QueryStrategy.MovieSearch : QueryStrategy.TvSearch;
        }

        if (contentIdFilters.Any(filter => !filter.Excluded))
        {
            return QueryStrategy.ContentIds;
        }

        var hasPositiveTitle = textFilters.Any(filter =>
            filter.Field is TextField.Title or TextField.OriginalTitle &&
            !filter.Negate &&
            filter.Values.Count > 0);
        var positiveSubjects = subjectFilters.Where(filter => !filter.Excluded).ToList();
        var personGroups = positiveSubjects.Count(filter => filter.Alternatives.Any(alternative =>
            CanSeedSubjectAlternative(alternative) &&
            alternative.Roles.Any(role => role is not SubjectRole.Company and not SubjectRole.Network)));
        if (route == ContentRoute.TvShow && personGroups > 0)
        {
            return QueryStrategy.TvPersonCredits;
        }

        var hasDiscoverAnchor = temporalFilters.Any(filter => !filter.Negate) ||
            durationFilters.Any(filter => !filter.Negate) ||
            genreFilters.Any(filter => !filter.Excluded) ||
            positiveSubjects.Count > 0 ||
            textFilters.Any(filter => filter.Field == TextField.Language && !filter.Negate) ||
            elementFilters.Any(filter => !filter.Excluded && filter.Family == ElementFamily.ContentRating);
        if (hasPositiveTitle && !hasDiscoverAnchor)
        {
            return route == ContentRoute.Movie ? QueryStrategy.MovieSearch : QueryStrategy.TvSearch;
        }

        return route == ContentRoute.Movie ? QueryStrategy.MovieDiscover : QueryStrategy.TvDiscover;
    }

    private PlanNode BuildPlanTree(
        ContentRoute route,
        QueryStrategy strategy,
        string? ordinaryTerm,
        IReadOnlyList<TextFilter> textFilters,
        IReadOnlyList<TemporalFilter> temporalFilters,
        IReadOnlyList<DurationFilter> durationFilters,
        IReadOnlyList<GenreFilter> genreFilters,
        IReadOnlyList<SubjectResolutionBinding> subjectBindings,
        IReadOnlyList<ContentIdFilter> contentIdFilters,
        IReadOnlyList<ElementFilter> elementFilters,
        int requestedPage,
        int? maximumResults,
        string fingerprint)
    {
        var nextId = 1;
        if (strategy == QueryStrategy.Empty)
        {
            return new UnionPlanNode
            {
                Id = nextId,
                Precondition = PlanPrecondition.None,
                Contributes = "known empty result",
                Children = []
            };
        }

        var subjectFilters = subjectBindings.Select(binding => binding.Filter).ToList();
        var subjectIndexes = subjectFilters.Select((filter, index) => (filter, index))
            .Where(item => !item.filter.Excluded)
            .Select(item => item.index)
            .ToList();
        var resolve = subjectIndexes.Count > 0 ||
            genreFilters.Count > 0 ||
            elementFilters.Any(filter => filter.Family == ElementFamily.Image)
            ? new ResolvePlanNode
            {
                Id = nextId++,
                Precondition = PlanPrecondition.None,
                Contributes = "resolved ids and residual context",
                Route = route,
                SubjectBindings = subjectBindings,
                GenreFilters = genreFilters,
                ResolveImageBase = elementFilters.Any(filter => filter.Family == ElementFamily.Image)
            }
            : null;
        var pushed = PushedFilters(
            strategy,
            route,
            textFilters,
            temporalFilters,
            durationFilters,
            contentIdFilters,
            elementFilters);
        var personSubjectIndexes = subjectFilters.Select((filter, index) => (filter, index))
            .Where(item => !item.filter.Excluded &&
                item.filter.Alternatives.Any(alternative =>
                    alternative.Roles.Any(role => role is not SubjectRole.Company and not SubjectRole.Network)))
            .Select(item => item.index)
            .ToList();

        PlanNode primary;
        if (strategy == QueryStrategy.TvPersonCredits)
        {
            var personSources = new List<PlanNode>();
            foreach (var index in personSubjectIndexes)
            {
                var roles = subjectFilters[index].Alternatives.SelectMany(alternative => alternative.Roles)
                    .Distinct().ToList();
                var personRoles = roles.Where(role => role is not SubjectRole.Company
                    and not SubjectRole.Network).ToList();
                var organizationRoles = roles.Where(role => role is SubjectRole.Company
                    or SubjectRole.Network).ToList();
                var credits = new FetchPlanNode
                {
                    Id = nextId++,
                    Precondition = ApplicablePrecondition(resolve),
                    Contributes = "TV credit candidates for one required relationship group",
                    Route = route,
                    AnchorKind = QueryAnchorKind.PersonCredits,
                    SubjectBindings = [subjectBindings[index]],
                    SubjectRoles = personRoles,
                    PushedFilters = IsDirectSingletonSubjectFilter(subjectFilters[index], personRoles)
                        ? [$"relationship[{index}] /person/{{id}}/tv_credits"]
                        : [],
                    Resolve = resolve,
                    IsIntersectionBranch = true,
                    TextFilters = [],
                    PublishDateFilters = [],
                    DurationFilters = [],
                    GenreFilters = [],
                    ContentIdFilters = [],
                    ElementFilters = [],
                    RequestedPage = requestedPage,
                    MaximumResults = maximumResults,
                    Fingerprint = fingerprint
                };
                if (organizationRoles.Count == 0)
                {
                    personSources.Add(credits);
                    continue;
                }

                var alternatives = new List<PlanNode> { credits };
                foreach (var organizationRole in organizationRoles)
                {
                    var organizationFetch = new FetchPlanNode
                    {
                        Id = nextId++,
                        Precondition = resolve is null
                            ? PlanPrecondition.None
                            : new PlanPrecondition
                            {
                                Kind = PlanPreconditionKind.NodeNonEmpty,
                                NodeId = resolve.Id
                            },
                        Contributes = "TV Discover candidates for one organization role alternative",
                        Route = route,
                        AnchorKind = QueryAnchorKind.Discover,
                        SubjectBindings = [subjectBindings[index]],
                        SubjectRoles = [organizationRole],
                        DiscoverParameters = BuildDiscoverParameters(
                            route,
                            [subjectBindings[index]],
                            [new SubjectRoleSelection(subjectBindings[index].Key, [organizationRole])],
                            genreFilters),
                        PushedFilters = [],
                        Resolve = resolve,
                        RequiresLogicalStream = true,
                        BranchScopedScan = true,
                        TextFilters = DiscoverTextFilters(textFilters),
                        PublishDateFilters = DiscoverPublishDateFilters(temporalFilters),
                        DurationFilters = DiscoverDurationFilters(durationFilters),
                        GenreFilters = genreFilters,
                        ContentIdFilters = [],
                        ElementFilters = DiscoverElementFilters(route, elementFilters),
                        RequestedPage = requestedPage,
                        MaximumResults = maximumResults,
                        Fingerprint = fingerprint
                    };
                    alternatives.Add(WrapResolvedIdExpansion(organizationFetch, resolve, ref nextId));
                }

                personSources.Add(new UnionPlanNode
                {
                    Id = nextId++,
                    Precondition = resolve is null
                        ? PlanPrecondition.None
                        : new PlanPrecondition
                        {
                            Kind = PlanPreconditionKind.NodeApplicable,
                            NodeId = resolve.Id
                        },
                    Contributes = "TV person or organization relationship alternatives",
                    Children = alternatives
                });
            }

            primary = personSources.Count > 1 && personSources.All(source => source is FetchPlanNode)
                ? new IntersectPlanNode
                {
                    Id = nextId++,
                    Precondition = resolve is null
                        ? PlanPrecondition.None
                        : new PlanPrecondition
                        {
                            Kind = PlanPreconditionKind.NodeApplicable,
                            NodeId = resolve.Id
                        },
                    Contributes = "candidates present in every required person relationship source",
                    Children = personSources
                }
                : personSources[0];
        }
        else
        {
            var anchorKind = AnchorKind(strategy);
            if (anchorKind == QueryAnchorKind.Discover)
            {
                primary = BuildDiscoverPlan(
                    route,
                    subjectIndexes,
                    subjectBindings,
                    resolve,
                    pushed,
                    textFilters,
                    temporalFilters,
                    durationFilters,
                    genreFilters,
                    contentIdFilters,
                    elementFilters,
                    requestedPage,
                    maximumResults,
                    fingerprint,
                    ref nextId);
            }
            else
            {
                primary = new FetchPlanNode
                {
                    Id = nextId++,
                    Precondition = ApplicablePrecondition(resolve),
                    Contributes = "candidate rows",
                    Route = route,
                    AnchorKind = anchorKind,
                    SubjectBindings = [.. subjectIndexes.Select(index => subjectBindings[index])],
                    PushedFilters = pushed,
                    OrdinarySearchTerm = ordinaryTerm,
                    Resolve = resolve,
                    TextFilters = anchorKind == QueryAnchorKind.Search
                        ? SearchTextFilters(textFilters)
                        : [],
                    PublishDateFilters = anchorKind == QueryAnchorKind.Search
                        ? SearchPublishDateFilters(temporalFilters)
                        : [],
                    DurationFilters = [],
                    GenreFilters = [],
                    ContentIdFilters = anchorKind == QueryAnchorKind.ContentIds ? contentIdFilters : [],
                    ElementFilters = anchorKind == QueryAnchorKind.ContentIds ? elementFilters : [],
                    RequestedPage = requestedPage,
                    MaximumResults = maximumResults,
                    Fingerprint = fingerprint
                };
            }
        }

        if (resolve is not null && personSubjectIndexes.Count > 0)
        {
            var fallbackKind = textFilters.Any(filter => !filter.Negate &&
                filter.Field is TextField.Title or TextField.OriginalTitle)
                ? QueryAnchorKind.Search
                : QueryAnchorKind.Discover;
            var fallback = new FetchPlanNode
            {
                Id = nextId++,
                Precondition = new PlanPrecondition
                {
                    Kind = PlanPreconditionKind.NodeFailed,
                    NodeId = resolve.Id
                },
                Contributes = "fallback candidates when relationship resolution fails",
                Route = route,
                AnchorKind = fallbackKind,
                SubjectBindings = [],
                DiscoverParameters = fallbackKind == QueryAnchorKind.Discover
                    ? BuildDiscoverParameters(route, [], [], genreFilters)
                    : [],
                PushedFilters = [.. pushed.Where(filter => !filter.StartsWith("subject", StringComparison.Ordinal))],
                OrdinarySearchTerm = ordinaryTerm,
                IgnoreSubjectFilters = true,
                TextFilters = fallbackKind == QueryAnchorKind.Discover
                    ? DiscoverTextFilters(textFilters)
                    : SearchTextFilters(textFilters),
                PublishDateFilters = fallbackKind == QueryAnchorKind.Discover
                    ? DiscoverPublishDateFilters(temporalFilters)
                    : SearchPublishDateFilters(temporalFilters),
                DurationFilters = fallbackKind == QueryAnchorKind.Discover
                    ? DiscoverDurationFilters(durationFilters)
                    : [],
                GenreFilters = fallbackKind == QueryAnchorKind.Discover ? genreFilters : [],
                ContentIdFilters = [],
                ElementFilters = fallbackKind == QueryAnchorKind.Discover
                    ? DiscoverElementFilters(route, elementFilters)
                    : [],
                RequiresLogicalStream = fallbackKind == QueryAnchorKind.Discover,
                RequestedPage = requestedPage,
                MaximumResults = maximumResults,
                Fingerprint = fingerprint
            };
            primary = new CoalescePlanNode
            {
                Id = nextId++,
                Precondition = PlanPrecondition.None,
                Contributes = "first applicable anchor",
                Children = [primary, fallback]
            };
        }

        return new FilterPlanNode
        {
            Id = nextId,
            Precondition = PlanPrecondition.None,
            Contributes = "post-residual survivors",
            Route = route,
            Child = primary,
            ResidualPredicates = ResidualPredicates(
                route,
                strategy,
                textFilters,
                temporalFilters,
                durationFilters,
                genreFilters,
                subjectFilters,
                contentIdFilters,
                elementFilters),
            TextFilters = textFilters,
            PublishDateFilters = temporalFilters,
            DurationFilters = durationFilters,
            GenreFilters = genreFilters,
            SubjectBindings = subjectBindings,
            ContentIdFilters = contentIdFilters,
            ElementFilters = elementFilters,
            RequestedPage = requestedPage,
            MaximumResults = maximumResults
        };
    }

    private PlanNode BuildDiscoverPlan(
        ContentRoute route,
        IReadOnlyList<int> subjectIndexes,
        IReadOnlyList<SubjectResolutionBinding> subjectBindings,
        ResolvePlanNode? resolve,
        IReadOnlyList<string> pushedFilters,
        IReadOnlyList<TextFilter> textFilters,
        IReadOnlyList<TemporalFilter> temporalFilters,
        IReadOnlyList<DurationFilter> durationFilters,
        IReadOnlyList<GenreFilter> genreFilters,
        IReadOnlyList<ContentIdFilter> contentIdFilters,
        IReadOnlyList<ElementFilter> elementFilters,
        int requestedPage,
        int? maximumResults,
        string fingerprint,
        ref int nextId)
    {
        var subjectFilters = subjectBindings.Select(binding => binding.Filter).ToList();
        List<List<SubjectRoleSelection>> combinations = [[]];
        foreach (var index in subjectIndexes)
        {
            var roles = subjectFilters[index].Alternatives.SelectMany(alternative => alternative.Roles)
                .Where(role => CanDiscoverRole(route, role))
                .Distinct()
                .ToList();
            if (roles.Count <= 1)
            {
                continue;
            }

            combinations = [.. combinations.SelectMany(combination => roles.Select(role =>
                combination.Append(new SubjectRoleSelection(subjectBindings[index].Key, [role])).ToList()))];
        }

        var branches = new List<PlanNode>(combinations.Count);
        var guaranteedRelationships = GuaranteedDiscoverRelationships(route, subjectIndexes, subjectFilters);
        foreach (var selections in combinations)
        {
            var fetch = new FetchPlanNode
            {
                Id = nextId++,
                Precondition = ApplicablePrecondition(resolve),
                Contributes = "role-specific Discover candidate rows",
                Route = route,
                AnchorKind = QueryAnchorKind.Discover,
                SubjectBindings = [.. subjectIndexes.Select(index => subjectBindings[index])],
                SubjectRoleSelections = selections,
                DiscoverParameters = BuildDiscoverParameters(
                    route,
                    [.. subjectIndexes.Select(index => subjectBindings[index])],
                    selections,
                    genreFilters),
                PushedFilters =
                [
                    .. pushedFilters,
                    .. guaranteedRelationships.Select(item =>
                            $"relationship[{item.Index}] {item.Parameter}"),
                ],
                Resolve = resolve,
                RequiresLogicalStream = subjectIndexes.Count > 0 ||
                    combinations.Count > 1 ||
                    HasDiscoverResidual(
                        route,
                        textFilters,
                        temporalFilters,
                        durationFilters,
                        contentIdFilters,
                        elementFilters),
                BranchScopedScan = combinations.Count > 1,
                TextFilters = DiscoverTextFilters(textFilters),
                PublishDateFilters = DiscoverPublishDateFilters(temporalFilters),
                DurationFilters = DiscoverDurationFilters(durationFilters),
                GenreFilters = genreFilters,
                ContentIdFilters = [],
                ElementFilters = DiscoverElementFilters(route, elementFilters),
                RequestedPage = requestedPage,
                MaximumResults = maximumResults,
                Fingerprint = fingerprint
            };
            branches.Add(WrapResolvedIdExpansion(fetch, resolve, ref nextId));
        }

        if (branches.Count == 1)
        {
            return branches[0];
        }

        return new UnionPlanNode
        {
            Id = nextId++,
            Precondition = ApplicablePrecondition(resolve),
            Contributes = "candidates matching any role-specific relationship branch",
            Children = branches
        };
    }

    private PlanNode WrapResolvedIdExpansion(FetchPlanNode fetch, ResolvePlanNode? resolve, ref int nextId)
    {
        if (fetch.SubjectBindings.Count == 0)
        {
            return fetch;
        }

        return new UnionPlanNode
        {
            Id = nextId++,
            Precondition = ApplicablePrecondition(resolve),
            Contributes = "split resolved-id request templates",
            Children = [fetch],
            ExpandsResolvedSubjectIds = true
        };
    }

    private IReadOnlyList<DiscoverParameter> BuildDiscoverParameters(
        ContentRoute route,
        IReadOnlyList<SubjectResolutionBinding> subjectBindings,
        IReadOnlyList<SubjectRoleSelection> selections,
        IReadOnlyList<GenreFilter> genreFilters)
    {
        var subjectClauses = new List<(string Parameter, string BindingKey)>();
        foreach (var binding in subjectBindings)
        {
            var selectedRoles = selections.FirstOrDefault(selection =>
                selection.SubjectBindingKey == binding.Key)?.Roles
                ?? [.. binding.Filter.Alternatives.SelectMany(alternative => alternative.Roles).Distinct()];
            foreach (var parameter in selectedRoles.Select(role => DiscoverParameter(route, role, binding.Filter))
                .Where(parameter => parameter is not null).Distinct(StringComparer.Ordinal))
            {
                subjectClauses.Add((parameter!, binding.Key));
            }
        }

        var parameters = subjectClauses.GroupBy(clause => clause.Parameter, StringComparer.Ordinal)
            .Select(group => new DiscoverParameter
            {
                Name = group.Key,
                Combination = group.Select(clause => clause.BindingKey).Distinct(StringComparer.Ordinal).Count() > 1
                    ? DiscoverCombination.And
                    : DiscoverCombination.Or,
                SubjectBindingKeys = [.. group.Select(clause => clause.BindingKey).Distinct(StringComparer.Ordinal)]
            })
            .ToList();
        var positiveGenres = genreFilters.Where(filter => !filter.Excluded).ToList();
        if (positiveGenres.Count > 0)
        {
            parameters.Add(new DiscoverParameter
            {
                Name = "with_genres",
                Combination = positiveGenres.Count > 1
                    ? DiscoverCombination.And
                    : DiscoverCombination.Or,
                GenreFilters = positiveGenres
            });
        }

        var excludedGenres = genreFilters.Where(filter => filter.Excluded).ToList();
        if (excludedGenres.Count > 0)
        {
            parameters.Add(new DiscoverParameter
            {
                Name = "without_genres",
                Combination = DiscoverCombination.Or,
                GenreFilters = excludedGenres
            });
        }

        return parameters;
    }

    private IReadOnlyList<TextFilter> SearchTextFilters(IReadOnlyList<TextFilter> filters)
    {
        var seed = filters.FirstOrDefault(filter => filter.Field is TextField.Title
            or TextField.OriginalTitle &&
            !filter.Negate &&
            filter.Values.Count > 0);
        return seed is null ? [] : [seed];
    }

    private IReadOnlyList<TemporalFilter> SearchPublishDateFilters(IReadOnlyList<TemporalFilter> filters)
    {
        var year = filters.FirstOrDefault(filter => !filter.Negate &&
            filter.Operator == CriteriaValueOperator.Equals &&
            filter.Values.Count == 1 &&
            filter.Values[0].Precision == TemporalPrecision.Year);
        return year is null ? [] : [year];
    }

    private IReadOnlyList<TextFilter> DiscoverTextFilters(IReadOnlyList<TextFilter> filters)
    {
        return [.. filters.Where(CanApplyOriginalLanguage).Take(1)];
    }

    private IReadOnlyList<TemporalFilter> DiscoverPublishDateFilters(IReadOnlyList<TemporalFilter> filters)
    {
        return [.. filters.Where(CanApplyPublishDate)];
    }

    private IReadOnlyList<DurationFilter> DiscoverDurationFilters(IReadOnlyList<DurationFilter> filters)
    {
        return [.. filters.Where(CanApplyRuntime)];
    }

    private IReadOnlyList<ElementFilter> DiscoverElementFilters(
        ContentRoute route,
        IReadOnlyList<ElementFilter> filters)
    {
        return [.. filters.Where(filter => CanApplyCertification(route, filter)).Take(1)];
    }

    private bool HasDiscoverResidual(
        ContentRoute route,
        IReadOnlyList<TextFilter> textFilters,
        IReadOnlyList<TemporalFilter> publishDateFilters,
        IReadOnlyList<DurationFilter> durationFilters,
        IReadOnlyList<ContentIdFilter> contentIdFilters,
        IReadOnlyList<ElementFilter> elementFilters)
    {
        var appliedText = DiscoverTextFilters(textFilters);
        var appliedPublishDates = DiscoverPublishDateFilters(publishDateFilters);
        var appliedDurations = DiscoverDurationFilters(durationFilters);
        var appliedElements = DiscoverElementFilters(route, elementFilters);
        return textFilters.Any(filter => !appliedText.Contains(filter)) ||
            publishDateFilters.Any(filter => !appliedPublishDates.Contains(filter)) ||
            durationFilters.Any(filter => !appliedDurations.Contains(filter)) ||
            contentIdFilters.Count > 0 ||
            elementFilters.Any(filter => !appliedElements.Contains(filter));
    }

    private string? DiscoverParameter(ContentRoute route, SubjectRole role, SubjectFilter filter)
    {
        return role switch
        {
            SubjectRole.Company => "with_companies",
            SubjectRole.Network when route == ContentRoute.TvShow =>
                filter.Alternatives.SelectMany(alternative => alternative.IdentityClauses)
                    .SelectMany(clause => clause)
                    .All(IsDirectSubjectIdentity)
                    ? "with_networks"
                    : "with_companies",
            SubjectRole.Cast when route == ContentRoute.Movie => "with_cast",
            SubjectRole.Crew or SubjectRole.Director or SubjectRole.Writer
                when route == ContentRoute.Movie => "with_crew",
            SubjectRole.Person when route == ContentRoute.Movie => "with_people",
            _ => null
        };
    }

    private PlanPrecondition ApplicablePrecondition(ResolvePlanNode? resolve)
    {
        return resolve is null
            ? PlanPrecondition.None
            : new PlanPrecondition
            {
                Kind = PlanPreconditionKind.NodeApplicable,
                NodeId = resolve.Id
            };
    }

    private bool CanDiscoverRole(ContentRoute route, SubjectRole role)
    {
        return role switch
        {
            SubjectRole.Company => true,
            SubjectRole.Network => route == ContentRoute.TvShow,
            SubjectRole.Person or SubjectRole.Cast or SubjectRole.Crew
                or SubjectRole.Director or SubjectRole.Writer => route == ContentRoute.Movie,
            _ => false
        };
    }

    private QueryAnchorKind AnchorKind(QueryStrategy strategy)
    {
        return strategy switch
        {
            QueryStrategy.MovieSearch or QueryStrategy.TvSearch => QueryAnchorKind.Search,
            QueryStrategy.MovieDiscover or QueryStrategy.TvDiscover => QueryAnchorKind.Discover,
            QueryStrategy.TvPersonCredits => QueryAnchorKind.PersonCredits,
            QueryStrategy.ContentIds => QueryAnchorKind.ContentIds,
            _ => throw new ArgumentOutOfRangeException(nameof(strategy))
        };
    }

    private IReadOnlyList<string> PushedFilters(
        QueryStrategy strategy,
        ContentRoute route,
        IReadOnlyList<TextFilter> textFilters,
        IReadOnlyList<TemporalFilter> temporalFilters,
        IReadOnlyList<DurationFilter> durationFilters,
        IReadOnlyList<ContentIdFilter> contentIdFilters,
        IReadOnlyList<ElementFilter> elementFilters)
    {
        if (strategy == QueryStrategy.ContentIds)
        {
            return [.. contentIdFilters.Where(filter => !filter.Excluded).Select(_ => "content identity")];
        }

        if (strategy is QueryStrategy.MovieSearch or QueryStrategy.TvSearch)
        {
            return ["title query"];
        }

        if (strategy == QueryStrategy.TvPersonCredits)
        {
            return [];
        }

        return
        [
            .. textFilters.Select((filter, index) => (filter, index))
                        .Where(item => CanApplyOriginalLanguage(item.filter))
                        .Select(item => $"text[{item.index}] original language")
,
            .. temporalFilters.Select((filter, index) => (filter, index))
                    .Where(item => CanApplyPublishDate(item.filter))
                    .Select(item => $"publishDate[{item.index}]"),
            .. durationFilters.Select((filter, index) => (filter, index))
                    .Where(item => CanApplyRuntime(item.filter))
                    .Select(item => $"duration[{item.index}] runtime"),
            .. elementFilters.Select((filter, index) => (filter, index))
                    .Where(item => CanApplyCertification(route, item.filter))
                    .Select(item => $"element[{item.index}] certification"),
        ];
    }

    private IReadOnlyList<string> ResidualPredicates(
        ContentRoute route,
        QueryStrategy strategy,
        IReadOnlyList<TextFilter> textFilters,
        IReadOnlyList<TemporalFilter> temporalFilters,
        IReadOnlyList<DurationFilter> durationFilters,
        IReadOnlyList<GenreFilter> genreFilters,
        IReadOnlyList<SubjectFilter> subjectFilters,
        IReadOnlyList<ContentIdFilter> contentIdFilters,
        IReadOnlyList<ElementFilter> elementFilters)
    {
        var discover = strategy is QueryStrategy.MovieDiscover or QueryStrategy.TvDiscover;
        var guaranteedRelationships = GuaranteedRelationshipIndexes(route, strategy, subjectFilters);
        return [.. textFilters.Select((filter, index) => (filter, index))
            .Where(item => !discover || !CanApplyOriginalLanguage(item.filter))
            .Select(item => $"text[{item.index}]:{item.filter.Field}")
            .Concat(temporalFilters.Select((filter, index) => (filter, index))
                .Where(item => !discover || !CanApplyPublishDate(item.filter))
                .Select(item => $"publishDate[{item.index}]"))
            .Concat(durationFilters.Select((filter, index) => (filter, index))
                .Where(item => !discover || !CanApplyRuntime(item.filter))
                .Select(item => $"duration[{item.index}]"))
            .Concat(genreFilters.Select((_, index) => $"genre[{index}]"))
            .Concat(subjectFilters.Select((_, index) => index)
                .Where(index => !guaranteedRelationships.Contains(index))
                .Select(index => $"relationship[{index}]"))
            .Concat(contentIdFilters.Where(filter => filter.Excluded).Select(_ => "contentIdentity"))
            .Concat(elementFilters.Select((filter, index) => (filter, index))
                .Where(item => !discover || !CanApplyCertification(route, item.filter))
                .Select(item => $"element[{item.index}]:{item.filter.Family}"))
            .Distinct(StringComparer.Ordinal)];
    }

    private ISet<int> GuaranteedRelationshipIndexes(
        ContentRoute route,
        QueryStrategy strategy,
        IReadOnlyList<SubjectFilter> subjectFilters)
    {
        var hasPersonAnchor = subjectFilters.Any(filter => !filter.Excluded &&
            filter.Alternatives.Any(alternative => alternative.Roles.Any(role =>
                role is not SubjectRole.Company and not SubjectRole.Network)));
        if (hasPersonAnchor && SubjectResolutionCanFail(subjectFilters))
        {
            return new HashSet<int>();
        }

        if (strategy == QueryStrategy.TvPersonCredits)
        {
            return subjectFilters.Select((filter, index) => (filter, index))
                .Where(item => IsDirectSingletonSubjectFilter(
                    item.filter,
                    [.. item.filter.Alternatives.SelectMany(alternative => alternative.Roles)
                        .Where(role => role is not SubjectRole.Company and not SubjectRole.Network)
                        .Distinct()]))
                .Select(item => item.index)
                .ToHashSet();
        }

        if (strategy is QueryStrategy.MovieDiscover or QueryStrategy.TvDiscover)
        {
            return GuaranteedDiscoverRelationships(
                route,
                [.. subjectFilters.Select((filter, index) => (filter, index))
                    .Where(item => !item.filter.Excluded)
                    .Select(item => item.index)],
                subjectFilters)
                .Select(item => item.Index)
                .ToHashSet();
        }

        return new HashSet<int>();
    }

    private IReadOnlyList<(int Index, string Parameter)> GuaranteedDiscoverRelationships(
        ContentRoute route,
        IReadOnlyList<int> subjectIndexes,
        IReadOnlyList<SubjectFilter> subjectFilters)
    {
        var guaranteed = new List<(int Index, string Parameter)>();
        var parameters = route == ContentRoute.Movie
            ? new[]
            {
                (Role: SubjectRole.Person, Parameter: "with_people", SingleValue: false),
                (Role: SubjectRole.Company, Parameter: "with_companies", SingleValue: false)
            }
            :
            [
                (Role: SubjectRole.Company, Parameter: "with_companies", SingleValue: false),
                (Role: SubjectRole.Network, Parameter: "with_networks", SingleValue: true)
            ];
        foreach (var descriptor in parameters)
        {
            var roleIndexes = subjectIndexes.Where(index => subjectFilters[index].Alternatives
                .SelectMany(alternative => alternative.Roles)
                .Contains(descriptor.Role))
                .ToList();
            if (roleIndexes.Count == 0 ||
                (descriptor.SingleValue && roleIndexes.Count != 1) ||
                roleIndexes.Any(index => !IsDirectSingletonSubjectFilter(subjectFilters[index], [descriptor.Role])))
            {
                continue;
            }

            guaranteed.AddRange(roleIndexes.Select(index => (index, descriptor.Parameter)));
        }

        return guaranteed;
    }

    private bool IsDirectSingletonSubjectFilter(SubjectFilter filter, IReadOnlyList<SubjectRole> roles)
    {
        if (filter.Excluded || filter.RequiresLocalVerification || roles.Count != 1 || filter.Alternatives.Count != 1)
        {
            return false;
        }

        var alternative = filter.Alternatives[0];
        if (alternative.Roles.Count != 1 ||
            alternative.Roles[0] != roles[0] ||
            alternative.IdentityClauses.Count != 1 ||
            alternative.IdentityClauses[0].Count != 1)
        {
            return false;
        }

        var identity = alternative.IdentityClauses[0][0];
        return IsDirectSubjectIdentity(identity);
    }

    private bool SubjectResolutionCanFail(IReadOnlyList<SubjectFilter> subjectFilters)
    {
        return subjectFilters.SelectMany(filter => filter.Alternatives)
            .Any(alternative => alternative.Roles.Count > 0 && alternative.IdentityClauses
                .SelectMany(clause => clause)
                .Any(identity => !IsDirectSubjectIdentity(identity)));
    }

    private bool IsDirectSubjectIdentity(SubjectIdentity identity)
    {
        return string.IsNullOrWhiteSpace(identity.Name) &&
            !identity.Negate &&
            identity.Operator is CriteriaValueOperator.Equals or CriteriaValueOperator.In &&
            Constants.ProviderType.Equals(identity.ExternalIdSource, StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(identity.ExternalId, NumberStyles.None, CultureInfo.InvariantCulture, out var id) &&
            id > 0;
    }

    private bool CanSeedSubjectAlternative(SubjectAlternative alternative)
    {
        return alternative.Criteria is null ||
        alternative.Criteria.Subject.And.All(CandidateFilterExtensions.IsPositiveSearchableText) &&
        alternative.Criteria.ExternalIds.SomeFlat.All(externalId =>
            externalId.Source.And.All(CandidateFilterExtensions.IsPositiveEquality) &&
            externalId.Value.And.All(CandidateFilterExtensions.IsPositiveEquality));
    }

    private bool TryMapRoles(
        RelationshipCriteria relationship,
        ContentRoute route,
        out IReadOnlyList<SubjectRole> roles,
        out bool incomplete,
        out bool cannotCompile)
    {
        var roleCriteria = relationship.Kind.And.Concat(relationship.Type.And).ToList();
        if (roleCriteria.Count == 0)
        {
            roles = DefaultRoles(route);
            incomplete = false;
            cannotCompile = false;
            return true;
        }

        cannotCompile = roleCriteria.Any(criterion => !criterion.IsPositiveEquality());
        var hasUnmappedValues = false;
        var clauses = roleCriteria.Where(CandidateFilterExtensions.IsPositiveEquality)
            .Select(criterion =>
            {
                var values = TextValues(criterion);
                var mapped = values.Select(MapRole).OfType<SubjectRole>().Distinct().ToList();
                hasUnmappedValues |= values.Any(value => MapRole(value) is null);
                return mapped;
            })
            .ToList();
        if (clauses.Count == 0 || clauses.Any(clause => clause.Count == 0))
        {
            roles = [];
            incomplete = cannotCompile || hasUnmappedValues;
            return false;
        }

        var intersection = clauses.Skip(1).Aggregate(clauses[0].ToList(), IntersectRoles);
        roles = intersection;
        incomplete = cannotCompile || hasUnmappedValues;
        return true;
    }

    private IReadOnlyList<SubjectRole> DefaultRoles(ContentRoute route)
    {
        return route == ContentRoute.TvShow
            ? [SubjectRole.Person, SubjectRole.Company, SubjectRole.Network]
            : [SubjectRole.Person, SubjectRole.Company];
    }

    private List<SubjectRole> IntersectRoles(List<SubjectRole> left, IReadOnlyList<SubjectRole> right)
    {
        var intersections =
            from first in left
            from second in right
            let intersection = RoleCovers(first, second)
                ? second
                : RoleCovers(second, first)
                    ? first
                    : (SubjectRole?)null
            where intersection is not null
            select intersection.Value;
        return [.. intersections.Distinct()];
    }

    private bool RoleCovers(SubjectRole role, SubjectRole candidate)
    {
        return role switch
        {
            SubjectRole.Person => candidate is SubjectRole.Person
                or SubjectRole.Cast
                or SubjectRole.Crew
                or SubjectRole.Director
                or SubjectRole.Writer,
            SubjectRole.Crew => candidate is SubjectRole.Crew
                or SubjectRole.Director
                or SubjectRole.Writer,
            _ => role == candidate
        };
    }

    private SubjectRole? MapRole(string value)
    {
        return value.Trim().ToLowerInvariant() switch
        {
            "studio" => SubjectRole.Company,
            "person" => SubjectRole.Person,
            "cast" or "performer" => SubjectRole.Cast,
            "crew" => SubjectRole.Crew,
            "director" => SubjectRole.Director,
            "writer" => SubjectRole.Writer,
            "network" => SubjectRole.Network,
            _ => null
        };
    }

    private IReadOnlyList<SubjectIdentity> ExternalIdIdentities(IEnumerable<ExternalIdCriteria> externalIds)
    {
        var identities = new List<SubjectIdentity>();
        foreach (var externalId in externalIds)
        {
            var sources = PositiveEqualsValues(externalId.Source);
            var values = PositiveEqualsValues(externalId.Value);
            identities.AddRange(
                from source in sources
                from value in values
                select new SubjectIdentity(null, source, value));
        }

        return identities;
    }

    private IReadOnlyList<string> PositiveEqualsValues(CriteriaSet<PropertyCriterion<string>> criteria)
    {
        return [.. criteria.And
            .Where(CandidateFilterExtensions.IsPositiveEquality)
            .SelectMany(TextValues)
            .Distinct(StringComparer.OrdinalIgnoreCase)];
    }

    private IReadOnlyList<string> TextValues(PropertyCriterion<string> criterion)
    {
        var values = new List<string>();
        if (!string.IsNullOrWhiteSpace(criterion.Value))
        {
            values.Add(criterion.Value.Trim());
        }

        values.AddRange((criterion.Values ?? [])
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim()));
        return [.. values.Distinct(StringComparer.OrdinalIgnoreCase)];
    }

    private IReadOnlyList<TemporalValue> TemporalValues(PropertyCriterion<TemporalValue> criterion)
    {
        var values = new List<TemporalValue>();
        if (criterion.Value is not null)
        {
            values.Add(criterion.Value);
        }

        values.AddRange((criterion.Values ?? []).Where(value => value is not null));
        return values;
    }

    private IReadOnlyList<TimeSpan> DurationValues(PropertyCriterion<TimeSpan> criterion)
    {
        if (criterion.Operator.NormalizeOperator(criterion.Negate).Operator
            is CriteriaValueOperator.IsNull or CriteriaValueOperator.IsNotNull)
        {
            return [];
        }

        var values = (criterion.Values ?? []).ToList();
        if (criterion.Value != default || values.Count == 0)
        {
            values.Insert(0, criterion.Value);
        }

        return values;
    }

    private bool IsTextOperator(CriteriaValueOperator op)
    {
        return op is CriteriaValueOperator.Equals
            or CriteriaValueOperator.In
            or CriteriaValueOperator.ContainsString
            or CriteriaValueOperator.ContainsWord
            or CriteriaValueOperator.StartsWithString
            or CriteriaValueOperator.EndsWithString
            or CriteriaValueOperator.IsNull
            or CriteriaValueOperator.IsNotNull;
    }

    private bool IsRangeOperator(CriteriaValueOperator op)
    {
        return op is CriteriaValueOperator.Equals
            or CriteriaValueOperator.GreaterThan
            or CriteriaValueOperator.GreaterThanOrEqual
            or CriteriaValueOperator.LessThan
            or CriteriaValueOperator.LessThanOrEqual;
    }

    private bool IsContentIdSource(string source)
    {
        return source.Equals(Constants.ProviderType, StringComparison.OrdinalIgnoreCase) ||
        source.Equals("imdb", StringComparison.OrdinalIgnoreCase) ||
        source.Equals("imdb_id", StringComparison.OrdinalIgnoreCase) ||
        source.Equals("wikidata", StringComparison.OrdinalIgnoreCase) ||
        source.Equals("wikidata_id", StringComparison.OrdinalIgnoreCase) ||
        source.Equals("tvdb", StringComparison.OrdinalIgnoreCase) ||
        source.Equals("tvdb_id", StringComparison.OrdinalIgnoreCase);
    }

    private bool IsTvdbSource(string source)
    {
        return source.Equals("tvdb", StringComparison.OrdinalIgnoreCase) ||
        source.Equals("tvdb_id", StringComparison.OrdinalIgnoreCase);
    }

    private bool IsValidContentId(string source, string value)
    {
        if (source.Equals(Constants.ProviderType, StringComparison.OrdinalIgnoreCase))
        {
            return int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var id) && id > 0;
        }

        if (source.Equals("imdb", StringComparison.OrdinalIgnoreCase) ||
            source.Equals("imdb_id", StringComparison.OrdinalIgnoreCase))
        {
            return value.Length > 2 &&
                value.StartsWith("tt", StringComparison.OrdinalIgnoreCase) &&
                value.Skip(2).All(character => character is >= '0' and <= '9');
        }

        if (source.Equals("wikidata", StringComparison.OrdinalIgnoreCase) ||
            source.Equals("wikidata_id", StringComparison.OrdinalIgnoreCase))
        {
            return value.Length > 1 &&
                value[0] is 'q' or 'Q' &&
                value.Skip(1).All(character => character is >= '0' and <= '9');
        }

        return int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var tvdbId) && tvdbId > 0;
    }

    private string BuildFingerprint(
        ContentRoute route,
        string? ordinaryTerm,
        IEnumerable<TextFilter> text,
        IEnumerable<TemporalFilter> temporal,
        IEnumerable<DurationFilter> duration,
        IEnumerable<GenreFilter> genres,
        IEnumerable<SubjectFilter> subjects,
        IEnumerable<ContentIdFilter> ids,
        IEnumerable<ElementFilter> elements,
        int? maximumResults)
    {
        var value = new StringBuilder(route.ToString());
        AppendFingerprintValue(value, ordinaryTerm?.ToLowerInvariant());
        if (maximumResults is not null)
        {
            value.Append("|m:").Append(Math.Max(1, maximumResults.Value));
        }

        foreach (var filter in text)
        {
            value.Append("|t:").Append(filter.Field).Append(':').Append(filter.Operator).Append(':')
                .Append(filter.Negate);
            foreach (var item in filter.Values)
            {
                AppendFingerprintValue(value, item.ToLowerInvariant());
            }
        }

        foreach (var filter in temporal)
        {
            value.Append("|d:").Append(filter.Operator).Append(':').Append(filter.Negate).Append(':')
                .AppendJoin(
                    ',',
                    filter.Values.Select(item => string.Format(
                        CultureInfo.InvariantCulture,
                        "{0:O}/{1}",
                        item.Value,
                        item.Precision)));
        }

        foreach (var filter in duration)
        {
            value.Append("|r:").Append(filter.Operator).Append(':').Append(filter.Negate).Append(':')
                .AppendJoin(',', filter.Values.Select(item => item.Ticks.ToString(CultureInfo.InvariantCulture)));
        }

        foreach (var filter in genres)
        {
            value.Append("|g:").Append(filter.Excluded);
            foreach (var alternative in filter.Alternatives)
            {
                value.Append("|a");
                foreach (var predicate in alternative)
                {
                    value.Append('|').Append(predicate.Operator).Append(':').Append(predicate.Negate);
                    foreach (var item in predicate.Values)
                    {
                        AppendFingerprintValue(value, item.ToLowerInvariant());
                    }
                }
            }
        }

        foreach (var filter in subjects)
        {
            value.Append("|s:").Append(filter.Excluded);
            foreach (var alternative in filter.Alternatives)
            {
                value.Append(':').AppendJoin(',', alternative.Roles);
                AppendFingerprintValue(
                    value,
                    alternative.Criteria is null
                        ? null
                        : JsonSerializer.Serialize(alternative.Criteria).ToLowerInvariant());
                foreach (var clause in alternative.IdentityClauses)
                {
                    value.Append('{');
                    foreach (var identity in clause)
                    {
                        value.Append('[');
                        AppendFingerprintValue(value, identity.Name?.ToLowerInvariant());
                        AppendFingerprintValue(value, identity.ExternalIdSource?.ToLowerInvariant());
                        AppendFingerprintValue(value, identity.ExternalId?.ToLowerInvariant());
                        value.Append(':').Append(identity.Operator).Append(':').Append(identity.Negate);
                        value.Append(']');
                    }

                    value.Append('}');
                }
            }
        }

        foreach (var filter in ids)
        {
            value.Append("|i:").Append(filter.Excluded);
            foreach (var item in filter.Alternatives)
            {
                AppendFingerprintValue(value, item.Source.ToLowerInvariant());
                AppendFingerprintValue(value, item.Value.ToLowerInvariant());
            }
        }

        foreach (var filter in elements)
        {
            value.Append("|e:").Append(filter.Family).Append(':').Append(filter.Excluded);
            foreach (var alternative in filter.Alternatives)
            {
                value.Append("|a");
                foreach (var predicate in alternative)
                {
                    value.Append('|').Append(predicate.Field).Append(':').Append(predicate.Operator).Append(':')
                        .Append(predicate.Negate);
                    foreach (var item in predicate.StringValues)
                    {
                        AppendFingerprintValue(value, item.ToLowerInvariant());
                    }

                    foreach (var item in predicate.DateValues)
                    {
                        AppendFingerprintValue(value, item.ToString("O", CultureInfo.InvariantCulture));
                    }

                    foreach (var item in predicate.NumberValues)
                    {
                        AppendFingerprintValue(value, item.ToString(CultureInfo.InvariantCulture));
                    }
                }
            }
        }

        return value.ToString();
    }

    private void AppendFingerprintValue(StringBuilder builder, string? item)
    {
        var normalized = item ?? string.Empty;
        builder.Append('|').Append(normalized.Length).Append(':').Append(normalized);
    }

    private bool CanApplyOriginalLanguage(TextFilter filter)
    {
        return filter.Field == TextField.Language &&
        !filter.Negate &&
        filter.Operator == CriteriaValueOperator.Equals &&
        filter.Values.Count == 1;
    }

    private bool CanApplyPublishDate(TemporalFilter filter)
    {
        return !filter.Negate &&
        filter.Values.Count == 1 &&
        filter.Operator is CriteriaValueOperator.Equals
            or CriteriaValueOperator.GreaterThan
            or CriteriaValueOperator.GreaterThanOrEqual
            or CriteriaValueOperator.LessThan
            or CriteriaValueOperator.LessThanOrEqual;
    }

    private bool CanApplyRuntime(DurationFilter filter)
    {
        if (filter.Negate || filter.Values.Count != 1)
        {
            return false;
        }

        var totalMinutes = filter.Values[0].TotalMinutes;
        if (totalMinutes is < 0 or > int.MaxValue)
        {
            return false;
        }

        var floorMinutes = (int)Math.Floor(totalMinutes);
        var ceilingMinutes = (int)Math.Ceiling(totalMinutes);
        return filter.Operator switch
        {
            CriteriaValueOperator.Equals => floorMinutes == ceilingMinutes,
            CriteriaValueOperator.GreaterThan => floorMinutes < int.MaxValue,
            CriteriaValueOperator.GreaterThanOrEqual => true,
            CriteriaValueOperator.LessThan => ceilingMinutes > 0,
            CriteriaValueOperator.LessThanOrEqual => true,
            _ => false
        };
    }

    private bool CanApplyCertification(ContentRoute route, ElementFilter filter)
    {
        return route == ContentRoute.Movie &&
        !filter.Excluded &&
        filter.Family == ElementFamily.ContentRating &&
        filter.Alternatives.Count == 1 &&
        IsCertificationPair(filter.Alternatives[0]);
    }

    private bool IsCertificationPair(IReadOnlyList<ElementPredicate> alternative)
    {
        if (alternative.Count != 2)
        {
            return false;
        }

        var hasCountry = false;
        var hasRating = false;
        foreach (var predicate in alternative)
        {
            if (predicate.Negate ||
                predicate.Operator is not (CriteriaValueOperator.Equals or CriteriaValueOperator.In) ||
                predicate.StringValues.Count != 1)
            {
                return false;
            }

            if (predicate.Field == ElementField.RatingBody)
            {
                hasCountry = true;
            }
            else if (predicate.Field == ElementField.RatingValue)
            {
                hasRating = true;
            }
        }

        return hasCountry && hasRating;
    }

    private void RecordPlan(QueryPlan plan, ContentRoute route, bool knownEmpty, bool pageLimitExceeded)
    {
        var contentType = new KeyValuePair<string, object?>("content_type", route.ContentTypeName());
        Telemetry.Plans.Add(
            1,
            Telemetry.ProviderTag,
            contentType,
            new KeyValuePair<string, object?>("anchor_kind", plan.AnchorKindName()));
        if (knownEmpty)
        {
            Telemetry.PlansRefused.Add(
                1,
                Telemetry.ProviderTag,
                contentType,
                new KeyValuePair<string, object?>("reason", TerminalReason.KnownEmpty));
        }
        else if (pageLimitExceeded)
        {
            Telemetry.PlansRefused.Add(
                1,
                Telemetry.ProviderTag,
                contentType,
                new KeyValuePair<string, object?>("reason", TerminalReason.Unbounded));
        }
    }
}
