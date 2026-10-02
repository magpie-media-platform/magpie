using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Filtering;
using Magpie.Sdk.Content;

namespace Magpie.Extensions.OpenLibrary.QueryEngine;

internal sealed class QueryPlanner : IQueryPlanner
{
    private const string SubtitleKind = "subtitle";
    private const string AlternativeTitleKind = "alternative_title";

    private const string AnchorNone = "none";
    private const string AnchorTitle = "title";
    private const string AnchorCriteria = "criteria";
    private const string AnchorWorkKey = "work_key";
    private const string AnchorIsbn = "isbn";

    private const string RefusalKnownEmpty = "known_empty";
    private const string RefusalUnbounded = "unbounded";
    private const string RefusalIdentityEmpty = "identity_empty";

    private static readonly IReadOnlyList<string> DetailProjectionFields =
        ["alternative_title", "number_of_pages_median", "subtitle"];

    private const string CanonicalReferenceKind = "canonical";
    private const string CanonicalReferenceDisplayName = "Open Library";
    private const string CanonicalWorkUrlPrefix = "https://openlibrary.org/works/";

    private static readonly IReadOnlyList<(string Kind, QueryField Field, string SourceField)>
        SubjectFacets =
        [
            (Constants.PersonSubjectKind, QueryField.Person, "person"),
            (Constants.PlaceSubjectKind, QueryField.Place, "place"),
            (Constants.TimeSubjectKind, QueryField.Time, "time")
        ];

    private static readonly IReadOnlyList<string> ProviderIdentities = [Constants.ProviderType];

    public QueryPlan Plan(QueryRequest request)
    {
        var plan = request.Intent switch
        {
            QueryIntent.OrdinarySearch => PlanOrdinary(request),
            QueryIntent.AdvancedSearch => PlanAdvanced(request),
            QueryIntent.WorkResolution => PlanResolution(request),
            _ => Empty(request, RefusalKnownEmpty)
        };
        var anchorKind = AnchorKind(request, plan);
        plan = plan with
        {
            CandidateLanguagePreferences = CandidateLanguagePreferences(request),
            AnchorKind = anchorKind
        };
        Telemetry.Plans.Add(1, new TagList
        {
            { "provider", Constants.ProviderType },
            { "content_type", request.Hierarchy.ContentTypeSlug },
            { "anchor_kind", anchorKind }
        });
        return plan;
    }

    private string AnchorKind(QueryRequest request, QueryPlan plan)
    {
        if (plan.Root.State != PlanNodeState.Ready)
        {
            return AnchorNone;
        }

        return request.Intent switch
        {
            QueryIntent.OrdinarySearch => AnchorTitle,
            QueryIntent.AdvancedSearch => AnchorCriteria,
            QueryIntent.WorkResolution => string.Equals(
                plan.ResolutionSource,
                Constants.IsbnSource,
                StringComparison.OrdinalIgnoreCase)
                    ? AnchorIsbn
                    : AnchorWorkKey,
            _ => AnchorNone
        };
    }

    private IReadOnlyList<string> CandidateLanguagePreferences(QueryRequest request)
    {
        var criteria = request.Criteria?.MergeMetadata() ?? request.MetadataCriteria;
        var preferences = new List<string>();
        var configured = request.LanguagePreference.NormalizeLanguage();
        if (configured is not null)
        {
            preferences.Add(configured);
        }

        foreach (var criterion in criteria?.Language.And ?? [])
        {
            var (op, negate) = InterpretOperator(criterion.Operator, criterion.Negate);
            if (negate || op is not (CriteriaValueOperator.Equals or CriteriaValueOperator.In))
            {
                continue;
            }

            foreach (var language in Strings(criterion)
                .Select(value => value.NormalizeLanguage())
                .Where(value => value is not null)
                .Cast<string>())
            {
                preferences.Add(language);
            }
        }

        return [.. preferences.Distinct(StringComparer.OrdinalIgnoreCase)];
    }

    private QueryPlan PlanOrdinary(QueryRequest request)
    {
        var value = CleanText(request.Value);
        return value is null
            ? Empty(request, RefusalIdentityEmpty)
            : new QueryPlan
            {
                Root = new()
                {
                    Kind = PlanNodeKind.Fetch,
                    SourceTerms = [Term("title", [value])],
                    SourceFields = ["title"],
                    LanguagePreference = request.LanguagePreference.NormalizeLanguagePreference(),
                    PagingMode = FetchPagingMode.Direct
                },
                Hierarchy = request.Hierarchy,
                Page = Math.Max(1, request.Page),
                MaxResults = QueryLimits.LogicalPageSize
            };
    }

    private QueryPlan PlanResolution(QueryRequest request)
    {
        if (string.Equals(request.Source, Constants.ProviderType, StringComparison.OrdinalIgnoreCase))
        {
            var workOlid = request.Value.NormalizeOlid('W');
            return workOlid is null
                ? Empty(request, RefusalIdentityEmpty)
                : new QueryPlan
                {
                    Root = new()
                    {
                        Kind = PlanNodeKind.Fetch,
                        SourceTerms = [Term("key", [string.Concat("/works/", workOlid)])],
                        SourceFields = ["key"],
                        RequiredFields = DetailProjectionFields,
                        LanguagePreference = request.LanguagePreference.NormalizeLanguagePreference(),
                        PagingMode = FetchPagingMode.Direct
                    },
                    Hierarchy = request.Hierarchy,
                    ResolutionSource = Constants.ProviderType,
                    ResolutionValue = workOlid
                };
        }

        if (string.Equals(request.Source, Constants.IsbnSource, StringComparison.OrdinalIgnoreCase))
        {
            var isbn = request.Value.NormalizeIsbn();
            return isbn is null
                ? Empty(request, RefusalIdentityEmpty)
                : new QueryPlan
                {
                    Root = new()
                    {
                        Kind = PlanNodeKind.Fetch,
                        SourceTerms = [Term("isbn", [isbn])],
                        SourceFields = ["isbn"],
                        RequiredFields = DetailProjectionFields,
                        LanguagePreference = request.LanguagePreference.NormalizeLanguagePreference(),
                        PagingMode = FetchPagingMode.Direct
                    },
                    Hierarchy = request.Hierarchy,
                    ResolutionSource = Constants.IsbnSource,
                    ResolutionValue = isbn
                };
        }

        return Empty(request, RefusalIdentityEmpty);
    }

    private PlannedCriterion PlanTitle(PropertyCriterion<string> criterion)
    {
        var (op, negate) = InterpretOperator(criterion.Operator, criterion.Negate);
        if (op is CriteriaValueOperator.IsNull or CriteriaValueOperator.IsNotNull)
        {
            return (op == CriteriaValueOperator.IsNotNull) ^ negate ? Satisfied() : KnownEmpty();
        }

        if (!IsTextOperator(op))
        {
            return KnownEmpty();
        }

        var values = Strings(criterion);
        if (values.Count == 0)
        {
            return KnownEmpty();
        }

        var filter = StringFilter(QueryField.Title, op, negate, values);
        if (negate)
        {
            return new PlannedCriterion
            {
                Filters = [filter],
                NeedsIndependentSeed = true
            };
        }

        if (op is CriteriaValueOperator.StartsWithString or CriteriaValueOperator.EndsWithString)
        {
            return new PlannedCriterion
            {
                Filters = [filter],
                SourceTerms =
                [
                    WildcardTerm(
                        "title",
                        values,
                        op == CriteriaValueOperator.StartsWithString
                            ? WildcardPlacement.Trailing
                            : WildcardPlacement.Leading)
                ],
                RecallBoundedSource = true
            };
        }

        return new PlannedCriterion
        {
            Filters = [filter],
            SourceTerms = [Term("title", values)],
            RecallBoundedSource = op == CriteriaValueOperator.ContainsString
        };
    }

    private PlannedCriterion PlanOriginalTitle(PropertyCriterion<string> criterion)
    {
        var (op, negate) = InterpretOperator(criterion.Operator, criterion.Negate);
        if (op is CriteriaValueOperator.IsNull or CriteriaValueOperator.IsNotNull)
        {
            return (op == CriteriaValueOperator.IsNotNull) ^ negate ? Satisfied() : KnownEmpty();
        }

        if (!IsTextOperator(op))
        {
            return KnownEmpty();
        }

        var values = Strings(criterion);
        if (values.Count == 0)
        {
            return KnownEmpty();
        }

        var filter = StringFilter(QueryField.OriginalTitle, op, negate, values);
        if (negate)
        {
            return new PlannedCriterion
            {
                Filters = [filter],
                NeedsIndependentSeed = true
            };
        }

        if (op is CriteriaValueOperator.StartsWithString or CriteriaValueOperator.EndsWithString)
        {
            return new PlannedCriterion
            {
                Filters = [filter],
                SourceTerms =
                [
                    WildcardTerm(
                        "title",
                        values,
                        op == CriteriaValueOperator.StartsWithString
                            ? WildcardPlacement.Trailing
                            : WildcardPlacement.Leading)
                ],
                RecallBoundedSource = true
            };
        }

        return new PlannedCriterion
        {
            Filters = [filter],
            SourceTerms = [Term("title", values)],
            RecallBoundedSource = op == CriteriaValueOperator.ContainsString
        };
    }

