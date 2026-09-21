using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
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
using Magpie.Sdk.Caching;

using Microsoft.Extensions.Logging;

namespace Magpie.Extensions.OpenLibrary.QueryEngine;

internal sealed class QueryExecutor(
    IOpenLibraryClient client,
    ICacheFactory cacheFactory,
    ISettingsResolver settingsResolver,
    ILogger<QueryExecutor> logger) : IQueryExecutor
{
    private sealed class ExecutionState
    {
        private int _nodes;

        public List<string> Warnings { get; } = [];

        public Activity? Query { get; init; }

        public bool Failed { get; set; }

        public bool Exhausted { get; set; }

        public bool HasUnverifiableCriteria { get; set; }

        public bool HydrationFailed { get; set; }

        public int RequestCount { get; set; }

        public long RowsFetched { get; set; }

        public int FetchLimit { get; set; } = ScanBatchSize;

        public int FetchOffset { get; set; }

        public bool SingleFetch { get; set; }

        public bool ExactClass { get; set; }

        public bool UpstreamHasMore { get; set; }

        public int NextNodeId()
        {
            return _nodes++;
        }
    }

    [SuppressMessage("Ordering", "SA1201", Justification = "Nested types come first in this codebase.")]
    private const int ScanBatchSize = 100;

    private const string ExecuteSpanName = "openlibrary.query.execute";
    private const string RequestSpanName = "openlibrary.query.request";
    private const string FetchSpanName = "openlibrary.query.node.fetch";
    private const string FilterSpanName = "openlibrary.query.node.filter";

    private const string TerminalSuccess = "success";
    private const string TerminalEmpty = "empty";
    private const string TerminalPartial = "partial";
    private const string TerminalUnavailable = "unavailable";

    private const string RoleSearch = "search";
    private const string RoleAuthor = "author";
    private const string RoleResolution = "resolution";
    private const string RoleEdition = "edition";

    private const string RegionPage = "page";
    private const string RegionAuthor = "author";
    private const string RegionResolution = "resolution";
    private const string RegionEdition = "edition";

    private static readonly JsonSerializerOptions PlanJsonOptions = new()
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

    private readonly ICache _advancedPageCache = cacheFactory.GetOrCreate(
        "openlibrary-advanced-pages",
        () => new CachePolicy
        {
            Freshness = TimeSpan.FromMinutes(2),
            Retention = TimeSpan.FromMinutes(2),
            MaximumEntries = 256,
            ConcurrencyMode = CacheConcurrencyMode.Pessimistic
        });

    private readonly ICache _authorCache = cacheFactory.GetOrCreate(
        "openlibrary-authors",
        () => new CachePolicy
        {
            Freshness = TimeSpan.FromMinutes(30),
            Retention = TimeSpan.FromMinutes(30),
            MaximumEntries = 256,
            ConcurrencyMode = CacheConcurrencyMode.Pessimistic
        });

    private readonly ICache _editionCache = cacheFactory.GetOrCreate(
        "openlibrary-editions",
        () => new CachePolicy
        {
            Freshness = TimeSpan.FromMinutes(30),
            Retention = TimeSpan.FromMinutes(30),
            MaximumEntries = 256,
            ConcurrencyMode = CacheConcurrencyMode.Pessimistic
        });

    private readonly ICache _negativeCache = cacheFactory.GetOrCreate(
        "openlibrary-negative-identities",
        () => new CachePolicy
        {
            Freshness = TimeSpan.FromMinutes(2),
            Retention = TimeSpan.FromMinutes(2),
            MaximumEntries = 128,
            ConcurrencyMode = CacheConcurrencyMode.Pessimistic
        });

    private readonly ICache _pageCache = cacheFactory.GetOrCreate(
        "openlibrary-pages",
        () => new CachePolicy
        {
            Freshness = TimeSpan.FromMinutes(5),
            Retention = TimeSpan.FromMinutes(5),
            MaximumEntries = 256,
            ConcurrencyMode = CacheConcurrencyMode.Pessimistic
        });

    private readonly ICache _resolutionCache = cacheFactory.GetOrCreate(
        "openlibrary-resolutions",
        () => new CachePolicy
        {
            Freshness = TimeSpan.FromMinutes(20),
            Retention = TimeSpan.FromMinutes(20),
            MaximumEntries = 512,
            ConcurrencyMode = CacheConcurrencyMode.Pessimistic
        });

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
            Warnings = [],
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
        using var activity = Telemetry.Source.StartActivity(ExecuteSpanName);
        var stopwatch = Stopwatch.StartNew();
        var execution = new ExecutionState { Query = activity };
        DescribePlan(activity, plan);
        if (plan.Root.State != PlanNodeState.Ready || plan.Advanced && plan.MaxResults <= 0)
        {
            execution.Exhausted = true;
            Complete(activity, plan, execution, stopwatch, surviving: 0, hasMore: false, TerminalEmpty);
            return Empty(plan);
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
            execution.Exhausted = true;
            activity?.SetStatus(
                ActivityStatusCode.Error,
                "The requested page offset exceeds the Open Library API integer range.");
            Complete(activity, plan, execution, stopwatch, surviving: 0, hasMore: false, TerminalUnavailable);
            return new QueryResult
            {
                Hits = [],
                PageInfo = PageInfo(plan.Page, hasMore: false),
                Warnings = [.. execution.Warnings],
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
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TimeoutException or InvalidOperationException)
        {
            logger.LogWarning(ex, "Open Library search execution failed");
            execution.Failed = true;
            activity?.SetStatus(ActivityStatusCode.Error, ex.GetType().Name);
        }

        var hasMore = hits.Count > desired ||
            execution.UpstreamHasMore ||
            execution.Failed && !execution.Exhausted;
        if (hits.Count > desired)
        {
            hits.RemoveAt(desired);
        }

        var warnings = new List<string>();
        if (execution.Failed || execution.HydrationFailed)
        {
            warnings.Add("Open Library could not complete the remote search.");
        }

        warnings.AddRange(execution.Warnings);
        Complete(
            activity,
            plan,
            execution,
            stopwatch,
            hits.Count,
            hasMore,
            execution.Failed
                ? TerminalPartial
                : hits.Count == 0 ? TerminalEmpty : TerminalSuccess);
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
        using var activity = Telemetry.Source.StartActivity(ExecuteSpanName);
        var stopwatch = Stopwatch.StartNew();
        var execution = new ExecutionState { Query = activity };
        DescribePlan(activity, plan);
        if (plan.Root.State != PlanNodeState.Ready ||
            plan.ResolutionSource is null ||
            plan.ResolutionValue is null)
        {
            execution.Exhausted = true;
            Complete(activity, plan, execution, stopwatch, surviving: 0, hasMore: false, TerminalEmpty);
            return new WorkResolution { Kind = WorkResolutionKind.NotFound };
        }

        var fetch = FindFetch(plan.Root)
            ?? throw new InvalidOperationException("Open Library resolution requires a fetch node.");
        execution.FetchLimit = string.Equals(
            plan.ResolutionSource,
            Constants.ProviderType,
            StringComparison.OrdinalIgnoreCase)
                ? 2
                : 5;
        var request = Build(fetch, execution.FetchLimit, 0);
        var result = await ResolveCachedAsync(plan, request, execution, cancellationToken).ConfigureAwait(false);
        var surviving = result.Hit is null ? 0 : 1;
        Complete(
            activity,
            plan,
            execution,
            stopwatch,
            surviving,
            hasMore: false,
            surviving == 0 ? TerminalEmpty : TerminalSuccess);
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
        using var activity = Telemetry.Source.StartActivity(RequestSpanName);
        DescribeRequest(activity, request, RoleEdition);
        var cacheHit = true;
        try
        {
            var negativeKey = string.Concat("edition|", request.CacheKey);
            var lookup = _negativeCache.TryGet<bool>(negativeKey, out _)
                ? new EditionLookup { Found = false }
                : await _editionCache.GetOrAddAsync(
                    request.CacheKey,
                    async operationToken =>
                    {
                        cacheHit = false;
                        var edition = await client.GetJsonAsync<EditionDto>(
                            request,
                            context,
                            operationToken).ConfigureAwait(false);
                        return new EditionLookup { Found = edition is not null, Edition = edition };
                    },
                    cancellationToken).ConfigureAwait(false);
            if (!lookup.Found)
            {
                _editionCache.Remove(request.CacheKey);
                _negativeCache.Set(negativeKey, true);
            }

            activity?.SetTag("magpie.request.rows", lookup.Found ? 1 : 0);
            return lookup.Edition;
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.GetType().Name);
            throw;
        }
        finally
        {
            CompleteRequest(activity, RoleEdition, RegionEdition, cacheHit);
        }
    }

    private async IAsyncEnumerable<WorkHit> ExecuteNodeAsync(
        PlanNode node,
        RequestContext context,
        ExecutionState execution,
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
        ExecutionState execution,
        long? demand,
        long? pageSizeHint,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (node.State != PlanNodeState.Ready)
        {
            SkipNode(execution, node);
            yield break;
        }

        using var activity = Telemetry.Source.StartActivity(FetchSpanName);
        activity?.SetTag("magpie.query.node.id", execution.NextNodeId());
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
        var rowsIn = 0L;
        try
        {
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
                rowsIn += page.SourceRowCount;
                execution.RowsFetched += page.SourceRowCount;
                var nextOffset = checked((long)offset + page.SourceRowCount);
                var exhausted = page.SourceRowCount == 0 ||
                    page.SourceRowCount < requestLimit ||
                    page.NumFoundExact && nextOffset >= page.NumFound;
                execution.UpstreamHasMore = !exhausted;
                execution.Exhausted = exhausted;
                foreach (var hit in page.Hits)
                {
                    if (!seen.Add(hit.WorkOlid))
                    {
                        continue;
                    }

                    emitted++;
                    yield return hit;
                    if (demand is { } maximum && emitted >= maximum)
                    {
                        yield break;
                    }
                }

                if (exhausted || singleFetch)
                {
                    yield break;
                }

                if (nextOffset > int.MaxValue)
                {
                    execution.Failed = true;
                    execution.Warnings.Add(
                        "Open Library exhausted its source offset range before completing the search.");
                    activity?.SetStatus(
                        ActivityStatusCode.Error,
                        "The next Open Library source offset exceeds the API integer range.");
                    yield break;
                }

                offset = (int)nextOffset;
            }
        }
        finally
        {
            activity?.SetTag("magpie.query.node.rows_in", rowsIn);
            activity?.SetTag("magpie.query.node.rows_out", emitted);
        }
    }

    private async IAsyncEnumerable<WorkHit> ExecuteFilterAsync(
        PlanNode node,
        RequestContext context,
        ExecutionState execution,
        long? demand,
        long? pageSizeHint,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (node.State != PlanNodeState.Ready || node.Children.Count != 1)
        {
            SkipNode(execution, node);
            yield break;
        }

        using var activity = Telemetry.Source.StartActivity(FilterSpanName);
        activity?.SetTag("magpie.query.node.id", execution.NextNodeId());
        var groups = EffectiveFilterGroups(node);
        var rejections = new Dictionary<string, int>(StringComparer.Ordinal);
        var emitted = 0L;
        var rowsIn = 0L;
        try
        {
            await foreach (var hit in ExecuteNodeAsync(
                node.Children[0],
                context,
                execution,
                null,
                pageSizeHint,
                cancellationToken).ConfigureAwait(false))
            {
                rowsIn++;
                var violated = false;
                for (var index = 0; index < groups.Count; index++)
                {
                    var outcome = Evaluate(hit, groups[index]);
                    if (outcome == FilterOutcome.Violated)
                    {
                        Reject(rejections, string.Concat("group-", index.ToString(CultureInfo.InvariantCulture)));
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
                        rejections,
                        cancellationToken).ConfigureAwait(false))
                {
                    continue;
                }

                emitted++;
                yield return hit;
                if (demand is { } maximum && emitted >= maximum)
                {
                    yield break;
                }
            }
        }
        finally
        {
            activity?.SetTag("magpie.query.node.rows_in", rowsIn);
            activity?.SetTag("magpie.query.node.rows_out", emitted);
            DescribeRejections(activity, rejections);
        }
    }

    private void Reject(Dictionary<string, int> rejections, string criterion)
    {
        rejections.TryGetValue(criterion, out var count);
        rejections[criterion] = count + 1;
    }

    private void SkipNode(ExecutionState execution, PlanNode node)
    {
        execution.Query?.AddEvent(new ActivityEvent(
            "node.skipped",
            tags: new ActivityTagsCollection
            {
                { "magpie.query.node.id", execution.NextNodeId() },
                { "magpie.query.node.kind", node.Kind.ToString().ToLowerInvariant() },
                { "magpie.query.node.state", node.State.ToString().ToLowerInvariant() },
                { "magpie.query.node.reason", node.Reason }
            }));
    }

    private void DescribeRejections(Activity? activity, Dictionary<string, int> rejections)
    {
        if (activity is null || rejections.Count == 0)
        {
            return;
        }

        var tags = new ActivityTagsCollection();
        foreach (var rejection in rejections)
        {
            tags.Add(rejection.Key, rejection.Value);
        }

        activity.AddEvent(new ActivityEvent("rejections", tags: tags));
    }

    private async Task<WorkResolution> ResolveCachedAsync(
        QueryPlan plan,
        ApiRequest request,
        ExecutionState execution,
        CancellationToken cancellationToken)
    {
        using var activity = Telemetry.Source.StartActivity(RequestSpanName);
        DescribeRequest(activity, request, RoleResolution);
        var cacheHit = true;
        try
        {
            var negativeKey = string.Concat("work|", request.CacheKey);
            var result = _negativeCache.TryGet<bool>(negativeKey, out _)
                ? new WorkResolution { Kind = WorkResolutionKind.NotFound }
                : await _resolutionCache.GetOrAddAsync(
                    request.CacheKey,
                    operationToken =>
                    {
                        cacheHit = false;
                        return new ValueTask<WorkResolution>(ResolveUncachedAsync(plan, execution, operationToken));
                    },
                    cancellationToken).ConfigureAwait(false);
            if (result.Kind == WorkResolutionKind.NotFound)
            {
                _resolutionCache.Remove(request.CacheKey);
                _negativeCache.Set(negativeKey, true);
            }

            activity?.SetTag("magpie.request.rows", result.Hit is null ? 0 : 1);
            return result;
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.GetType().Name);
            throw;
        }
        finally
        {
            CompleteRequest(activity, RoleResolution, RegionResolution, cacheHit);
        }
    }

    private async Task<WorkResolution> ResolveUncachedAsync(
        QueryPlan plan,
        ExecutionState execution,
        CancellationToken cancellationToken)
    {
        var context = await settingsResolver.GetRequestContextAsync(cancellationToken).ConfigureAwait(false);
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

        if (hits.Count == 0)
        {
            return new WorkResolution { Kind = WorkResolutionKind.NotFound };
        }

        var distinct = hits.GroupBy(hit => hit.WorkOlid, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();
        var ambiguous = distinct.Length > 1;
        return new WorkResolution
        {
            Kind = ambiguous ? WorkResolutionKind.Ambiguous : WorkResolutionKind.Resolved,
            Hit = distinct[0]
        };
    }

    private async Task<AuthorDto?> GetAuthorAsync(
        string authorOlid,
        RequestContext context,
        ExecutionState? execution,
        CancellationToken cancellationToken)
    {
        var request = BuildAuthor(authorOlid);
        using var activity = Telemetry.Source.StartActivity(RequestSpanName);
        DescribeRequest(activity, request, RoleAuthor);
        if (execution is not null)
        {
            execution.RequestCount++;
        }

        var cacheHit = true;
        try
        {
            var negativeKey = string.Concat("author|", request.CacheKey);
            var lookup = _negativeCache.TryGet<bool>(negativeKey, out _)
                ? new AuthorLookup { Found = false }
                : await _authorCache.GetOrAddAsync(
                    request.CacheKey,
                    async operationToken =>
                    {
                        cacheHit = false;
                        var author = await client.GetJsonAsync<AuthorDto>(
                            request,
                            context,
                            operationToken).ConfigureAwait(false);
                        return new AuthorLookup { Found = author is not null, Author = author };
                    },
                    cancellationToken).ConfigureAwait(false);
            if (!lookup.Found)
            {
                _authorCache.Remove(request.CacheKey);
                _negativeCache.Set(negativeKey, true);
            }

            activity?.SetTag("magpie.request.rows", lookup.Found ? 1 : 0);
            return lookup.Author;
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.GetType().Name);
            throw;
        }
        finally
        {
            CompleteRequest(activity, RoleAuthor, RegionAuthor, cacheHit);
        }
    }

    private async Task<SourcePage> LoadSourcePageAsync(
        ApiRequest request,
        RequestContext context,
        ExecutionState execution,
        bool exactClass,
        CancellationToken cancellationToken)
    {
        using var activity = Telemetry.Source.StartActivity(RequestSpanName);
        DescribeRequest(activity, request, RoleSearch);
        execution.RequestCount++;
        var cacheHit = true;
        try
        {
            var page = await (exactClass ? _pageCache : _advancedPageCache).GetOrAddAsync(
                request.CacheKey,
                async operationToken =>
                {
                    cacheHit = false;
                    var response = await client.GetJsonAsync<SearchResponseDto>(
                        request,
                        context,
                        operationToken).ConfigureAwait(false)
                        ?? throw new InvalidOperationException("Open Library returned no search response.");
                    return response.ToSourcePage();
                },
                cancellationToken).ConfigureAwait(false);
            activity?.SetTag("magpie.request.rows", page.SourceRowCount);
            return page;
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.GetType().Name);
            throw;
        }
        finally
        {
            CompleteRequest(activity, RoleSearch, RegionPage, cacheHit);
        }
    }

    private async Task<bool> SatisfiesResidualsAsync(
        WorkHit hit,
        IReadOnlyList<AuthorDateResidual> residuals,
        RequestContext context,
        ExecutionState execution,
        Dictionary<string, int> rejections,
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
                    Reject(rejections, string.Concat("author-", residual.Field.ToString()));
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
                Reject(rejections, string.Concat("author-", residual.Field.ToString()));
                return false;
            }
        }

        return true;
    }

    private void DescribeRequest(Activity? activity, ApiRequest request, string role)
    {
        activity?.SetTag("magpie.request.role", role);
        activity?.SetTag("magpie.request.target", string.Concat(OpenLibraryClient.DefaultBaseUrl, request.CacheKey));
    }

    private void CompleteRequest(Activity? activity, string role, string region, bool cacheHit)
    {
        activity?.SetTag("magpie.cache.hit", cacheHit);
        Telemetry.Requests.Add(1, new TagList
        {
            { "provider", Constants.ProviderType },
            { "request_role", role },
            { "cache_hit", cacheHit }
        });
        Telemetry.Cache.Add(1, new TagList
        {
            { "provider", Constants.ProviderType },
            { "cache_region", region },
            { "outcome", cacheHit ? "hit" : "miss" }
        });
    }

    private void DescribePlan(Activity? activity, QueryPlan plan)
    {
        if (activity is null)
        {
            return;
        }

        activity.SetTag("magpie.query.fingerprint", Fingerprint(plan));
        if (!activity.IsAllDataRequested)
        {
            return;
        }

        activity.AddEvent(new ActivityEvent(
            "plan",
            tags: new ActivityTagsCollection
            {
                { "magpie.query.plan_json", JsonSerializer.Serialize(plan, PlanJsonOptions) },
                { "magpie.query.anchor_kind", plan.AnchorKind },
                { "magpie.query.known_empty", plan.Root.State == PlanNodeState.Empty }
            }));
    }

    private void Complete(
        Activity? activity,
        QueryPlan plan,
        ExecutionState execution,
        Stopwatch stopwatch,
        int surviving,
        bool hasMore,
        string terminalReason)
    {
        activity?.SetTag("magpie.query.rows_fetched", execution.RowsFetched);
        activity?.SetTag("magpie.query.rows_surviving", surviving);
        activity?.SetTag("magpie.query.request_count", execution.RequestCount);
        activity?.SetTag("magpie.query.has_unverifiable_criteria", execution.HasUnverifiableCriteria);
        activity?.SetTag("magpie.query.has_more", hasMore);
        activity?.SetTag("magpie.query.terminal_reason", terminalReason);
        Telemetry.QueryDuration.Record(stopwatch.Elapsed.TotalMilliseconds, new TagList
        {
            { "provider", Constants.ProviderType },
            { "content_type", plan.Hierarchy.ContentTypeSlug },
            { "terminal_reason", terminalReason }
        });
        Telemetry.Results.Record(surviving, new TagList
        {
            { "provider", Constants.ProviderType },
            { "content_type", plan.Hierarchy.ContentTypeSlug }
        });
    }

    private string Fingerprint(QueryPlan plan)
    {
        var builder = new StringBuilder();
        builder.Append(plan.AnchorKind)
            .Append('|')
            .Append(plan.Advanced ? '1' : '0')
            .Append('|')
            .Append(plan.Page.ToString(CultureInfo.InvariantCulture))
            .Append('|')
            .Append(plan.MaxResults.ToString(CultureInfo.InvariantCulture));
        AppendNode(plan.Root, builder);
        var hash = 14695981039346656037UL;
        foreach (var character in builder.ToString())
        {
            hash = (hash ^ character) * 1099511628211UL;
        }

        return hash.ToString("x16", CultureInfo.InvariantCulture);
    }

    private void AppendNode(PlanNode node, StringBuilder builder)
    {
        builder.Append('|').Append(node.Kind.ToString()).Append(':').Append(node.State.ToString());
        var terms = node.SourceTerms.Concat(node.SourceGroups
            .SelectMany(group => group.Alternatives)
            .SelectMany(alternative => alternative.Terms));
        foreach (var term in terms)
        {
            builder.Append(';')
                .Append(term.Field)
                .Append(term.Negate ? "!=" : "=")
                .Append(string.Join(",", term.Values));
        }

        foreach (var filter in node.Filters)
        {
            builder.Append('#').Append(filter.Field.ToString()).Append(':').Append(filter.Operator.ToString());
        }

        foreach (var child in node.Children)
        {
            AppendNode(child, builder);
        }
    }
}
