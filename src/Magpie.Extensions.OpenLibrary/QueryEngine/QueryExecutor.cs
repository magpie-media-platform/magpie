using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Extensions.OpenLibrary.Mapping;
using Magpie.Models.Enums;
using Magpie.Models.Filtering;
using Magpie.Models.MetadataSearch;

using Microsoft.Extensions.Logging;

namespace Magpie.Extensions.OpenLibrary.QueryEngine;

internal sealed class QueryExecutor(
    IOpenLibraryClient client,
    IQueryCache cache,
    ISettingsResolver settingsResolver,
    ILogger<QueryExecutor> logger) : IQueryExecutor
{
    private const int ScanBatchSize = 100;

    private static readonly JsonSerializerOptions TraceJsonOptions = new()
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private static readonly string[] SubjectFacetFields =
    [
        "person",
        "place",
        "time",
        "person_key",
        "place_key",
        "time_key"
    ];

    private static readonly string[] IdentifierFields = ["publish_date", "oclc", "lccn", "id_goodreads"];

    private static readonly string[] CandidateFields =
    [
        "key",
        "title",
        "author_name",
        "author_key",
        "first_publish_year",
        "language",
        "subject",
        "subject_key",
        "cover_i",
        "cover_edition_key",
        "isbn",
        "description",
        .. SubjectFacetFields,
        .. IdentifierFields,
        "publisher",
    ];

    private bool CanExecute(PlanNode node, NodeExecution branch)
    {
        branch.Reason ??= node.Reason;
        return node.State == PlanNodeState.Ready;
    }

    private PlanNode? FindFetch(PlanNode node)
    {
        if (node.Kind == PlanNodeKind.Fetch)
        {
            return node;
        }

        return node.Children.Select(FindFetch).FirstOrDefault(child => child is not null);
    }

    private IReadOnlyList<FilterGroup> EffectiveFilterGroups(PlanNode node)
    {
        if (node.FilterGroups.Count > 0)
        {
            return node.FilterGroups;
        }

        return
        [
            .. node.Filters.Select(filter => new FilterGroup
            {
                Alternatives = [new FilterAlternative { Filters = [filter] }]
            })
        ];
    }

    private int RequestLimit(ApiRequest request)
    {
        return int.TryParse(Parameter(request, "limit"), out var limit) ? limit : ScanBatchSize;
    }

    private string? Parameter(ApiRequest request, string key)
    {
        var match = request.Parameters.FirstOrDefault(
            parameter => string.Equals(parameter.Key, key, StringComparison.Ordinal));
        return match.Value;
    }

    private ProviderSearchPageInfo PageInfo(int page, bool hasMore)
    {
        return new ProviderSearchPageInfo
        {
            RequestedPage = Math.Max(1, page),
            HasMore = hasMore
        };
    }

    private QueryResult Empty(QueryPlan plan)
    {
        return new QueryResult
        {
            Hits = [],
            PageInfo = PageInfo(plan.Advanced ? 1 : plan.Page, hasMore: false),
            Warnings = plan.Warnings,
            CandidateLanguagePreferences = plan.CandidateLanguagePreferences
        };
    }

    private ApiRequest Build(PlanNode node, int limit, int offset)
    {
        if (node.Kind != PlanNodeKind.Fetch || node.State != PlanNodeState.Ready)
        {
            throw new InvalidOperationException("Open Library requires a ready fetch node.");
        }

        var parameters = new List<ApiRequestParameter>();
        parameters.Add(new ApiRequestParameter("q", BuildQuery(node)));
        parameters.Add(new ApiRequestParameter("fields", BuildFields(node.RequiredFields)));
        AddLanguage(parameters, node.LanguagePreference);
        if (HasOnlyLanguageSeed(node))
        {
            parameters.Add(new ApiRequestParameter("sort", "editions"));
        }

        parameters.Add(Number("limit", limit));
        parameters.Add(Number("offset", offset));

        return new ApiRequest
        {
            Kind = ApiRequestKind.Search,
            Path = "search.json",
            Parameters = parameters,
            CacheKey = CanonicalKey("search.json", parameters)
        };
    }

    private ApiRequest BuildAuthor(string authorOlid)
    {
        var normalized = authorOlid.NormalizeOlid('A')
            ?? throw new InvalidOperationException(
                "Open Library requires a valid Open Library identifier for an author.");
        var path = string.Format(CultureInfo.InvariantCulture, "authors/{0}.json", normalized);
        return new ApiRequest
        {
            Kind = ApiRequestKind.Author,
            Path = path,
            Parameters = [],
            CacheKey = path
        };
    }

    private ApiRequest BuildEdition(string editionOlid)
    {
        var normalized = editionOlid.NormalizeOlid('M')
            ?? throw new InvalidOperationException(
                "Open Library requires a valid Open Library identifier for an edition.");
        var path = string.Format(CultureInfo.InvariantCulture, "books/{0}.json", normalized);
        return new ApiRequest
        {
            Kind = ApiRequestKind.Edition,
            Path = path,
            Parameters = [],
            CacheKey = path
        };
    }

    private ApiRequest BuildEditionByIsbn(string isbn)
    {
        var normalized = isbn.NormalizeIsbn()
            ?? throw new InvalidOperationException("Open Library requires a valid ISBN.");
        var path = string.Format(CultureInfo.InvariantCulture, "isbn/{0}.json", normalized);
        return new ApiRequest
        {
            Kind = ApiRequestKind.Edition,
            Path = path,
            Parameters = [],
            CacheKey = path
        };
    }

    private string BuildQuery(PlanNode node)
    {
        var groups = node.SourceGroups.Count > 0
            ? node.SourceGroups
            : [.. node.SourceTerms.Select(term => new SourceGroup
            {
                Alternatives = [new SourceAlternative { Terms = [term] }]
            })];
        var expressions = new List<string>(groups.Count);
        foreach (var group in groups)
        {
            var alternatives = group.Alternatives
                .Select(alternative => alternative.Terms.Select(BuildTerm).Where(value => value.Length > 0).ToArray())
                .Where(terms => terms.Length > 0)
                .Select(terms => terms.Length == 1 ? terms[0] : string.Concat("(", string.Join(" AND ", terms), ")"))
                .ToArray();
            if (alternatives.Length == 0)
            {
                continue;
            }

            expressions.Add(alternatives.Length == 1
                ? alternatives[0]
                : string.Concat("(", string.Join(" OR ", alternatives), ")"));
        }

        if (expressions.Count == 0)
        {
            throw new InvalidOperationException("Open Library requires a positive source query.");
        }

        return string.Join(" AND ", expressions);
    }

    private string BuildTerm(SourceTerm term)
    {
        var values = term.Values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
        if (values.Length == 0)
        {
            return string.Empty;
        }

        var expression = term.RangeSyntax
            ? string.Concat(term.Field, ":", values[0])
            : values.Length == 1
                ? string.Concat(term.Field, ":", ValueExpression(term, values[0]))
                : string.Concat(
                    term.Field,
                    ":(",
                    string.Join(" OR ", values.Select(value => ValueExpression(term, value))),
                    ")");
        return term.Negate ? string.Concat("NOT ", expression) : expression;
    }

    private string ValueExpression(SourceTerm term, string value)
    {
        if (term.WildcardPlacement == WildcardPlacement.None || value.Any(char.IsWhiteSpace))
        {
            return string.Concat("\"", EscapePhrase(value), "\"");
        }

        var escaped = EscapeTerm(value);
        return term.WildcardPlacement switch
        {
            WildcardPlacement.Leading => string.Concat("*", escaped),
            WildcardPlacement.Trailing => string.Concat(escaped, "*"),
            _ => string.Concat("\"", EscapePhrase(value), "\"")
        };
    }

    private string EscapePhrase(string value)
    {
        var builder = new StringBuilder(value.Length + 8);
        foreach (var character in value.Normalize(NormalizationForm.FormKC))
        {
            if (char.IsControl(character))
            {
                builder.Append(' ');
                continue;
            }

            if (character is '\\' or '"')
            {
                builder.Append('\\');
            }

            builder.Append(character);
        }

        return builder.ToString().Trim();
    }

    private void AddLanguage(List<ApiRequestParameter> parameters, string language)
    {
        if (!string.IsNullOrWhiteSpace(language))
        {
            parameters.Add(new ApiRequestParameter("lang", language));
        }
    }

    private string EscapeTerm(string value)
    {
        var builder = new StringBuilder(value.Length + 8);
        foreach (var character in value.Normalize(NormalizationForm.FormKC))
        {
            if (char.IsControl(character))
            {
                builder.Append(' ');
                continue;
            }

            if (character is '+' or '-' or '&' or '|' or '!' or '(' or ')' or '{' or '}'
                or '[' or ']' or '^' or '"' or '~' or '*' or '?' or ':' or '\\' or '/')
            {
                builder.Append('\\');
            }

            builder.Append(character);
        }

        return builder.ToString().Trim();
    }

    private bool HasOnlyLanguageSeed(PlanNode node)
    {
        var terms = node.SourceTerms
            .Concat(node.SourceGroups
                .SelectMany(group => group.Alternatives)
                .SelectMany(alternative => alternative.Terms))
            .ToArray();
        return terms.Length > 0 &&
            terms.All(term => string.Equals(term.Field, "language", StringComparison.Ordinal));
    }

    private string BuildFields(IReadOnlyList<string> requiredFields)
    {
        var fields = CandidateFields
            .Concat(requiredFields)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.Ordinal);
        return string.Join(",", fields);
    }

    private ApiRequestParameter Number(string key, int value)
    {
        return new ApiRequestParameter(key, Math.Max(0, value).ToString(CultureInfo.InvariantCulture));
    }

    private string CanonicalKey(string path, IReadOnlyList<ApiRequestParameter> parameters)
    {
        var ordered = parameters
            .OrderBy(parameter => parameter.Key, StringComparer.Ordinal)
            .ThenBy(parameter => parameter.Value, StringComparer.Ordinal)
            .Select(parameter => string.Concat(parameter.Key, "=", parameter.Value));
        return string.Concat(path, "?", string.Join("&", ordered));
    }

    private FilterOutcome Evaluate(WorkHit hit, FilterGroup group)
    {
        var unverifiable = false;
        foreach (var alternative in group.Alternatives)
        {
            var outcome = alternative.AlignAuthors
                ? EvaluateAlignedAuthorAlternative(hit, alternative)
                : alternative.AlignPublishers
                    ? EvaluateAlignedPublisherAlternative(hit, alternative)
                    : alternative.AlignTexts
                        ? EvaluateAlignedTextAlternative(hit, alternative)
                        : alternative.AlignGenres
                            ? EvaluateAlignedGenreAlternative(hit, alternative)
                            : alternative.AlignSubjectFacet is { } facet
                                ? EvaluateAlignedSubjectFacetAlternative(hit, alternative, facet)
                                : EvaluateAlternative(hit, alternative);
            if (outcome == FilterOutcome.Matched)
            {
                return group.Negate ? FilterOutcome.Violated : FilterOutcome.Matched;
            }

            unverifiable |= outcome == FilterOutcome.Unverifiable;
        }

        if (unverifiable)
        {
            return FilterOutcome.Unverifiable;
        }

        return group.Negate ? FilterOutcome.Matched : FilterOutcome.Violated;
    }

    private FilterOutcome Evaluate(WorkHit hit, QueryFilter filter)
    {
        var matched = filter.Field switch
        {
            QueryField.Title => CompareText([hit.Title], filter.Operator, filter.Values),
            QueryField.OriginalTitle => CompareText([hit.Title], filter.Operator, filter.Values),
            QueryField.PublishDate => CompareDate(hit, filter),
            QueryField.Language => CompareLanguage(hit.Languages, filter.Operator, filter.Values),
            QueryField.Genre => CompareGenre(hit, filter.Operator, filter.Values),
            QueryField.WorkOlid => CompareMembership([hit.WorkOlid], filter.Values),
            QueryField.Isbn => CompareMembership(hit.Isbns, filter.Values),
            QueryField.Oclc => CompareMembership(hit.Oclcs, filter.Values),
            QueryField.Lccn => CompareMembership(hit.Lccns, filter.Values),
            QueryField.Goodreads => CompareMembership(hit.GoodreadsIds, filter.Values),
            QueryField.AuthorName => CompareText(
                [.. hit.Authors.Select(author => author.Name)],
                filter.Operator,
                filter.Values),
            QueryField.AuthorOlid => CompareMembership(
                [.. hit.Authors.Select(author => author.Olid)],
                filter.Values),
            QueryField.Publisher => CompareText(hit.Publishers, filter.Operator, filter.Values),
            QueryField.Overview => CompareText(TextList(hit.Description), filter.Operator, filter.Values),
            QueryField.Pages => CompareText(
                hit.NumberOfPagesMedian is { } pages ? [pages.ToString(CultureInfo.InvariantCulture)] : [],
                filter.Operator,
                filter.Values),
            QueryField.Subtitle => CompareText(TextList(hit.Subtitle), filter.Operator, filter.Values),
            QueryField.AlternativeTitle => CompareText(hit.AlternativeTitles, filter.Operator, filter.Values),
            QueryField.AlternateText => CompareText(AlternateTexts(hit), filter.Operator, filter.Values),
            QueryField.Person => CompareSubjectFacet(hit.Persons, filter.Operator, filter.Values),
            QueryField.Place => CompareSubjectFacet(hit.Places, filter.Operator, filter.Values),
            QueryField.Time => CompareSubjectFacet(hit.Times, filter.Operator, filter.Values),
            QueryField.Cover => hit.CoverId is not null || hit.CoverEditionOlid is not null,
            _ => null
        };
        return ToOutcome(matched, filter.Negate);
    }

    private FilterOutcome EvaluateText(IReadOnlyList<string> candidates, QueryFilter filter)
    {
        return ToOutcome(CompareText(candidates, filter.Operator, filter.Values), filter.Negate);
    }

    private FilterOutcome EvaluateMembership(IReadOnlyList<string> candidates, QueryFilter filter)
    {
        return ToOutcome(CompareMembership(candidates, filter.Values), filter.Negate);
    }

    private FilterOutcome ToOutcome(bool? matched, bool negate)
    {
        if (matched is null)
        {
            return FilterOutcome.Unverifiable;
        }

        matched ^= negate;
        return matched.Value ? FilterOutcome.Matched : FilterOutcome.Violated;
    }

    private bool? CompareDate(WorkHit hit, QueryFilter filter)
    {
        var candidate = hit.RefinedPublishTemporal();
        var present = candidate is not null;
        if (filter.Operator is CriteriaValueOperator.IsNull or CriteriaValueOperator.IsNotNull)
        {
            return filter.Operator == CriteriaValueOperator.IsNotNull ? present : !present;
        }

        if (!present || filter.TemporalValues.Count == 0)
        {
            return null;
        }

        return candidate!.Compare(filter.Operator, filter.TemporalValues);
    }

    private bool? CompareLanguage(
        IReadOnlyList<string> candidates,
        CriteriaValueOperator op,
        IReadOnlyList<string> requested)
    {
        if (op is CriteriaValueOperator.IsNull or CriteriaValueOperator.IsNotNull)
        {
            var present = candidates.Any(candidate => !string.IsNullOrWhiteSpace(candidate));
            return op == CriteriaValueOperator.IsNotNull ? present : !present;
        }

        if (candidates.Count == 0 || requested.Count == 0)
        {
            return null;
        }

        var normalized = candidates.Select(candidate => candidate.NormalizeLanguage())
            .Where(value => value is not null)
            .Cast<string>()
            .ToArray();
        var normalizedRequested = requested.Select(value => value.NormalizeLanguage())
            .Where(value => value is not null)
            .Cast<string>()
            .ToArray();
        if (normalized.Length == 0 || normalizedRequested.Length == 0)
        {
            return null;
        }

        var matched = CompareMembership(normalized, normalizedRequested);
        return op switch
        {
            CriteriaValueOperator.Equals or CriteriaValueOperator.In => matched,
            _ => null
        };
    }

    private bool? CompareGenre(WorkHit hit, CriteriaValueOperator op, IReadOnlyList<string> requested)
    {
        if (requested.Count == 0 || hit.Subjects.Count == 0 && hit.SubjectKeys.Count == 0)
        {
            return null;
        }

        if (op is CriteriaValueOperator.Equals or CriteriaValueOperator.In)
        {
            var keys = hit.SubjectKeys
                .Concat(hit.Subjects.Select(subject => subject.NormalizeSubjectKey()))
                .Where(value => value.Length > 0)
                .ToArray();
            var wanted = requested.Select(value => value.NormalizeSubjectKey()).ToArray();
            return CompareMembership(keys, wanted);
        }

        return CompareText(hit.Subjects, op, requested);
    }

    private bool? CompareSubjectFacet(
        IReadOnlyList<SubjectFacetPair> facets,
        CriteriaValueOperator op,
        IReadOnlyList<string> requested)
    {
        if (requested.Count == 0 || facets.Count == 0)
        {
            return null;
        }

        if (op is CriteriaValueOperator.Equals or CriteriaValueOperator.In)
        {
            var candidates = (
                from facet in facets
                from value in new[] { facet.Key, facet.Name }
                where !string.IsNullOrWhiteSpace(value)
                let normalized = value.NormalizeSubjectKey()
                where normalized.Length > 0
                select normalized).ToArray();

            var wanted = requested.Select(value => value.NormalizeSubjectKey()).ToArray();

            return CompareMembership(candidates, wanted);
        }

        return CompareText([.. facets.Select(facet => facet.Name)], op, requested);
    }

    private bool? CompareText(
        IReadOnlyList<string> candidates,
        CriteriaValueOperator op,
        IReadOnlyList<string> requested)
    {
        if (op is CriteriaValueOperator.IsNull or CriteriaValueOperator.IsNotNull)
        {
            var present = candidates.Any(value => !string.IsNullOrWhiteSpace(value));
            return op == CriteriaValueOperator.IsNotNull ? present : !present;
        }

        if (candidates.Count == 0 || requested.Count == 0)
        {
            return null;
        }

        foreach (var candidate in candidates)
        {
            var normalizedCandidate = NormalizeText(candidate);
            foreach (var value in requested)
            {
                var normalizedValue = NormalizeText(value);
                var matched = op switch
                {
                    CriteriaValueOperator.Equals or CriteriaValueOperator.In =>
                        string.Equals(normalizedCandidate, normalizedValue, StringComparison.Ordinal),
                    CriteriaValueOperator.ContainsString =>
                        normalizedCandidate.Contains(normalizedValue, StringComparison.Ordinal),
                    CriteriaValueOperator.ContainsWord => ContainsWord(normalizedCandidate, normalizedValue),
                    CriteriaValueOperator.StartsWithString =>
                        normalizedCandidate.StartsWith(normalizedValue, StringComparison.Ordinal),
                    CriteriaValueOperator.EndsWithString =>
                        normalizedCandidate.EndsWith(normalizedValue, StringComparison.Ordinal),
                    _ => false
                };
                if (matched)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private bool CompareMembership(IReadOnlyList<string> candidates, IReadOnlyList<string> requested)
    {
        foreach (var candidate in candidates)
        {
            foreach (var value in requested)
            {
                if (string.Equals(candidate, value, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private bool ContainsWord(string candidate, string value)
    {
        if (value.Length == 0)
        {
            return false;
        }

        return Regex.IsMatch(
            candidate,
            string.Concat("(?<![\\p{L}\\p{N}])", Regex.Escape(value), "(?![\\p{L}\\p{N}])"),
            RegexOptions.CultureInvariant);
    }

    private string NormalizeText(string value)
    {
        return Regex.Replace(
            value.Normalize(NormalizationForm.FormKC).Trim().ToLowerInvariant(),
            "\\s+",
            " ",
            RegexOptions.CultureInvariant);
    }

    private IReadOnlyList<string> TextList(string? value) => value is null ? [] : [value];

    private IReadOnlyList<string> AlternateTexts(WorkHit hit)
    {
        return hit.Subtitle is null ? hit.AlternativeTitles : [hit.Subtitle, .. hit.AlternativeTitles];
    }

    private bool MatchesTextElement((string Kind, string Text) element, IReadOnlyList<QueryFilter> filters)
    {
        foreach (var filter in filters)
        {
            var admissible = filter.Field switch
            {
                QueryField.Subtitle => element.Kind == "subtitle",
                QueryField.AlternativeTitle => element.Kind == "alternative_title",
                _ => true
            };
            if (!admissible)
            {
                return false;
            }

            var matched = CompareText([element.Text], filter.Operator, filter.Values);
            if (matched is null || matched.Value == filter.Negate)
            {
                return false;
            }
        }

        return true;
    }

    private FilterOutcome EvaluateAlternative(WorkHit hit, FilterAlternative alternative)
    {
        var unverifiable = false;
        foreach (var filter in alternative.Filters)
        {
            var outcome = Evaluate(hit, filter);
            if (outcome == FilterOutcome.Violated)
            {
                return outcome;
            }

            unverifiable |= outcome == FilterOutcome.Unverifiable;
        }

        return unverifiable ? FilterOutcome.Unverifiable : FilterOutcome.Matched;
    }

    private FilterOutcome EvaluateAlignedTextAlternative(WorkHit hit, FilterAlternative alternative)
    {
        var elements = new List<(string Kind, string Text)>();
        if (hit.Subtitle is not null)
        {
            elements.Add(("subtitle", hit.Subtitle));
        }

        foreach (var title in hit.AlternativeTitles)
        {
            elements.Add(("alternative_title", title));
        }

        if (elements.Count == 0)
        {
            return FilterOutcome.Unverifiable;
        }

        foreach (var element in elements)
        {
            if (MatchesTextElement(element, alternative.Filters))
            {
                return FilterOutcome.Matched;
            }
        }

        return FilterOutcome.Violated;
    }

    private FilterOutcome EvaluateAlignedGenreAlternative(WorkHit hit, FilterAlternative alternative)
    {
        var candidates = hit.Subjects.Concat(hit.SubjectKeys)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (candidates.Length == 0)
        {
            return FilterOutcome.Unverifiable;
        }

        foreach (var candidate in candidates)
        {
            var matched = true;
            foreach (var filter in alternative.Filters)
            {
                var comparison = filter.Operator is CriteriaValueOperator.Equals or CriteriaValueOperator.In
                    ? CompareMembership(
                        [candidate.NormalizeSubjectKey()],
                        [.. filter.Values.Select(value => value.NormalizeSubjectKey())])
                    : CompareText([candidate], filter.Operator, filter.Values);
                if (comparison is null || comparison.Value == filter.Negate)
                {
                    matched = false;
                    break;
                }
            }

            if (matched)
            {
                return FilterOutcome.Matched;
            }
        }

        return FilterOutcome.Violated;
    }

    private FilterOutcome EvaluateAlignedPublisherAlternative(WorkHit hit, FilterAlternative alternative)
    {
        if (hit.Publishers.Count == 0)
        {
            return FilterOutcome.Unverifiable;
        }

        var unverifiable = false;
        foreach (var publisher in hit.Publishers)
        {
            var outcome = EvaluatePublisherAlternative(hit, publisher, alternative.Filters);
            if (outcome == FilterOutcome.Matched)
            {
                return outcome;
            }

            unverifiable |= outcome == FilterOutcome.Unverifiable;
        }

        return unverifiable ? FilterOutcome.Unverifiable : FilterOutcome.Violated;
    }

    private FilterOutcome EvaluateAlignedSubjectFacetAlternative(
        WorkHit hit,
        FilterAlternative alternative,
        QueryField facet)
    {
        var facets = facet switch
        {
            QueryField.Person => hit.Persons,
            QueryField.Place => hit.Places,
            QueryField.Time => hit.Times,
            _ => []
        };
        if (facets.Count == 0)
        {
            return FilterOutcome.Unverifiable;
        }

        for (var index = 0; index < facets.Count; index++)
        {
            var pairFacets = new[] { facets[index] };
            var matched = true;
            foreach (var filter in alternative.Filters)
            {
                var comparison = filter.Field == facet
                    ? CompareSubjectFacet(pairFacets, filter.Operator, filter.Values)
                    : Evaluate(hit, filter) switch
                    {
                        FilterOutcome.Matched => true,
                        FilterOutcome.Violated => false,
                        _ => null
                    };
                if (comparison is null || comparison.Value == filter.Negate)
                {
                    matched = false;
                    break;
                }
            }

            if (matched)
            {
                return FilterOutcome.Matched;
            }
        }

        return FilterOutcome.Violated;
    }

    private FilterOutcome EvaluatePublisherAlternative(
        WorkHit hit,
        string publisher,
        IReadOnlyList<QueryFilter> filters)
    {
        var unverifiable = false;
        foreach (var filter in filters)
        {
            var outcome = filter.Field == QueryField.Publisher
                ? EvaluateText([publisher], filter)
                : Evaluate(hit, filter);
            if (outcome == FilterOutcome.Violated)
            {
                return outcome;
            }

            unverifiable |= outcome == FilterOutcome.Unverifiable;
        }

        return unverifiable ? FilterOutcome.Unverifiable : FilterOutcome.Matched;
    }

    private FilterOutcome EvaluateAlignedAuthorAlternative(WorkHit hit, FilterAlternative alternative)
    {
        if (hit.Authors.Count == 0)
        {
            return FilterOutcome.Unverifiable;
        }

        var unverifiable = false;
        foreach (var author in hit.Authors)
        {
            var outcome = EvaluateAuthorAlternative(hit, author, alternative.Filters);
            if (outcome == FilterOutcome.Matched)
            {
                return outcome;
            }

            unverifiable |= outcome == FilterOutcome.Unverifiable;
        }

        return unverifiable ? FilterOutcome.Unverifiable : FilterOutcome.Violated;
    }

    private FilterOutcome EvaluateAuthorAlternative(WorkHit hit, AuthorPair author, IReadOnlyList<QueryFilter> filters)
    {
        var unverifiable = false;
        foreach (var filter in filters)
        {
            var outcome = filter.Field switch
            {
                QueryField.AuthorName => EvaluateText([author.Name], filter),
                QueryField.AuthorOlid => EvaluateMembership([author.Olid], filter),
                _ => Evaluate(hit, filter)
            };
            if (outcome == FilterOutcome.Violated)
            {
                return outcome;
            }

            unverifiable |= outcome == FilterOutcome.Unverifiable;
        }

        return unverifiable ? FilterOutcome.Unverifiable : FilterOutcome.Matched;
    }

    public async Task<QueryResult> ExecuteSearchAsync(QueryPlan plan, CancellationToken cancellationToken)
    {
        var execution = new QueryExecution();
        if (plan.Root.State != PlanNodeState.Ready || plan.Advanced && plan.MaxResults <= 0)
        {
            execution.AddBranch(plan.Root);
            execution.Completed = true;
            execution.Exhausted = true;
            execution.FinalOutcome = "empty";
            var empty = Empty(plan);
            TraceExecution(execution);
            return empty;
        }

        var desired = plan.Advanced ? plan.MaxResults : QueryLimits.LogicalPageSize;
        var fetch = FindFetch(plan.Root);
        var firstFetchIsDirect = fetch?.PagingMode == FetchPagingMode.Direct;
        var rootIsDirect = plan.Root.Kind == PlanNodeKind.Fetch &&
            plan.Root.PagingMode == FetchPagingMode.Direct;
        var requestedOffset = plan.Advanced
            ? 0L
            : checked((Math.Max(1, plan.Page) - 1L) * QueryLimits.LogicalPageSize);
        if (requestedOffset > int.MaxValue)
        {
            var branch = execution.AddBranch(plan.Root);
            branch.State = PlanNodeState.Unavailable;
            branch.Reason = "The requested page offset exceeds the Open Library API integer range.";
            branch.Exhausted = true;
            execution.Completed = true;
            execution.Exhausted = true;
            execution.FinalOutcome = "unavailable";
            execution.Warnings.Add("Open Library cannot represent the requested page offset. No search was run.");
            TraceExecution(execution);
            return new QueryResult
            {
                Hits = [],
                PageInfo = PageInfo(plan.Page, hasMore: false),
                Warnings = [.. plan.Warnings, .. execution.Warnings],
                CandidateLanguagePreferences = plan.CandidateLanguagePreferences
            };
        }

        var maximumProbe = desired == int.MaxValue ? int.MaxValue : desired + 1;
        var batchMaximumResults = plan.Advanced && desired == int.MaxValue;
        execution.FetchLimit = firstFetchIsDirect
            ? plan.Advanced ? batchMaximumResults ? ScanBatchSize : maximumProbe : QueryLimits.LogicalPageSize
            : ScanBatchSize;
        execution.FetchOffset = rootIsDirect ? (int)requestedOffset : 0;
        execution.SingleFetch = rootIsDirect && !batchMaximumResults;
        execution.ExactClass = rootIsDirect && !plan.Advanced;
        var remainingSkip = rootIsDirect || plan.Advanced ? 0L : requestedOffset;
        var demand = plan.Advanced
            ? maximumProbe
            : checked(remainingSkip + QueryLimits.LogicalPageSize + 1L);
        var hits = new List<WorkHit>(Math.Min(desired, QueryLimits.LogicalPageSize) + 1);
        try
        {
            var context = await settingsResolver.GetRequestContextAsync(cancellationToken).ConfigureAwait(false);
            await foreach (var hit in ExecuteNodeAsync(
                plan.Root,
                context,
                execution,
                demand,
                null,
                cancellationToken).ConfigureAwait(false))
            {
                if (remainingSkip > 0)
                {
                    remainingSkip--;
                    continue;
                }

                hits.Add(hit);
                if (hits.Count > desired)
                {
                    break;
                }
            }

            execution.Completed = true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TimeoutException or InvalidOperationException)
        {
            logger.LogWarning(ex, "Open Library search execution failed");
            execution.Failed = true;
            execution.Failure = ex.GetType().Name;
        }

        var hasMore = hits.Count > desired ||
            execution.UpstreamHasMore ||
            execution.Failed && !execution.Exhausted;
        if (hits.Count > desired)
        {
            hits.RemoveAt(desired);
        }

        var warnings = plan.Warnings.ToList();
        if (execution.HasUnverifiableCriteria)
        {
            warnings.Add("Open Library retained rows with missing optional fields and degraded certainty.");
        }

        if (execution.Failed || execution.HydrationFailed)
        {
            warnings.Add("Open Library could not complete the remote search.");
        }

        warnings.AddRange(execution.Warnings);
        execution.ReturnedRows = hits.Count;
        execution.HasMore = hasMore;
        execution.FinalOutcome = execution.Failed
            ? "partial"
            : hits.Count == 0 ? "empty" : "success";
        TraceExecution(execution);
        return new QueryResult
        {
            Hits = hits,
            PageInfo = PageInfo(plan.Advanced ? 1 : plan.Page, hasMore),
            Warnings = [.. warnings.Distinct()],
            CandidateLanguagePreferences = plan.CandidateLanguagePreferences
        };
    }

    public async Task<WorkResolution> ResolveWorkAsync(QueryPlan plan, CancellationToken cancellationToken)
    {
        if (plan.Root.State != PlanNodeState.Ready ||
            plan.ResolutionSource is null ||
            plan.ResolutionValue is null)
        {
            return new WorkResolution
            {
                Kind = WorkResolutionKind.NotFound,
                Warnings = plan.Warnings
            };
        }

        var fetch = FindFetch(plan.Root)
            ?? throw new InvalidOperationException("Open Library resolution requires a fetch node.");
        var request = Build(
            fetch,
            string.Equals(plan.ResolutionSource, Constants.ProviderType, StringComparison.OrdinalIgnoreCase)
                ? 2
                : 5,
            0);
        var factoryRan = false;
        var result = await cache.GetResolutionAsync(
            request.CacheKey,
            operationToken =>
            {
                factoryRan = true;
                return ResolveUncachedAsync(plan, operationToken);
            },
            cancellationToken).ConfigureAwait(false);
        if (!factoryRan)
        {
            var execution = new QueryExecution
            {
                Completed = true,
                Exhausted = true,
                ReturnedRows = result.Hit is null ? 0 : 1,
                FinalOutcome = result.Kind.ToString().ToLowerInvariant()
            };
            execution.Requests.Add(DescribeRequest(request, "resolution"));
            TraceExecution(execution);
        }

        return result;
    }

    public async Task<AuthorDto?> GetAuthorAsync(string authorOlid, CancellationToken cancellationToken)
    {
        var context = await settingsResolver.GetRequestContextAsync(cancellationToken).ConfigureAwait(false);
        return await GetAuthorAsync(authorOlid, context, null, cancellationToken).ConfigureAwait(false);
    }

    public async Task<EditionDto?> GetRepresentativeEditionAsync(WorkHit hit, CancellationToken cancellationToken)
    {
        var request = hit.CoverEditionOlid is not null
            ? BuildEdition(hit.CoverEditionOlid)
            : hit.Isbns.RepresentativeIsbn() is { } isbn
                ? BuildEditionByIsbn(isbn)
                : null;
        if (request is null)
        {
            return null;
        }

        var context = await settingsResolver.GetRequestContextAsync(cancellationToken).ConfigureAwait(false);
        var lookup = await cache.GetEditionAsync(
            request.CacheKey,
            async operationToken =>
            {
                var edition = await client.GetJsonAsync<EditionDto>(
                    request,
                    context,
                    operationToken).ConfigureAwait(false);
                return new EditionLookup { Found = edition is not null, Edition = edition };
            },
            cancellationToken).ConfigureAwait(false);
        return lookup.Edition;
    }

    private async IAsyncEnumerable<WorkHit> ExecuteNodeAsync(
        PlanNode node,
        RequestContext context,
        QueryExecution execution,
        long? demand,
        long? pageSizeHint,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var stream = node.Kind switch
        {
            PlanNodeKind.Fetch => ExecuteFetchAsync(node, context, execution, demand, pageSizeHint, cancellationToken),
            PlanNodeKind.Filter => ExecuteFilterAsync(
                node,
                context,
                execution,
                demand,
                pageSizeHint,
                cancellationToken),
            _ => throw new InvalidOperationException("Open Library encountered an unknown query node.")
        };
        await foreach (var hit in stream.ConfigureAwait(false))
        {
            yield return hit;
        }
    }

    private async IAsyncEnumerable<WorkHit> ExecuteFetchAsync(
        PlanNode node,
        RequestContext context,
        QueryExecution execution,
        long? demand,
        long? pageSizeHint,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var branch = execution.AddBranch(node);
        if (!CanExecute(node, branch))
        {
            yield break;
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var survivorScan = node.PagingMode == FetchPagingMode.SurvivorScan;
        var fetchLimit = survivorScan
            ? pageSizeHint is > 0
                ? (int)Math.Min(ScanBatchSize, pageSizeHint.Value)
                : ScanBatchSize
            : execution.FetchLimit;
        var offset = survivorScan ? 0 : execution.FetchOffset;
        var singleFetch = !survivorScan && execution.SingleFetch;
        var emitted = 0L;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var request = Build(node, fetchLimit, offset);
            var page = await LoadSourcePageAsync(
                request,
                context,
                execution,
                !survivorScan && execution.ExactClass,
                cancellationToken)
                .ConfigureAwait(false);
            var requestLimit = RequestLimit(request);
            branch.RowsIn += page.SourceRowCount;
            var nextOffset = checked((long)offset + page.SourceRowCount);
            branch.Exhausted = page.SourceRowCount == 0 ||
                page.SourceRowCount < requestLimit ||
                page.NumFoundExact && nextOffset >= page.NumFound;
            execution.UpstreamHasMore = !branch.Exhausted;
            execution.Exhausted = branch.Exhausted;
            foreach (var hit in page.Hits)
            {
                if (!seen.Add(hit.WorkOlid))
                {
                    continue;
                }

                branch.RowsOut++;
                emitted++;
                yield return hit;
                if (demand is { } maximum && emitted >= maximum)
                {
                    yield break;
                }
            }

            if (branch.Exhausted || singleFetch)
            {
                yield break;
            }

            if (nextOffset > int.MaxValue)
            {
                branch.State = PlanNodeState.Unavailable;
                branch.Reason = "The next Open Library source offset exceeds the API integer range.";
                execution.Failed = true;
                execution.Failure = "OffsetOutOfRange";
                execution.Warnings.Add("Open Library exhausted its source offset range before completing the search.");
                yield break;
            }

            offset = (int)nextOffset;
        }
    }

    private async IAsyncEnumerable<WorkHit> ExecuteFilterAsync(
        PlanNode node,
        RequestContext context,
        QueryExecution execution,
        long? demand,
        long? pageSizeHint,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var branch = execution.AddBranch(node);
        if (!CanExecute(node, branch) || node.Children.Count != 1)
        {
            yield break;
        }

        var groups = EffectiveFilterGroups(node);
        var emitted = 0L;
        await foreach (var hit in ExecuteNodeAsync(
            node.Children[0],
            context,
            execution,
            null,
            pageSizeHint,
            cancellationToken).ConfigureAwait(false))
        {
            branch.RowsIn++;
            var violated = false;
            for (var index = 0; index < groups.Count; index++)
            {
                var outcome = Evaluate(hit, groups[index]);
                if (outcome == FilterOutcome.Violated)
                {
                    execution.Reject(string.Concat("group-", index));
                    violated = true;
                    break;
                }

                execution.HasUnverifiableCriteria |= outcome == FilterOutcome.Unverifiable;
            }

            if (violated ||
                !await SatisfiesResidualsAsync(
                    hit,
                    node.Residuals,
                    context,
                    execution,
                    cancellationToken).ConfigureAwait(false))
            {
                continue;
            }

            branch.RowsOut++;
            emitted++;
            yield return hit;
            if (demand is { } maximum && emitted >= maximum)
            {
                yield break;
            }
        }

        branch.Exhausted = execution.Exhausted;
    }

    private async Task<WorkResolution> ResolveUncachedAsync(QueryPlan plan, CancellationToken cancellationToken)
    {
        var context = await settingsResolver.GetRequestContextAsync(cancellationToken).ConfigureAwait(false);
        var execution = new QueryExecution
        {
            FetchLimit = string.Equals(
                plan.ResolutionSource,
                Constants.ProviderType,
                StringComparison.OrdinalIgnoreCase)
                    ? 2
                    : 5
        };
        var hits = new List<WorkHit>();
        await foreach (var hit in ExecuteNodeAsync(
            plan.Root,
            context,
            execution,
            null,
            null,
            cancellationToken)
            .ConfigureAwait(false))
        {
            var matches = string.Equals(
                plan.ResolutionSource,
                Constants.ProviderType,
                StringComparison.OrdinalIgnoreCase)
                    ? string.Equals(hit.WorkOlid, plan.ResolutionValue, StringComparison.OrdinalIgnoreCase)
                    : hit.Isbns.Any(isbn => string.Equals(
                        isbn,
                        plan.ResolutionValue,
                        StringComparison.OrdinalIgnoreCase));
            if (matches)
            {
                hits.Add(hit);
            }
        }

        execution.Completed = true;
        execution.ReturnedRows = hits.Count;
        if (hits.Count == 0)
        {
            execution.FinalOutcome = "not_found";
            TraceExecution(execution);
            return new WorkResolution { Kind = WorkResolutionKind.NotFound };
        }

        var distinct = hits.GroupBy(hit => hit.WorkOlid, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();
        var ambiguous = distinct.Length > 1;
        execution.FinalOutcome = ambiguous ? "ambiguous" : "resolved";
        TraceExecution(execution);
        return new WorkResolution
        {
            Kind = ambiguous ? WorkResolutionKind.Ambiguous : WorkResolutionKind.Resolved,
            Hit = distinct[0],
            Warnings = ambiguous
                ?
                [
                    "Open Library returned multiple works. Source order selected the first verified work."
                ]
                : []
        };
    }

    private async Task<AuthorDto?> GetAuthorAsync(
        string authorOlid,
        RequestContext context,
        QueryExecution? execution,
        CancellationToken cancellationToken)
    {
        var request = BuildAuthor(authorOlid);
        var logicalRequest = DescribeRequest(request, "author");
        execution?.Requests.Add(logicalRequest);
        var lookup = await cache.GetAuthorAsync(
            request.CacheKey,
            async operationToken =>
            {
                var stopwatch = Stopwatch.StartNew();
                try
                {
                    var author = await client.GetJsonAsync<AuthorDto>(
                        request,
                        context,
                        operationToken).ConfigureAwait(false);
                    logicalRequest.Status = author is null ? "not_found" : "success";
                    logicalRequest.RowsOut = author is null ? 0 : 1;
                    logicalRequest.ElapsedMilliseconds = stopwatch.ElapsedMilliseconds;
                    return new AuthorLookup { Found = author is not null, Author = author };
                }
                catch (Exception ex)
                {
                    logicalRequest.Status = ex.GetType().Name;
                    logicalRequest.ElapsedMilliseconds = stopwatch.ElapsedMilliseconds;
                    throw;
                }
            },
            cancellationToken).ConfigureAwait(false);
        return lookup.Author;
    }

    private Task<SourcePage> LoadSourcePageAsync(
        ApiRequest request,
        RequestContext context,
        QueryExecution execution,
        bool exactClass,
        CancellationToken cancellationToken)
    {
        var logicalRequest = DescribeRequest(request, "search");
        execution.Requests.Add(logicalRequest);
        return cache.GetSourcePageAsync(
            request.CacheKey,
            exactClass,
            async operationToken =>
            {
                var stopwatch = Stopwatch.StartNew();
                try
                {
                    var response = await client.GetJsonAsync<SearchResponseDto>(
                        request,
                        context,
                        operationToken).ConfigureAwait(false)
                        ?? throw new InvalidOperationException("Open Library returned no search response.");
                    var page = response.ToSourcePage();
                    logicalRequest.Status = "success";
                    logicalRequest.RowsIn = page.SourceRowCount;
                    logicalRequest.RowsOut = page.Hits.Count;
                    logicalRequest.ElapsedMilliseconds = stopwatch.ElapsedMilliseconds;
                    return page;
                }
                catch (Exception ex)
                {
                    logicalRequest.Status = ex.GetType().Name;
                    logicalRequest.ElapsedMilliseconds = stopwatch.ElapsedMilliseconds;
                    throw;
                }
            },
            cancellationToken);
    }

    private async Task<bool> SatisfiesResidualsAsync(
        WorkHit hit,
        IReadOnlyList<AuthorDateResidual> residuals,
        RequestContext context,
        QueryExecution execution,
        CancellationToken cancellationToken)
    {
        if (residuals.Count == 0)
        {
            return true;
        }

        var authors = new List<AuthorDto>();
        var faulted = false;
        foreach (var olid in hit.Authors.Select(author => author.Olid).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var author = await GetAuthorAsync(olid, context, execution, cancellationToken).ConfigureAwait(false);
                if (author is null)
                {
                    faulted = true;
                }
                else
                {
                    authors.Add(author);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (ex is HttpRequestException or TimeoutException or InvalidOperationException)
            {
                logger.LogWarning(ex, "Open Library author hydration failed for {AuthorOlid}", olid);
                faulted = true;
            }
        }

        execution.HydrationFailed |= faulted;
        foreach (var residual in residuals)
        {
            var anySatisfied = false;
            var anyUnverifiable = faulted || hit.Authors.Count == 0;
            foreach (var author in authors)
            {
                var candidate = (residual.Field == AuthorDateField.BirthDate ? author.BirthDate : author.DeathDate)
                    .ParseTemporal();
                if (candidate is null)
                {
                    anyUnverifiable = true;
                    continue;
                }

                anySatisfied |= candidate.Compare(residual.Operator, residual.TemporalValues);
            }

            if (anySatisfied)
            {
                if (residual.Negate)
                {
                    execution.Reject(string.Concat("author-", residual.Field));
                    return false;
                }

                continue;
            }

            if (anyUnverifiable)
            {
                execution.HasUnverifiableCriteria = true;
                continue;
            }

            if (!residual.Negate)
            {
                execution.Reject(string.Concat("author-", residual.Field));
                return false;
            }
        }

        return true;
    }

    private ExecutedRequest DescribeRequest(ApiRequest request, string role)
    {
        var offset = int.TryParse(Parameter(request, "offset"), out var parsedOffset) ? parsedOffset : 0;
        var limit = int.TryParse(Parameter(request, "limit"), out var parsedLimit) ? parsedLimit : 0;
        return new ExecutedRequest
        {
            Role = role,
            Endpoint = request.Path,
            Status = "cache_hit",
            Query = Parameter(request, "q"),
            Fields = Parameter(request, "fields"),
            Offset = offset,
            Limit = limit,
            Page = limit > 0 ? offset / limit + 1 : 1
        };
    }

    private void TraceExecution(QueryExecution execution)
    {
        execution.TotalRequestCount = execution.Requests.Count;
        logger.LogTrace(
            "Open Library query execution {Provider} query {QueryId} outcome {OutcomeJson}",
            Constants.ProviderType,
            Activity.Current?.TraceId.ToString() ?? "none",
            JsonSerializer.Serialize(execution, TraceJsonOptions));
    }

    private sealed class QueryExecution
    {
        public List<NodeExecution> Branches { get; } = [];

        public List<ExecutedRequest> Requests { get; } = [];

        public List<string> Warnings { get; } = [];

        public Dictionary<string, int> PredicateRejections { get; } = new(StringComparer.Ordinal);

        public bool Completed { get; set; }

        public bool Failed { get; set; }

        public string? Failure { get; set; }

        public bool Exhausted { get; set; }

        public bool HasMore { get; set; }

        public bool HasUnverifiableCriteria { get; set; }

        public bool HydrationFailed { get; set; }

        public int ReturnedRows { get; set; }

        public int TotalRequestCount { get; set; }

        public string? FinalOutcome { get; set; }

        public int FetchLimit { get; set; } = ScanBatchSize;

        public int FetchOffset { get; set; }

        public bool SingleFetch { get; set; }

        public bool ExactClass { get; set; }

        public bool UpstreamHasMore { get; set; }

        public NodeExecution AddBranch(PlanNode node)
        {
            var branch = new NodeExecution
            {
                Kind = node.Kind,
                State = node.State,
                Reason = node.Reason
            };
            Branches.Add(branch);
            return branch;
        }

        public void Reject(string predicate)
        {
            PredicateRejections.TryGetValue(predicate, out var count);
            PredicateRejections[predicate] = count + 1;
        }
    }

    private sealed class NodeExecution
    {
        public required PlanNodeKind Kind { get; init; }

        public required PlanNodeState State { get; set; }

        public string? Reason { get; set; }

        public int RowsIn { get; set; }

        public int RowsOut { get; set; }

        public bool Exhausted { get; set; }
    }

    private sealed class ExecutedRequest
    {
        public required string Role { get; init; }

        public required string Endpoint { get; init; }

        public required string Status { get; set; }

        public string? Query { get; init; }

        public string? Fields { get; init; }

        public int Offset { get; init; }

        public int Limit { get; init; }

        public int Page { get; init; }

        public int RowsIn { get; set; }

        public int RowsOut { get; set; }

        public long ElapsedMilliseconds { get; set; }
    }
}