    private PlannedCriterion PlanLanguage(PropertyCriterion<string> criterion)
    {
        var (op, negate) = InterpretOperator(criterion.Operator, criterion.Negate);
        if (op is CriteriaValueOperator.IsNull or CriteriaValueOperator.IsNotNull)
        {
            return new PlannedCriterion
            {
                Filters = [new QueryFilter { Field = QueryField.Language, Operator = op, Negate = negate }],
                NeedsIndependentSeed = true
            };
        }

        if (op is not CriteriaValueOperator.Equals and not CriteriaValueOperator.In)
        {
            return KnownEmpty();
        }

        var values = Strings(criterion)
            .Select(value => value.NormalizeLanguage())
            .Where(value => value is not null)
            .Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (values.Length == 0)
        {
            return negate ? Satisfied() : KnownEmpty();
        }

        var filter = StringFilter(QueryField.Language, op, negate, values);
        return negate
            ? new PlannedCriterion
            {
                Filters = [filter],
                NeedsIndependentSeed = true
            }
            : new PlannedCriterion
            {
                Filters = [filter],
                SourceTerms = [Term("language", values)]
            };
    }

    private PlannedCriterion PlanSelfIdentity(PropertyCriterion<string> criterion, IReadOnlyList<string> identities)
    {
        var (op, negate) = InterpretOperator(criterion.Operator, criterion.Negate);
        if (op is CriteriaValueOperator.IsNull or CriteriaValueOperator.IsNotNull)
        {
            return (op == CriteriaValueOperator.IsNotNull) ^ negate ? Satisfied() : KnownEmpty();
        }

        if (!IsTextOperator(op))
        {
            return KnownEmpty();
        }

        var values = Strings(criterion);
        if (values.Count == 0)
        {
            return KnownEmpty();
        }

        return identities.Any(identity => MatchesConstant(identity, op, values) ^ negate)
            ? Satisfied()
            : KnownEmpty();
    }

    private PlannedCriterion Satisfied()
    {
        return new PlannedCriterion { NeedsIndependentSeed = true };
    }

    private PlannedCriterion KnownEmpty()
    {
        return new PlannedCriterion { MatchesNothing = true };
    }

    private PlannedCriterion PlanAbsentProperty<T>(PropertyCriterion<T> criterion)
    {
        var (op, negate) = InterpretOperator(criterion.Operator, criterion.Negate);
        if (op is CriteriaValueOperator.IsNull or CriteriaValueOperator.IsNotNull)
        {
            return (op == CriteriaValueOperator.IsNull) ^ negate
                ? Satisfied()
                : KnownEmpty();
        }

        return negate ? Satisfied() : KnownEmpty();
    }

    private void PlanAbsentCollection<T>(CollectionCriteria<T> criteria, List<PlannedCriterion> planned)
    {
        foreach (var _ in criteria.Some)
        {
            planned.Add(KnownEmpty());
        }

        foreach (var _ in criteria.None)
        {
            planned.Add(Satisfied());
        }
    }

    private bool MatchesConstant(string identity, CriteriaValueOperator op, IReadOnlyList<string> values)
    {
        foreach (var value in values)
        {
            var matched = op switch
            {
                CriteriaValueOperator.Equals or CriteriaValueOperator.In =>
                    string.Equals(identity, value, StringComparison.OrdinalIgnoreCase),
                CriteriaValueOperator.ContainsString =>
                    identity.Contains(value, StringComparison.OrdinalIgnoreCase),
                CriteriaValueOperator.StartsWithString =>
                    identity.StartsWith(value, StringComparison.OrdinalIgnoreCase),
                CriteriaValueOperator.EndsWithString =>
                    identity.EndsWith(value, StringComparison.OrdinalIgnoreCase),
                CriteriaValueOperator.ContainsWord => Regex.IsMatch(
                    identity,
                    string.Concat("(?<![\\p{L}\\p{N}])", Regex.Escape(value), "(?![\\p{L}\\p{N}])"),
                    RegexOptions.CultureInvariant | RegexOptions.IgnoreCase),
                _ => false
            };
            if (matched)
            {
                return true;
            }
        }

        return false;
    }

    private PlannedCriterion PlanOverview(PropertyCriterion<string> criterion)
    {
        var (op, negate) = InterpretOperator(criterion.Operator, criterion.Negate);
        if (op is CriteriaValueOperator.IsNull or CriteriaValueOperator.IsNotNull)
        {
            return new PlannedCriterion
            {
                Filters = [StringFilter(QueryField.Overview, op, negate, [])],
                NeedsIndependentSeed = true
            };
        }

        if (!IsTextOperator(op))
        {
            return KnownEmpty();
        }

        var values = Strings(criterion);
        if (values.Count == 0)
        {
            return KnownEmpty();
        }

        return new PlannedCriterion
        {
            Filters = [StringFilter(QueryField.Overview, op, negate, values)],
            NeedsIndependentSeed = true
        };
    }

    private PlannedCriterion PlanPublishDate(PropertyCriterion<TemporalValue> criterion)
    {
        var (op, negate) = InterpretOperator(criterion.Operator, criterion.Negate);
        if (op is CriteriaValueOperator.IsNull or CriteriaValueOperator.IsNotNull)
        {
            return new PlannedCriterion
            {
                Filters = [new QueryFilter { Field = QueryField.PublishDate, Operator = op, Negate = negate }],
                NeedsIndependentSeed = true
            };
        }

        if (op is not (CriteriaValueOperator.Equals
            or CriteriaValueOperator.In
            or CriteriaValueOperator.GreaterThan
            or CriteriaValueOperator.GreaterThanOrEqual
            or CriteriaValueOperator.LessThan
            or CriteriaValueOperator.LessThanOrEqual))
        {
            return KnownEmpty();
        }

        var values = TemporalValues(criterion);
        if (values.Count == 0)
        {
            return KnownEmpty();
        }

        var filter = new QueryFilter
        {
            Field = QueryField.PublishDate,
            Operator = op,
            TemporalValues = values,
            Negate = negate
        };
        if (negate)
        {
            return new PlannedCriterion
            {
                Filters = [filter],
                NeedsIndependentSeed = true
            };
        }

        var term = DateTerm(op, values);
        return new PlannedCriterion
        {
            Filters = [filter],
            SourceTerms = term is null ? [] : [term],
            NeedsIndependentSeed = term is null
        };
    }

    private PlannedCriterion PlanGenre(IReadOnlyList<GenreCriteria> alternatives, bool excluded)
    {
        var filterAlternatives = new List<FilterAlternative>();
        var sourceAlternatives = new List<SourceAlternative>();
        var partial = false;
        var relevanceBounded = false;
        var needsIndependentSeed = excluded;
        foreach (var alternative in alternatives)
        {
            var criteria = alternative.Name.And.ToArray();
            if (criteria.Length == 0 ||
                criteria.Any(criterion =>
                    !IsGenreOperator(InterpretOperator(criterion.Operator, criterion.Negate).Operator)))
            {
                partial = true;
                continue;
            }

            var filters = new List<QueryFilter>();
            var terms = new List<SourceTerm>();
            var usable = true;
            foreach (var criterion in criteria)
            {
                var (op, negate) = InterpretOperator(criterion.Operator, criterion.Negate);
                var values = Strings(criterion);
                if (values.Count == 0)
                {
                    usable = false;
                    break;
                }

                filters.Add(new QueryFilter
                {
                    Field = QueryField.Genre,
                    Operator = op,
                    Values = values,
                    Negate = negate
                });
                if (negate)
                {
                    continue;
                }

                if (op is CriteriaValueOperator.ContainsWord or CriteriaValueOperator.ContainsString)
                {
                    terms.Add(Term("subject", values));
                    relevanceBounded = true;
                    continue;
                }

                var normalized = values.Select(value => value.NormalizeSubjectKey())
                    .Where(value => value.Length > 0)
                    .Distinct(StringComparer.Ordinal)
                    .ToArray();
                if (normalized.Length == 0)
                {
                    usable = false;
                    break;
                }

                terms.Add(Term("subject_key", normalized));
            }

            if (!usable)
            {
                partial = true;
                continue;
            }

            filterAlternatives.Add(new FilterAlternative { Filters = filters, AlignGenres = true });
            if (terms.Count == 0)
            {
                needsIndependentSeed = true;
            }
            else
            {
                sourceAlternatives.Add(new SourceAlternative { Terms = terms });
            }
        }

        if (filterAlternatives.Count == 0)
        {
            return KnownEmpty();
        }

        if (excluded)
        {
            sourceAlternatives.Clear();
        }

        if (sourceAlternatives.Count != filterAlternatives.Count)
        {
            sourceAlternatives.Clear();
            needsIndependentSeed = true;
        }

        var filtersFlat = filterAlternatives.SelectMany(alternative => alternative.Filters).ToArray();
        var sourceTerms = sourceAlternatives.SelectMany(alternative => alternative.Terms).ToArray();
        var sourceGroups = sourceAlternatives.Count == 0
            ? []
            : new[] { new SourceGroup { Alternatives = sourceAlternatives } };
        return new PlannedCriterion
        {
            Filters = filtersFlat,
            FilterGroups = [new FilterGroup { Alternatives = filterAlternatives, Negate = excluded }],
            SourceTerms = sourceTerms,
            SourceGroups = sourceGroups,
            NeedsIndependentSeed = needsIndependentSeed,
            RecallBoundedSource = relevanceBounded,
            CannotFulfill = partial
        };
    }

    private PlannedCriterion PlanExternalIds(IReadOnlyList<ExternalIdCriteria> alternatives)
    {
        var buckets = new Dictionary<(QueryField Field, string SourceField), List<string>>();
        var partial = false;
        foreach (var alternative in alternatives)
        {
            if (!TryReadExternalId(alternative, out var source, out var identifierValues))
            {
                partial = true;
                continue;
            }

            foreach (var value in identifierValues)
            {
                ClassifyIdentifier(source, value, buckets, ref partial);
            }
        }

        foreach (var key in buckets.Keys.ToArray())
        {
            buckets[key] = [.. buckets[key].Distinct(StringComparer.OrdinalIgnoreCase)];
        }

        if (buckets.Count == 0)
        {
            return KnownEmpty();
        }

        var filterAlternatives = buckets.Select(pair => new FilterAlternative
        {
            Filters =
            [
                new QueryFilter
                {
                    Field = pair.Key.Field,
                    Operator = pair.Value.Count == 1 ? CriteriaValueOperator.Equals : CriteriaValueOperator.In,
                    Values = pair.Value
                }
            ]
        }).ToArray();
        var sourceAlternatives = buckets.Select(pair => new SourceAlternative
        {
            Terms = [Term(pair.Key.SourceField, IdentifierSourceValues(pair.Key.Field, pair.Value))]
        }).ToArray();
        return new PlannedCriterion
        {
            Filters = [.. filterAlternatives.SelectMany(alternative => alternative.Filters)],
            FilterGroups = [new FilterGroup { Alternatives = filterAlternatives }],
            SourceTerms = [.. sourceAlternatives.SelectMany(alternative => alternative.Terms)],
            SourceGroups = [new SourceGroup { Alternatives = sourceAlternatives }],
            CannotFulfill = partial
        };
    }

    private void ClassifyIdentifier(
        string? source,
        string value,
        Dictionary<(QueryField Field, string SourceField), List<string>> buckets,
        ref bool partial)
    {
        var field = default(QueryField);
        var sourceField = string.Empty;
        var normalized = default(string);
        if (string.Equals(source, Constants.ProviderType, StringComparison.OrdinalIgnoreCase))
        {
            field = QueryField.WorkOlid;
            sourceField = "key";
            normalized = value.NormalizeOlid('W');
        }
        else if (string.Equals(source, Constants.IsbnSource, StringComparison.OrdinalIgnoreCase))
        {
            field = QueryField.Isbn;
            sourceField = "isbn";
            normalized = value.NormalizeIsbn();
        }
        else if (string.Equals(source, Constants.OclcSource, StringComparison.OrdinalIgnoreCase))
        {
            field = QueryField.Oclc;
            sourceField = "oclc";
            normalized = CleanText(value);
        }
        else if (string.Equals(source, Constants.LccnSource, StringComparison.OrdinalIgnoreCase))
        {
            field = QueryField.Lccn;
            sourceField = "lccn";
            normalized = CleanText(value);
        }
        else if (string.Equals(source, Constants.GoodreadsSource, StringComparison.OrdinalIgnoreCase))
        {
            field = QueryField.Goodreads;
            sourceField = "id_goodreads";
            normalized = CleanText(value);
        }
        else if (source is null && value.NormalizeOlid('W') is { } inferredWork)
        {
            field = QueryField.WorkOlid;
            sourceField = "key";
            normalized = inferredWork;
        }
        else if (source is null && value.NormalizeIsbn() is { } inferredIsbn)
        {
            field = QueryField.Isbn;
            sourceField = "isbn";
            normalized = inferredIsbn;
        }
        else
        {
            partial = true;
            return;
        }

        if (normalized is null)
        {
            partial = true;
            return;
        }

        var key = (field, sourceField);
        if (!buckets.TryGetValue(key, out var values))
        {
            values = [];
            buckets[key] = values;
        }

        values.Add(normalized);
    }

    private IReadOnlyList<string> IdentifierSourceValues(QueryField field, IReadOnlyList<string> values)
    {
        return field == QueryField.WorkOlid
            ? [.. values.Select(value => string.Concat("/works/", value))]
            : values;
    }

    private PlannedCriterion PlanExcludedExternalId(ExternalIdCriteria criterion)
    {
        if (!TryReadExternalId(criterion, out var source, out var values))
        {
            return KnownEmpty();
        }

        var buckets = new Dictionary<(QueryField Field, string SourceField), List<string>>();
        var partial = false;
        foreach (var value in values)
        {
            ClassifyIdentifier(source, value, buckets, ref partial);
        }

        foreach (var key in buckets.Keys.ToArray())
        {
            buckets[key] = [.. buckets[key].Distinct(StringComparer.OrdinalIgnoreCase)];
        }

        if (buckets.Count == 0)
        {
            return KnownEmpty();
        }

        var filters = new List<QueryFilter>();
        var terms = new List<SourceTerm>();
        foreach (var pair in buckets)
        {
            filters.Add(new QueryFilter
            {
                Field = pair.Key.Field,
                Operator = pair.Value.Count == 1 ? CriteriaValueOperator.Equals : CriteriaValueOperator.In,
                Values = pair.Value,
                Negate = true
            });
            terms.Add(new SourceTerm
            {
                Field = pair.Key.SourceField,
                Values = IdentifierSourceValues(pair.Key.Field, pair.Value),
                Negate = true
            });
        }

        return new PlannedCriterion
        {
            Filters = filters,
            SourceTerms = terms,
            NeedsIndependentSeed = true,
            CannotFulfill = partial
        };
    }

    private void PlanCustomMetadata(
        DictionaryCriteria<PropertyCriterion<string>> criteria,
        List<PlannedCriterion> planned)
    {
        foreach (var item in criteria.Items)
        {
            if (string.Equals(item.Key, "isbn", StringComparison.OrdinalIgnoreCase))
            {
                planned.Add(PlanIsbnFact(item.Value));
            }
            else if (string.Equals(item.Key, "language", StringComparison.OrdinalIgnoreCase))
            {
                planned.Add(PlanLanguage(item.Value));
            }
            else if (string.Equals(item.Key, "pages", StringComparison.OrdinalIgnoreCase))
            {
                planned.Add(PlanPagesFact(item.Value));
            }
            else
            {
                planned.Add(KnownEmpty());
            }
        }
    }

    private PlannedCriterion PlanIsbnFact(PropertyCriterion<string> criterion)
    {
        var (op, negate) = InterpretOperator(criterion.Operator, criterion.Negate);
        if (op is not CriteriaValueOperator.Equals and not CriteriaValueOperator.In)
        {
            return KnownEmpty();
        }

        var values = Strings(criterion);
        var isbns = values.Select(value => value.NormalizeIsbn())
            .Where(value => value is not null)
            .Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (isbns.Count == 0)
        {
            return KnownEmpty();
        }

        var partial = isbns.Count < values.Count;
        var filter = new QueryFilter
        {
            Field = QueryField.Isbn,
            Operator = isbns.Count == 1 ? CriteriaValueOperator.Equals : CriteriaValueOperator.In,
            Values = isbns,
            Negate = negate
        };
        return negate
            ? new PlannedCriterion
            {
                Filters = [filter],
                NeedsIndependentSeed = true,
                CannotFulfill = partial
            }
            : new PlannedCriterion
            {
                Filters = [filter],
                SourceTerms = [Term("isbn", isbns)],
                CannotFulfill = partial
            };
    }

    private PlannedCriterion PlanPagesFact(PropertyCriterion<string> criterion)
    {
        var (op, negate) = InterpretOperator(criterion.Operator, criterion.Negate);
        if (!IsTextOperator(op))
        {
            return KnownEmpty();
        }

        var values = Strings(criterion);
        if (values.Count == 0)
        {
            return KnownEmpty();
        }

        return new PlannedCriterion
        {
            Filters = [StringFilter(QueryField.Pages, op, negate, values)],
            NeedsIndependentSeed = true
        };
    }

    private PlannedCriterion PlanAlternateTexts(IReadOnlyList<AlternateTextCriteria> alternatives, bool excluded)
    {
        var filterAlternatives = new List<FilterAlternative>();
        var partial = false;
        foreach (var alternative in alternatives)
        {
            if (alternative.Language.And.Any() || !alternative.Text.And.Any())
            {
                partial = true;
                continue;
            }

            var kinds = new List<string> { SubtitleKind, AlternativeTitleKind };
            var usable = true;
            foreach (var kindCriterion in alternative.Kind.And)
            {
                var (op, negate) = InterpretOperator(kindCriterion.Operator, kindCriterion.Negate);
                var kindValues = Strings(kindCriterion);
                if (!IsTextOperator(op) || kindValues.Count == 0)
                {
                    usable = false;
                    break;
                }

                kinds = [.. kinds.Where(kind => MatchesConstant(kind, op, kindValues) ^ negate)];
            }

            if (!usable || kinds.Count == 0)
            {
                partial = true;
                continue;
            }

            var field = kinds.Count == 2
                ? QueryField.AlternateText
                : kinds[0] == SubtitleKind
                    ? QueryField.Subtitle
                    : QueryField.AlternativeTitle;
            var filters = new List<QueryFilter>();
            foreach (var text in alternative.Text.And)
            {
                var (op, negate) = InterpretOperator(text.Operator, text.Negate);
                var values = Strings(text);
                if (!IsTextOperator(op) || values.Count == 0)
                {
                    usable = false;
                    break;
                }

                filters.Add(StringFilter(field, op, negate, values));
            }

            if (!usable)
            {
                partial = true;
                continue;
            }

            filterAlternatives.Add(new FilterAlternative
            {
                Filters = filters,
                AlignTexts = true
            });
        }

        if (filterAlternatives.Count == 0)
        {
            return KnownEmpty();
        }

        return new PlannedCriterion
        {
            Filters = [.. filterAlternatives.SelectMany(alternative => alternative.Filters)],
            FilterGroups = [new FilterGroup { Alternatives = filterAlternatives, Negate = excluded }],
            NeedsIndependentSeed = true,
            CannotFulfill = partial
        };
    }

    private IEnumerable<SourceGroup> SourceGroups(PlannedCriterion criterion)
    {
        if (criterion.SourceGroups.Count > 0)
        {
            return criterion.SourceGroups;
        }

        return criterion.SourceTerms.Select(term => new SourceGroup
        {
            Alternatives = [new SourceAlternative { Terms = [term] }]
        });
    }

    private IEnumerable<FilterGroup> FilterGroups(PlannedCriterion criterion)
    {
        if (criterion.FilterGroups.Count > 0)
        {
            return criterion.FilterGroups;
        }

        return criterion.Filters.Select(filter => new FilterGroup
        {
            Alternatives = [new FilterAlternative { Filters = [filter] }]
        });
    }

    private bool TryReadExternalId(ExternalIdCriteria criterion, out string? source, out IReadOnlyList<string> values)
    {
        source = null;
        values = [];
        var sourceCriteria = criterion.Source.And.ToArray();
        var valueCriteria = criterion.Value.And.ToArray();
        if (sourceCriteria.Length > 1 || valueCriteria.Length != 1)
        {
            return false;
        }

        if (sourceCriteria.Length == 1)
        {
            var sourceCriterion = sourceCriteria[0];
            var (sourceOperator, sourceNegate) = InterpretOperator(sourceCriterion.Operator, sourceCriterion.Negate);
            if (sourceNegate ||
                sourceOperator is not CriteriaValueOperator.Equals and not CriteriaValueOperator.In)
            {
                return false;
            }

            var sources = Strings(sourceCriterion);
            if (sources.Count != 1)
            {
                return false;
            }

            source = sources[0];
        }

        var valueCriterion = valueCriteria[0];
        var (valueOperator, valueNegate) = InterpretOperator(valueCriterion.Operator, valueCriterion.Negate);
        if (valueNegate ||
            valueOperator is not CriteriaValueOperator.Equals and not CriteriaValueOperator.In)
        {
            return false;
        }

        values = Strings(valueCriterion);
        return values.Count > 0;
    }

    private SourceTerm? DateTerm(CriteriaValueOperator op, IReadOnlyList<TemporalValue> values)
    {
        if (op is CriteriaValueOperator.Equals or CriteriaValueOperator.In)
        {
            return Term(
                "first_publish_year",
                [.. values.Select(value => value.Value.Year.ToString(CultureInfo.InvariantCulture))]);
        }

        var year = values[0].Value.Year.ToString(CultureInfo.InvariantCulture);
        var range = op switch
        {
            CriteriaValueOperator.GreaterThan or CriteriaValueOperator.GreaterThanOrEqual =>
                string.Concat("[", year, " TO *]"),
            CriteriaValueOperator.LessThan or CriteriaValueOperator.LessThanOrEqual =>
                string.Concat("[* TO ", year, "]"),
            _ => null
        };
        return range is null
            ? null
            : new SourceTerm
            {
                Field = "first_publish_year",
                Values = [range],
                RangeSyntax = true
            };
    }

    private QueryFilter StringFilter(
        QueryField field,
        CriteriaValueOperator op,
        bool negate,
        IReadOnlyList<string> values)
    {
        return new QueryFilter
        {
            Field = field,
            Operator = op,
            Values = values,
            Negate = negate
        };
    }

    private SourceTerm Term(string field, IReadOnlyList<string> values)
    {
        return new SourceTerm { Field = field, Values = values };
    }

    private SourceTerm WildcardTerm(string field, IReadOnlyList<string> values, WildcardPlacement placement)
    {
        return new SourceTerm
        {
            Field = field,
            Values = values,
            WildcardPlacement = placement
        };
    }

    private QueryPlan Empty(QueryRequest request, string reason)
    {
        Telemetry.PlansRefused.Add(1, new TagList
        {
            { "provider", Constants.ProviderType },
            { "content_type", request.Hierarchy.ContentTypeSlug },
            { "reason", reason }
        });
        return new QueryPlan
        {
            Root = new()
            {
                Kind = PlanNodeKind.Fetch,
                State = PlanNodeState.Empty,
                Reason = "The normalized criteria have an empty result set."
            },
            Hierarchy = request.Hierarchy,
            Page = Math.Max(1, request.Page),
            MaxResults = request.MaxResults,
            Advanced = request.Intent == QueryIntent.AdvancedSearch
        };
    }

    private bool IsTextOperator(CriteriaValueOperator op)
    {
        return op is
            CriteriaValueOperator.Equals
            or CriteriaValueOperator.In
            or CriteriaValueOperator.ContainsString
            or CriteriaValueOperator.ContainsWord
            or CriteriaValueOperator.StartsWithString
            or CriteriaValueOperator.EndsWithString;
    }

    private bool IsGenreOperator(CriteriaValueOperator op)
    {
        return op is
            CriteriaValueOperator.Equals
            or CriteriaValueOperator.In
            or CriteriaValueOperator.ContainsString
            or CriteriaValueOperator.ContainsWord;
    }

    private bool IsPositiveEquality(PropertyCriterion<string> criterion)
    {
        var interpreted = InterpretOperator(criterion.Operator, criterion.Negate);
        return !interpreted.Negate &&
            interpreted.Operator is CriteriaValueOperator.Equals or CriteriaValueOperator.In;
    }

    private bool MatchesRelationshipRoute(
        IReadOnlyList<PropertyCriterion<string>> relationshipKindCriteria,
        IReadOnlyList<PropertyCriterion<string>> subjectKindCriteria,
        string relationshipKind,
        string subjectKind)
    {
        return relationshipKindCriteria.All(
                criterion => Strings(criterion).Any(
                    value => string.Equals(value, relationshipKind, StringComparison.OrdinalIgnoreCase))) &&
            subjectKindCriteria.All(
                criterion => Strings(criterion).Any(
                    value => string.Equals(value, subjectKind, StringComparison.OrdinalIgnoreCase)));
    }

    private IReadOnlyList<string> Strings(PropertyCriterion<string> criterion)
    {
        var values = new List<string>();
        var scalar = CleanText(criterion.Value);
        if (scalar is not null)
        {
            values.Add(scalar);
        }

        values.AddRange((criterion.Values ?? []).Select(CleanText)
            .Where(value => value is not null)
            .Cast<string>());
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

    private (CriteriaValueOperator Operator, bool Negate) InterpretOperator(CriteriaValueOperator op, bool negate)
    {
        var interpreted = op switch
        {
            CriteriaValueOperator.NotEquals => (CriteriaValueOperator.Equals, true),
            CriteriaValueOperator.NotIn => (CriteriaValueOperator.In, true),
            CriteriaValueOperator.NotContainsString => (CriteriaValueOperator.ContainsString, true),
            CriteriaValueOperator.NotStartsWithString => (CriteriaValueOperator.StartsWithString, true),
            CriteriaValueOperator.NotEndsWithString => (CriteriaValueOperator.EndsWithString, true),
            _ => (op, false)
        };
        return (interpreted.Item1, negate ^ interpreted.Item2);
    }

    private string? CleanText(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private PlannedCriterion PlanAuthorDate(PropertyCriterion<TemporalValue> criterion, AuthorDateField field)
    {
        var (op, negate) = InterpretOperator(criterion.Operator, criterion.Negate);
        if (op is not (CriteriaValueOperator.Equals
            or CriteriaValueOperator.In
            or CriteriaValueOperator.GreaterThan
            or CriteriaValueOperator.GreaterThanOrEqual
            or CriteriaValueOperator.LessThan
            or CriteriaValueOperator.LessThanOrEqual))
        {
            return KnownEmpty();
        }

        var values = TemporalValues(criterion);
        if (values.Count == 0)
        {
            return KnownEmpty();
        }

        return new PlannedCriterion
        {
            Residuals =
            [
                new AuthorDateResidual
                {
                    Field = field,
                    Operator = op,
                    Negate = negate,
                    TemporalValues = values
                }
            ],
            NeedsIndependentSeed = true
        };
    }

    private PlannedCriterion PlanReferences(IReadOnlyList<ReferenceMetadataCriteria> alternatives, bool excluded)
    {
        if (alternatives.Any(CanonicalReferenceSatisfies))
        {
            return excluded ? KnownEmpty() : Satisfied();
        }

        var workOlids = new List<string>();
        var partial = false;
        foreach (var alternative in alternatives)
        {
            if (TryReadCanonicalWorkOlids(alternative, out var alternativeOlids))
            {
                workOlids.AddRange(alternativeOlids);
            }
            else
            {
                partial = true;
            }
        }

        workOlids = [.. workOlids.Distinct(StringComparer.OrdinalIgnoreCase)];
        if (workOlids.Count == 0)
        {
            return KnownEmpty();
        }

        var filter = new QueryFilter
        {
            Field = QueryField.WorkOlid,
            Operator = workOlids.Count == 1 ? CriteriaValueOperator.Equals : CriteriaValueOperator.In,
            Values = workOlids,
            Negate = excluded
        };
        var sourceTerm = Term("key", [.. workOlids.Select(value => string.Concat("/works/", value))]);
        return excluded
            ? new PlannedCriterion
            {
                Filters = [filter],
                SourceTerms = [sourceTerm with { Negate = true }],
                NeedsIndependentSeed = true,
                CannotFulfill = partial
            }
            : new PlannedCriterion
            {
                Filters = [filter],
                SourceTerms = [sourceTerm],
                CannotFulfill = partial
            };
    }

    private bool CanonicalReferenceSatisfies(ReferenceMetadataCriteria alternative)
    {
        if (alternative.Ordinal.And.Any(criterion => !SatisfiedByConstant(0, criterion)))
        {
            return false;
        }

        foreach (var kind in alternative.Kind.And)
        {
            if (!SatisfiedByConstant(CanonicalReferenceKind, kind))
            {
                return false;
            }
        }

        foreach (var displayName in alternative.DisplayName.And)
        {
            if (!SatisfiedByConstant(CanonicalReferenceDisplayName, displayName))
            {
                return false;
            }
        }

        foreach (var language in alternative.Language.And)
        {
            var (op, negate) = InterpretOperator(language.Operator, language.Negate);
            if (op is not (CriteriaValueOperator.IsNull or CriteriaValueOperator.IsNotNull) ||
                !((op == CriteriaValueOperator.IsNull) ^ negate))
            {
                return false;
            }
        }

        foreach (var url in alternative.Url.And)
        {
            if (!SatisfiedByCanonicalUrl(url))
            {
                return false;
            }
        }

        return true;
    }

    private bool TryReadCanonicalWorkOlids(ReferenceMetadataCriteria alternative, out IReadOnlyList<string> workOlids)
    {
        workOlids = [];
        if (alternative.Ordinal.And.Any(criterion => !SatisfiedByConstant(0, criterion)) ||
            alternative.Kind.And.Any(criterion => !SatisfiedByConstant(CanonicalReferenceKind, criterion)) ||
            alternative.DisplayName.And.Any(criterion =>
                !SatisfiedByConstant(CanonicalReferenceDisplayName, criterion)) ||
            alternative.Language.And.Any(criterion => !SatisfiedByAbsentString(criterion)) ||
            !alternative.Url.And.Any())
        {
            return false;
        }

        HashSet<string>? matches = null;
        foreach (var criterion in alternative.Url.And)
        {
            var (op, negate) = InterpretOperator(criterion.Operator, criterion.Negate);
            if (negate || op is not (CriteriaValueOperator.Equals or CriteriaValueOperator.In))
            {
                return false;
            }

            var values = new List<Uri>();
            if (criterion.Value is not null)
            {
                values.Add(criterion.Value);
            }

            values.AddRange(criterion.Values ?? []);
            var criterionMatches = values.Select(CanonicalWorkOlid)
                .Where(value => value is not null)
                .Cast<string>()
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (criterionMatches.Count != values.Count)
            {
                return false;
            }

            if (matches is null)
            {
                matches = criterionMatches;
            }
            else
            {
                matches.IntersectWith(criterionMatches);
            }
        }

        workOlids = matches?.ToArray() ?? [];
        return workOlids.Count > 0;
    }

    private string? CanonicalWorkOlid(Uri uri)
    {
        if (!uri.IsAbsoluteUri ||
            !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(uri.Host, "openlibrary.org", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var segments = uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        return segments.Length == 2 && string.Equals(segments[0], "works", StringComparison.OrdinalIgnoreCase)
            ? segments[1].NormalizeOlid('W')
            : null;
    }

    private bool SatisfiedByAbsentString(PropertyCriterion<string> criterion)
    {
        var (op, negate) = InterpretOperator(criterion.Operator, criterion.Negate);
        return op is CriteriaValueOperator.IsNull or CriteriaValueOperator.IsNotNull &&
            (op == CriteriaValueOperator.IsNull) ^ negate;
    }

    private bool SatisfiedByConstant(string constant, PropertyCriterion<string> criterion)
    {
        var (op, negate) = InterpretOperator(criterion.Operator, criterion.Negate);
        if (op is CriteriaValueOperator.IsNull or CriteriaValueOperator.IsNotNull)
        {
            return (op == CriteriaValueOperator.IsNotNull) ^ negate;
        }

        if (!IsTextOperator(op))
        {
            return false;
        }

        var values = Strings(criterion);
        return values.Count > 0 && MatchesConstant(constant, op, values) ^ negate;
    }

    private bool SatisfiedByConstant(int constant, PropertyCriterion<int> criterion)
    {
        var (op, negate) = InterpretOperator(criterion.Operator, criterion.Negate);
        if (op is CriteriaValueOperator.IsNull or CriteriaValueOperator.IsNotNull)
        {
            return (op == CriteriaValueOperator.IsNotNull) ^ negate;
        }

        var values = (criterion.Values ?? []).ToList();
        if (values.Count == 0)
        {
            values.Add(criterion.Value);
        }

        var matched = op switch
        {
            CriteriaValueOperator.Equals or CriteriaValueOperator.In => values.Contains(constant),
            CriteriaValueOperator.GreaterThan => values.Any(value => constant > value),
            CriteriaValueOperator.GreaterThanOrEqual => values.Any(value => constant >= value),
            CriteriaValueOperator.LessThan => values.Any(value => constant < value),
            CriteriaValueOperator.LessThanOrEqual => values.Any(value => constant <= value),
            _ => false
        };
        return matched ^ negate;
    }

    private bool SatisfiedByCanonicalUrl(PropertyCriterion<Uri> criterion)
    {
        var (op, negate) = InterpretOperator(criterion.Operator, criterion.Negate);
        if (op is CriteriaValueOperator.IsNull or CriteriaValueOperator.IsNotNull)
        {
            return (op == CriteriaValueOperator.IsNotNull) ^ negate;
        }

        if (negate)
        {
            return false;
        }

        var values = new List<string>();
        if (criterion.Value is not null)
        {
            values.Add(criterion.Value.ToString());
        }

        values.AddRange((criterion.Values ?? []).Where(value => value is not null).Select(value => value.ToString()));
        return op switch
        {
            CriteriaValueOperator.ContainsString => values.Any(value =>
                CanonicalWorkUrlPrefix.Contains(value, StringComparison.OrdinalIgnoreCase)),
            CriteriaValueOperator.StartsWithString => values.Any(value =>
                CanonicalWorkUrlPrefix.StartsWith(value, StringComparison.OrdinalIgnoreCase)),
            _ => false
        };
    }

    private PlannedCriterion PlanImages(IReadOnlyList<ImageCriteria> alternatives, bool excluded)
    {
        var presence = false;
        foreach (var alternative in alternatives)
        {
            if (alternative.MimeType.And.Any() ||
                alternative.Language.And.Any() ||
                alternative.Source.And.Any() ||
                alternative.Width.And.Any() ||
                alternative.Height.And.Any() ||
                alternative.Url.And.Any(criterion =>
                    InterpretOperator(criterion.Operator, criterion.Negate).Operator
                        is not (CriteriaValueOperator.IsNull or CriteriaValueOperator.IsNotNull)))
            {
                return KnownEmpty();
            }

            var coverPossible = true;
            foreach (var url in alternative.Url.And)
            {
                var (op, negate) = InterpretOperator(url.Operator, url.Negate);
                coverPossible &= (op == CriteriaValueOperator.IsNotNull) ^ negate;
            }

            foreach (var type in alternative.Type.And)
            {
                var (op, negate) = InterpretOperator(type.Operator, type.Negate);
                if (op is CriteriaValueOperator.IsNull or CriteriaValueOperator.IsNotNull)
                {
                    coverPossible &= (op == CriteriaValueOperator.IsNotNull) ^ negate;
                    continue;
                }

                var values = Strings(type);
                if (!IsTextOperator(op) || values.Count == 0)
                {
                    return KnownEmpty();
                }

                coverPossible &= MatchesConstant("cover", op, values) ^ negate;
            }

            presence |= coverPossible;
        }

        if (!presence)
        {
            return excluded ? Satisfied() : KnownEmpty();
        }

        return new PlannedCriterion
        {
            Filters =
            [
                new QueryFilter
                {
                    Field = QueryField.Cover,
                    Operator = CriteriaValueOperator.IsNotNull,
                    Negate = excluded
                }
            ],
            NeedsIndependentSeed = true
        };
    }

    private PlannedCriterion PlanRelationships(IReadOnlyList<RelationshipCriteria> alternatives, bool excluded = false)
    {
        if (alternatives.Count == 0)
        {
            return KnownEmpty();
        }

        var filterAlternatives = new List<FilterAlternative>();
        var sourceAlternatives = new List<SourceAlternative>();
        var partial = false;
        var approximate = false;
        var needsIndependentSeed = excluded;
        foreach (var alternative in alternatives)
        {
            var plannedAlternativeCount = filterAlternatives.Count;
            var relationshipKindCriteria = alternative.Kind.And.ToArray();
            var subjectKindCriteria = alternative.Type.And.ToArray();
            if (relationshipKindCriteria.Concat(subjectKindCriteria)
                    .Any(criterion => !IsPositiveEquality(criterion) || Strings(criterion).Count == 0) ||
                alternative.RelatedMetadataContainerId.And.Any() ||
                alternative.Qualifier.And.Any())
            {
                partial = true;
                continue;
            }

            var relationshipIdentifiers = alternative.ExternalIds.Some.SelectMany(group => group)
                .Concat(alternative.ExternalIds.None)
                .ToArray();
            var author = MatchesRelationshipRoute(
                relationshipKindCriteria,
                subjectKindCriteria,
                Constants.AuthorRelationshipKind,
                Constants.PersonSubjectKind) &&
                relationshipIdentifiers.All(IsAuthorRelationshipIdentifier);
            var publisher = MatchesRelationshipRoute(
                relationshipKindCriteria,
                subjectKindCriteria,
                Constants.PublisherRelationshipKind,
                Constants.PublisherSubjectKind) &&
                relationshipIdentifiers.All(IsPublisherRelationshipIdentifier);
            if (author &&
                TryPlanAuthorAlternative(
                    alternative,
                    ref partial,
                    ref approximate,
                    out var authorPlan))
            {
                filterAlternatives.Add(authorPlan.Alternative);
                if (excluded || authorPlan.SourceTerms.Count == 0)
                {
                    needsIndependentSeed = true;
                }
                else
                {
                    sourceAlternatives.Add(new SourceAlternative { Terms = authorPlan.SourceTerms });
                }
            }

            if (publisher &&
                TryPlanPublisherAlternative(alternative, ref partial, ref approximate, out var publisherPlan))
            {
                filterAlternatives.Add(publisherPlan.Alternative);
                if (excluded || publisherPlan.SourceTerms.Count == 0)
                {
                    needsIndependentSeed = true;
                }
                else
                {
                    sourceAlternatives.Add(new SourceAlternative { Terms = publisherPlan.SourceTerms });
                }
            }

            foreach (var facet in SubjectFacets)
            {
                var facetEligible = relationshipIdentifiers.Length == 0 &&
                    MatchesRelationshipRoute(relationshipKindCriteria, subjectKindCriteria, facet.Kind, facet.Kind);
                if (!facetEligible ||
                    !TryPlanSubjectFacetAlternative(
                        alternative,
                        facet.Field,
                        facet.SourceField,
                        ref partial,
                        ref approximate,
                        out var facetPlan))
                {
                    continue;
                }

                filterAlternatives.Add(facetPlan.Alternative);
                if (excluded || facetPlan.SourceTerms.Count == 0)
                {
                    needsIndependentSeed = true;
                }
                else
                {
                    sourceAlternatives.Add(new SourceAlternative { Terms = facetPlan.SourceTerms });
                }
            }

            if (filterAlternatives.Count == plannedAlternativeCount)
            {
                partial = true;
            }
        }

        if (filterAlternatives.Count == 0)
        {
            return KnownEmpty();
        }

        if (sourceAlternatives.Count != filterAlternatives.Count)
        {
            sourceAlternatives.Clear();
            needsIndependentSeed = true;
        }

        var filtersFlat = filterAlternatives.SelectMany(alternative => alternative.Filters).ToArray();
        var sourceTermsFlat = sourceAlternatives.SelectMany(alternative => alternative.Terms).ToArray();
        var sourceGroups = sourceAlternatives.Count == 0
            ? Array.Empty<SourceGroup>()
            : [new SourceGroup { Alternatives = sourceAlternatives }];
        return new PlannedCriterion
        {
            Filters = filtersFlat,
            FilterGroups = [new FilterGroup { Alternatives = filterAlternatives, Negate = excluded }],
            SourceTerms = sourceTermsFlat,
            SourceGroups = sourceGroups,
            NeedsIndependentSeed = needsIndependentSeed,
            RecallBoundedSource = approximate,
            CannotFulfill = partial
        };
    }

    private bool TryPlanAuthorAlternative(
        RelationshipCriteria alternative,
        ref bool partial,
        ref bool approximate,
        out (FilterAlternative Alternative, IReadOnlyList<SourceTerm> SourceTerms) plan)
    {
        plan = default;
        var filters = new List<QueryFilter>();
        var authorOlidTerms = new List<SourceTerm>();
        var authorNameTerms = new List<SourceTerm>();
        foreach (var subject in alternative.Subject.And)
        {
            var (op, negate) = InterpretOperator(subject.Operator, subject.Negate);
            var values = Strings(subject);
            if (!IsTextOperator(op) || values.Count == 0)
            {
                partial = true;
                return false;
            }

            filters.Add(new QueryFilter
            {
                Field = QueryField.AuthorName,
                Operator = op,
                Values = values,
                Negate = negate
            });
            if (!negate &&
                op is CriteriaValueOperator.Equals
                    or CriteriaValueOperator.In
                    or CriteriaValueOperator.ContainsWord
                    or CriteriaValueOperator.ContainsString)
            {
                authorNameTerms.Add(Term("author", values));
                approximate |= op == CriteriaValueOperator.ContainsString;
            }
        }

        foreach (var group in alternative.ExternalIds.Some)
        {
            var groupOlids = new List<string>();
            foreach (var externalId in group)
            {
                if (!TryReadExternalId(externalId, out var source, out var values) ||
                    source is not null &&
                    !string.Equals(
                        source,
                        Constants.ProviderType,
                        StringComparison.OrdinalIgnoreCase))
                {
                    partial = true;
                    continue;
                }

                foreach (var value in values)
                {
                    var olid = value.NormalizeOlid('A');
                    if (olid is not null)
                    {
                        groupOlids.Add(olid);
                    }
                    else
                    {
                        partial = true;
                    }
                }
            }

            groupOlids = [.. groupOlids.Distinct(StringComparer.OrdinalIgnoreCase)];
            if (groupOlids.Count == 0)
            {
                partial = true;
                return false;
            }

            filters.Add(new QueryFilter
            {
                Field = QueryField.AuthorOlid,
                Operator = groupOlids.Count == 1 ? CriteriaValueOperator.Equals : CriteriaValueOperator.In,
                Values = groupOlids
            });
            authorOlidTerms.Add(Term("author_key", groupOlids));
        }

        foreach (var externalId in alternative.ExternalIds.None)
        {
            if (!TryReadExternalId(externalId, out var source, out var values) ||
                source is not null &&
                !string.Equals(
                    source,
                    Constants.ProviderType,
                    StringComparison.OrdinalIgnoreCase))
            {
                partial = true;
                return false;
            }

            var olids = values.Select(value => value.NormalizeOlid('A'))
                .Where(value => value is not null)
                .Cast<string>()
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (olids.Length == 0 || values.Any(value => value.NormalizeOlid('A') is null))
            {
                partial = true;
                return false;
            }

            filters.Add(new QueryFilter
            {
                Field = QueryField.AuthorOlid,
                Operator = olids.Length == 1 ? CriteriaValueOperator.Equals : CriteriaValueOperator.In,
                Values = olids,
                Negate = true
            });
        }

        if (filters.Count == 0)
        {
            partial = true;
            return false;
        }

        plan = (
            new FilterAlternative { Filters = filters, AlignAuthors = true },
            authorOlidTerms.Count > 0 ? authorOlidTerms : authorNameTerms);
        return true;
    }

    private bool TryPlanSubjectFacetAlternative(
        RelationshipCriteria alternative,
        QueryField field,
        string sourceField,
        ref bool partial,
        ref bool approximate,
        out (FilterAlternative Alternative, IReadOnlyList<SourceTerm> SourceTerms) plan)
    {
        plan = default;
        if (alternative.ExternalIds.Some.Any() || alternative.ExternalIds.None.Any())
        {
            partial = true;
        }

        var filters = new List<QueryFilter>();
        var terms = new List<SourceTerm>();
        foreach (var subject in alternative.Subject.And)
        {
            var (op, negate) = InterpretOperator(subject.Operator, subject.Negate);
            var values = Strings(subject);
            if (!IsTextOperator(op) || values.Count == 0)
            {
                partial = true;
                return false;
            }

            filters.Add(new QueryFilter
            {
                Field = field,
                Operator = op,
                Values = values,
                Negate = negate
            });
            if (!negate &&
                op is CriteriaValueOperator.Equals
                    or CriteriaValueOperator.In
                    or CriteriaValueOperator.ContainsWord
                    or CriteriaValueOperator.ContainsString)
            {
                terms.Add(Term(sourceField, values));
                approximate |= op == CriteriaValueOperator.ContainsString;
            }
        }

        if (filters.Count == 0)
        {
            partial = true;
            return false;
        }

        plan = (new FilterAlternative { Filters = filters, AlignSubjectFacet = field }, terms);
        return true;
    }

    private bool TryPlanPublisherAlternative(
        RelationshipCriteria alternative,
        ref bool partial,
        ref bool approximate,
        out (FilterAlternative Alternative, IReadOnlyList<SourceTerm> SourceTerms) plan)
    {
        plan = default;
        var filters = new List<QueryFilter>();
        var terms = new List<SourceTerm>();
        foreach (var subject in alternative.Subject.And)
        {
            var (op, negate) = InterpretOperator(subject.Operator, subject.Negate);
            var values = Strings(subject);
            if (!IsTextOperator(op) || values.Count == 0)
            {
                partial = true;
                return false;
            }

            filters.Add(new QueryFilter
            {
                Field = QueryField.Publisher,
                Operator = op,
                Values = values,
                Negate = negate
            });
            if (!negate &&
                op is CriteriaValueOperator.Equals
                    or CriteriaValueOperator.In
                    or CriteriaValueOperator.ContainsWord
                    or CriteriaValueOperator.ContainsString)
            {
                terms.Add(Term("publisher", values));
                approximate |= op == CriteriaValueOperator.ContainsString;
            }
        }

        foreach (var group in alternative.ExternalIds.Some)
        {
            var publishers = group.SelectMany(PublisherNames).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            if (publishers.Length == 0)
            {
                partial = true;
                return false;
            }

            filters.Add(new QueryFilter
            {
                Field = QueryField.Publisher,
                Operator = publishers.Length == 1 ? CriteriaValueOperator.Equals : CriteriaValueOperator.In,
                Values = publishers
            });
            terms.Add(Term("publisher", publishers));
        }

        foreach (var externalId in alternative.ExternalIds.None)
        {
            var publishers = PublisherNames(externalId).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            if (publishers.Length == 0)
            {
                partial = true;
                return false;
            }

            filters.Add(new QueryFilter
            {
                Field = QueryField.Publisher,
                Operator = publishers.Length == 1 ? CriteriaValueOperator.Equals : CriteriaValueOperator.In,
                Values = publishers,
                Negate = true
            });
        }

        if (filters.Count == 0)
        {
            partial = true;
            return false;
        }

        plan = (new FilterAlternative { Filters = filters, AlignPublishers = true }, terms);
        return true;
    }

    private bool IsAuthorRelationshipIdentifier(ExternalIdCriteria criterion)
    {
        if (!TryReadExternalId(criterion, out var source, out var values))
        {
            return false;
        }

        return string.Equals(source, Constants.ProviderType, StringComparison.OrdinalIgnoreCase) ||
            source is null && values.All(value => value.NormalizeOlid('A') is not null);
    }

    private bool IsPublisherRelationshipIdentifier(ExternalIdCriteria criterion)
    {
        if (!TryReadExternalId(criterion, out var source, out _))
        {
            return false;
        }

        return source is null ||
            string.Equals(source, Constants.ProviderType, StringComparison.OrdinalIgnoreCase);
    }

    private IReadOnlyList<string> PublisherNames(ExternalIdCriteria criterion)
    {
        if (!TryReadExternalId(criterion, out var source, out var values) ||
            source is not null &&
            !string.Equals(
                source,
                Constants.ProviderType,
                StringComparison.OrdinalIgnoreCase))
        {
            return [];
        }

        return [.. values.Select(value => CleanText(value)?.Replace('_', ' '))
            .Where(value => value is not null)
            .Cast<string>()];
    }

    private bool IsAuthoritativeIdentitySeed(SourceTerm term)
    {
        return !term.Negate &&
            term.Field is "key" or "isbn" or "oclc" or "lccn" or "id_goodreads";
    }

    private IReadOnlyList<string> RequiredFields(IReadOnlyList<QueryFilter> filters)
    {
        var fields = new HashSet<string>(StringComparer.Ordinal);
        foreach (var filter in filters)
        {
            switch (filter.Field)
            {
                case QueryField.AlternativeTitle:
                    fields.Add("alternative_title");
                    break;
                case QueryField.Subtitle:
                    fields.Add("subtitle");
                    break;
                case QueryField.AlternateText:
                    fields.Add("alternative_title");
                    fields.Add("subtitle");
                    break;
                case QueryField.Pages:
                    fields.Add("number_of_pages_median");
                    break;
            }
        }

        return [.. fields.OrderBy(value => value, StringComparer.Ordinal)];
    }

    private IReadOnlyList<string> PredicateLabels(
        IReadOnlyList<QueryFilter> filters,
        IReadOnlyList<AuthorDateResidual> residuals)
    {
        return
        [
            .. filters.Select(filter => string.Concat(
                filter.Field,
                ":",
                filter.Negate ? "not-" : string.Empty,
                filter.Operator)),
            .. residuals.Select(residual => string.Concat(
                "author-",
                residual.Field,
                ":",
                residual.Negate ? "not-" : string.Empty,
                residual.Operator))
        ];
    }

    private IReadOnlyList<string> SourceFields(IReadOnlyList<SourceTerm> terms, IReadOnlyList<SourceGroup> groups)
    {
        var alternatives = groups
            .SelectMany(group => group.Alternatives)
            .SelectMany(alternative => alternative.Terms);
        return [.. terms
            .Concat(alternatives)
            .Select(term => term.Field)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)];
    }

    private bool MatchesHierarchyValue(PropertyCriterion<string> criterion, string candidate)
    {
        var (op, negate) = InterpretOperator(criterion.Operator, criterion.Negate);
        var matched = op switch
        {
            CriteriaValueOperator.IsNull => false,
            CriteriaValueOperator.IsNotNull => true,
            _ => MatchesConstant(candidate, op, Strings(criterion))
        };
        return matched ^ negate;
    }

    private PlannedCriterion PlanConstantBoolean(bool candidate, bool requested)
    {
        return candidate == requested ? Satisfied() : KnownEmpty();
    }

    private void PlanAbsentSet<T>(
        IEnumerable<PropertyCriterion<T>> criteria,
        string field,
        List<PlannedCriterion> planned,
        List<string> dispositions)
    {
        foreach (var criterion in criteria)
        {
            planned.Add(PlanAbsentProperty(criterion));
            dispositions.Add(string.Concat(field, ":constant-absent"));
        }
    }

    private void PlanHierarchyCriteria(
        ContentHierarchyItemCriteria criteria,
        BookHierarchy hierarchy,
        List<PlannedCriterion> planned,
        List<string> dispositions)
    {
        foreach (var criterion in criteria.ContentType.And)
        {
            planned.Add(MatchesHierarchyValue(criterion, hierarchy.ContentTypeSlug) ? Satisfied() : KnownEmpty());
            dispositions.Add("contentType:constant");
        }

        foreach (var criterion in criteria.Level.And)
        {
            planned.Add(MatchesHierarchyValue(criterion, hierarchy.BookLevelSlug) ? Satisfied() : KnownEmpty());
            dispositions.Add("level:constant");
        }

        PlanAbsentSet(criteria.ParentId.And, "parentId", planned, dispositions);
        PlanAbsentSet(criteria.RootFolderId.And, "rootFolderId", planned, dispositions);
        PlanAbsentSet(criteria.Slug.And, "slug", planned, dispositions);
        PlanAbsentSet(criteria.Monitoring.And, "monitoring", planned, dispositions);
        PlanAbsentSet(criteria.Fulfillment.And, "fulfillment", planned, dispositions);
        PlanAbsentSet(criteria.ActiveAcquisitionStatus.And, "activeAcquisitionStatus", planned, dispositions);
        PlanAbsentSet(criteria.QualityStatus.And, "qualityStatus", planned, dispositions);
        PlanAbsentSet(criteria.Path.And, "path", planned, dispositions);
        PlanAbsentSet(criteria.Tags.And, "tags", planned, dispositions);
        if (criteria.ContentItemId is not null)
        {
            planned.Add(KnownEmpty());
            dispositions.Add("contentItemId:constant-absent");
        }

        if (criteria.EffectiveMonitoring is not null)
        {
            planned.Add(KnownEmpty());
            dispositions.Add("effectiveMonitoring:constant-absent");
        }

        if (criteria.EffectiveRootFolderId is not null)
        {
            planned.Add(KnownEmpty());
            dispositions.Add("effectiveRootFolderId:constant-absent");
        }

        if (criteria.RootLevelOnly is { } rootLevelOnly)
        {
            planned.Add(PlanConstantBoolean(candidate: true, rootLevelOnly));
            dispositions.Add("rootLevelOnly:constant-true");
        }

        if (criteria.LeafOnly is { } leafOnly)
        {
            planned.Add(PlanConstantBoolean(candidate: true, leafOnly));
            dispositions.Add("leafOnly:constant-true");
        }

        if (criteria.PendingMetadataRefresh is not null)
        {
            planned.Add(KnownEmpty());
            dispositions.Add("pendingMetadataRefresh:constant-absent");
        }

        if (criteria.HasMetadataExternalIds is { } hasMetadataExternalIds)
        {
            planned.Add(PlanConstantBoolean(candidate: true, hasMetadataExternalIds));
            dispositions.Add("hasMetadataExternalIds:constant-true");
        }

        if (criteria.HasSearchableTitle is { } hasSearchableTitle)
        {
            planned.Add(PlanConstantBoolean(candidate: true, hasSearchableTitle));
            dispositions.Add("hasSearchableTitle:constant-true");
        }

        if (criteria.ExcludeIds is { Count: > 0 })
        {
            planned.Add(Satisfied());
            dispositions.Add("excludeIds:constant-absent-content-item-id");
        }

        if (criteria.AfterId is not null)
        {
            planned.Add(KnownEmpty());
            dispositions.Add("afterId:constant-absent");
        }

        if (criteria.AncestorId is not null)
        {
            planned.Add(KnownEmpty());
            dispositions.Add("ancestorId:constant-root");
        }

        if (criteria.Name.And.Any())
        {
            dispositions.Add("name:candidate-title");
        }

        if (criteria.Metadata.And.Any())
        {
            dispositions.Add("metadata:provider-plan");
        }
    }

    private QueryPlan PlanAdvanced(QueryRequest request)
    {
        if (request.MaxResults <= 0)
        {
            return Empty(request, RefusalUnbounded);
        }

        var dispositions = new List<string>();
        var hierarchyPlanned = new List<PlannedCriterion>();
        if (request.Criteria is { } hierarchyCriteria)
        {
            PlanHierarchyCriteria(hierarchyCriteria, request.Hierarchy, hierarchyPlanned, dispositions);
            if (hierarchyPlanned.Any(entry => entry.MatchesNothing))
            {
                return Empty(request, RefusalKnownEmpty) with
                {
                    CriterionDispositions = dispositions
                };
            }
        }

        var criteria = request.Criteria?.MergeMetadata() ?? request.MetadataCriteria ?? new MetadataCriteria();
        var planned = PlanCriteria(criteria);
        planned.AddRange(hierarchyPlanned);
        if (request.Criteria is { } fullCriteria)
        {
            planned.AddRange(fullCriteria.Name.And.Select(PlanTitle));
        }

        if (planned.Any(entry => entry.MatchesNothing))
        {
            return Empty(request, RefusalKnownEmpty);
        }

        var hasAuthoritativeIdentitySeed = planned.Any(entry =>
            !entry.RecallBoundedSource && entry.SourceTerms.Any(IsAuthoritativeIdentitySeed));
        var keptRecallBoundedSeed = false;
        for (var index = 0; index < planned.Count; index++)
        {
            var entry = planned[index];
            if (!entry.RecallBoundedSource || !entry.SourceTerms.Any(term => !term.Negate))
            {
                continue;
            }

            if (!hasAuthoritativeIdentitySeed && !keptRecallBoundedSeed)
            {
                keptRecallBoundedSeed = true;
                continue;
            }

            planned[index] = entry with { SourceTerms = [], SourceGroups = [] };
        }

        if (planned.Any(entry => entry.NeedsIndependentSeed) &&
            !planned.Any(entry => entry.SourceTerms.Any(term => !term.Negate)))
        {
            return Empty(request, RefusalUnbounded) with { CriterionDispositions = dispositions };
        }

        var terms = planned.SelectMany(entry => entry.SourceTerms).ToArray();
        var filters = planned.SelectMany(entry => entry.Filters).ToArray();
        var sourceGroups = planned.SelectMany(SourceGroups).ToArray();
        var filterGroups = planned.SelectMany(FilterGroups).ToArray();
        var residuals = planned.SelectMany(entry => entry.Residuals).ToArray();
        if (planned.Any(entry => entry.CannotFulfill))
        {
            return Empty(request, RefusalKnownEmpty) with
            {
                CriterionDispositions = dispositions
            };
        }

        if (terms.Length == 0)
        {
            return Empty(request, RefusalUnbounded) with
            {
                CriterionDispositions = dispositions
            };
        }

        var requiredFields = RequiredFields(filters);
        var fetch = new PlanNode
        {
            Kind = PlanNodeKind.Fetch,
            SourceTerms = terms,
            SourceGroups = sourceGroups,
            SourceFields = SourceFields(terms, sourceGroups),
            RequiredFields = requiredFields,
            LanguagePreference = request.LanguagePreference.NormalizeLanguagePreference(),
            PagingMode = filters.Length == 0 && filterGroups.Length == 0 && residuals.Length == 0
                ? FetchPagingMode.Direct
                : FetchPagingMode.SurvivorScan
        };
        var root = filters.Length == 0 && filterGroups.Length == 0 && residuals.Length == 0
            ? fetch
            : new PlanNode
            {
                Kind = PlanNodeKind.Filter,
                Children = [fetch],
                Filters = filters,
                FilterGroups = filterGroups,
                Residuals = residuals,
                Predicates = PredicateLabels(filters, residuals)
            };
        return new QueryPlan
        {
            Root = root,
            Hierarchy = request.Hierarchy,
            CandidateLanguagePreferences = CandidateLanguagePreferences(request),
            CriterionDispositions = dispositions,
            Page = 1,
            MaxResults = request.MaxResults,
            Advanced = true
        };
    }

    private List<PlannedCriterion> PlanCriteria(MetadataCriteria criteria)
    {
        var planned = new List<PlannedCriterion>();
        planned.AddRange(criteria.ProviderType.And.Select(c => PlanSelfIdentity(c, ProviderIdentities)));
        planned.AddRange(criteria.Source.And.Select(c => PlanSelfIdentity(c, ProviderIdentities)));
        planned.AddRange(criteria.Title.And.Select(PlanTitle));
        planned.AddRange(criteria.Language.And.Select(PlanLanguage));
        planned.AddRange(criteria.Overview.And.Select(PlanOverview));
        planned.AddRange(criteria.PublishDate.And.Select(PlanPublishDate));
        foreach (var criterion in criteria.CustomMetadata.And)
        {
            PlanCustomMetadata(criterion, planned);
        }

        planned.AddRange(criteria.OriginalTitle.And.Select(PlanOriginalTitle));
        planned.AddRange(criteria.Duration.And.Select(c => PlanAbsentProperty(c)));
        planned.AddRange(criteria.ProductionStatus.And.Select(c => PlanAbsentProperty(c)));
        planned.AddRange(criteria.Established.And.Select(c => PlanAbsentProperty(c)));
        planned.AddRange(criteria.Dissolved.And.Select(c => PlanAbsentProperty(c)));
        planned.AddRange(criteria.BirthDate.And.Select(c => PlanAuthorDate(c, AuthorDateField.BirthDate)));
        planned.AddRange(criteria.DeathDate.And.Select(c => PlanAuthorDate(c, AuthorDateField.DeathDate)));
        PlanAbsentCollection(criteria.Awards, planned);
        planned.AddRange(criteria.ExternalIds.Some.Select(PlanExternalIds));
        planned.AddRange(criteria.ExternalIds.None.Select(PlanExcludedExternalId));
        planned.AddRange(criteria.References.Some.Select(g => PlanReferences(g, excluded: false)));
        planned.AddRange(criteria.References.None.Select(e => PlanReferences([e], excluded: true)));
        planned.AddRange(criteria.Relationships.Some.Select(g => PlanRelationships(g)));
        planned.AddRange(criteria.Relationships.None.Select(e => PlanRelationships([e], excluded: true)));
        planned.AddRange(criteria.Images.Some.Select(g => PlanImages(g, excluded: false)));
        planned.AddRange(criteria.Images.None.Select(e => PlanImages([e], excluded: true)));
        PlanAbsentCollection(criteria.Previews, planned);
        planned.AddRange(criteria.AlternateTexts.Some.Select(g => PlanAlternateTexts(g, excluded: false)));
        planned.AddRange(criteria.AlternateTexts.None.Select(e => PlanAlternateTexts([e], excluded: true)));
        PlanAbsentCollection(criteria.ContentRatings, planned);
        planned.AddRange(criteria.Genres.Some.Select(g => PlanGenre(g, excluded: false)));
        planned.AddRange(criteria.Genres.None.Select(e => PlanGenre([e], excluded: true)));
        PlanAbsentCollection(criteria.Tags, planned);
        return planned;
    }
}
