using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

using Magpie.Extensions.Wikidata.Mapping;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Filtering;
using Magpie.Sdk.Content;

namespace Magpie.Extensions.Wikidata.QueryEngine;

internal sealed class QueryPlanner : IQueryPlanner
{
    private sealed record MappedCriterion(
        QueryFilter? Filter,
        bool MatchesNothing = false,
        ResidualFilter? ResidualFilter = null,
        string? Reason = null,
        bool IsUnsupported = false);

    private sealed class CollectionAlternativeCompilation
    {
        public SubjectFilter? Coarse { get; set; }

        public List<ResidualCollectionPredicate> Predicates { get; } = [];

        public bool RequiresResidual { get; set; }

        public bool MatchesNothing { get; set; }

        public bool ValueBearing { get; set; }

        public string? UnsupportedField { get; set; }
    }

    [SuppressMessage("Ordering", "SA1201", Justification = "Nested types come first in this codebase.")]
    private static readonly string SourceKeyPrefix = Constants.ProviderType + ":";

    private static readonly string[] SourceKeyTemplates = [.. Enum.GetValues(typeof(EntityKind))
        .Cast<EntityKind>()
        .Select(kind => SourceKeyPrefix + kind.ToString().ToLowerInvariant() + ":q")];

    public QueryPlan Plan(
        EntityKind entityKind,
        MetadataCriteria criteria,
        int page,
        string? ordinaryTerm = null,
        int maximumResults = SparqlQueryBuilder.PageSize)
    {
        var normalizedOrdinaryTerm = string.IsNullOrWhiteSpace(ordinaryTerm?.Replace("\"", string.Empty))
            ? null
            : ordinaryTerm.Trim();
        if (entityKind is not (EntityKind.Movie or EntityKind.Show))
        {
            var unsupported = new QueryPlan
            {
                EntityKind = entityKind,
                Strategy = QueryStrategy.Unsupported,
                Filters = [],
                CriterionDispositions =
                [
                    new CriterionDisposition
                    {
                        Source = nameof(QueryPlan.EntityKind),
                        Disposition = CriterionDispositionKind.Refused,
                        Reason = "The requested entity kind is not supported by Wikidata search."
                    }
                ],
                Warnings = [],
                Page = Math.Max(1, page),
                MaximumResults = Math.Max(1, maximumResults),
                OrdinaryTerm = normalizedOrdinaryTerm
            };
            unsupported = unsupported.PrepareDiagnostics();
            Telemetry.Plans.Add(
                1,
                new KeyValuePair<string, object?>("provider", Telemetry.Provider),
                new KeyValuePair<string, object?>("content_type", entityKind.ToString()),
                new KeyValuePair<string, object?>("anchor_kind", QueryStrategy.Unsupported.ToString()));
            Telemetry.PlansRefused.Add(
                1,
                new KeyValuePair<string, object?>("provider", Telemetry.Provider),
                new KeyValuePair<string, object?>("content_type", entityKind.ToString()),
                new KeyValuePair<string, object?>("reason", TerminalReason.Unbounded));
            return unsupported;
        }

        var criterionDispositions = MapProviderIdentityCriteria(criteria);
        var identityDispositionCount = criterionDispositions.Count;
        var identityExcluded = criterionDispositions.Any(value =>
            value.Disposition == CriterionDispositionKind.ProvablyUnsatisfiable);

        var filters = new List<QueryFilter>();
        var residualFilters = new List<ResidualFilter>();
        var mappedKnownEmpty = MapCriteria(
            criteria,
            entityKind,
            filters,
            residualFilters,
            criterionDispositions);
        var knownEmpty = identityExcluded || mappedKnownEmpty;
        if (identityExcluded)
        {
            filters.Clear();
            residualFilters.Clear();
            for (var index = identityDispositionCount; index < criterionDispositions.Count; index++)
            {
                criterionDispositions[index] = criterionDispositions[index] with
                {
                    Disposition = CriterionDispositionKind.Omitted,
                    Reason = "Omitted because an identity criterion already makes the Wikidata result empty."
                };
            }
        }

        var hasExactIdentitySeed = filters.Any(filter =>
            filter.Kind == QueryFilterKind.ExternalId && !filter.Negate);
        if (hasExactIdentitySeed)
        {
            PreferExactIdentityBoundForRelationshipResiduals(filters, criterionDispositions);
        }

        var hasPositiveTitleFilter = filters.Any(filter =>
            filter.Kind == QueryFilterKind.Title && !filter.Negate);
        var hasTitleSeed = normalizedOrdinaryTerm is not null || hasPositiveTitleFilter;
        var selective = normalizedOrdinaryTerm is not null || filters.Any(IsSelective);
        var strategy = knownEmpty || !selective
            ? QueryStrategy.Empty
            : hasTitleSeed && !hasExactIdentitySeed
                ? QueryStrategy.TitleSearch
                : QueryStrategy.Structured;

        if (!knownEmpty && !selective)
        {
            for (var index = 0; index < criterionDispositions.Count; index++)
            {
                var disposition = criterionDispositions[index];
                if (disposition.Disposition is not (CriterionDispositionKind.Residual
                    or CriterionDispositionKind.ServerFilterAndResidual))
                {
                    continue;
                }

                criterionDispositions[index] = disposition with
                {
                    Reason = disposition.Disposition == CriterionDispositionKind.Residual
                        ? "Planned for local residual evaluation, but no selective server bound was available."
                        : "Planned as a coarse server filter and local residual, but no selective server bound " +
                            "was available."
                };
            }
        }

        var plan = new QueryPlan
        {
            EntityKind = entityKind,
            Strategy = strategy,
            Filters = filters,
            ResidualFilters = residualFilters,
            CriterionDispositions = criterionDispositions,
            Warnings = [],
            Page = Math.Max(1, page),
            MaximumResults = Math.Max(1, maximumResults),
            UsesLocalPaging = !knownEmpty && (residualFilters.Count > 0 || hasTitleSeed),
            OrdinaryTerm = normalizedOrdinaryTerm,
            ProjectCandidateDetails = true
        };
        plan = plan.PrepareDiagnostics();
        Telemetry.Plans.Add(
            1,
            new KeyValuePair<string, object?>("provider", Telemetry.Provider),
            new KeyValuePair<string, object?>("content_type", entityKind.ToString()),
            new KeyValuePair<string, object?>("anchor_kind", strategy.ToString()));
        if (strategy == QueryStrategy.Empty)
        {
            Telemetry.PlansRefused.Add(
                1,
                new KeyValuePair<string, object?>("provider", Telemetry.Provider),
                new KeyValuePair<string, object?>("content_type", entityKind.ToString()),
                new KeyValuePair<string, object?>(
                    "reason",
                    criterionDispositions.Any(value =>
                        value.Disposition == CriterionDispositionKind.ProvablyUnsatisfiable)
                            ? TerminalReason.KnownEmpty
                            : TerminalReason.Unbounded));
        }

        return plan;
    }

    private void PreferExactIdentityBoundForRelationshipResiduals(
        List<QueryFilter> filters,
        List<CriterionDisposition> criterionDispositions)
    {
        var residualSources = criterionDispositions
            .Where(disposition => disposition.Disposition == CriterionDispositionKind.ServerFilterAndResidual)
            .Select(disposition => disposition.Source)
            .ToHashSet(StringComparer.Ordinal);
        var removedSources = filters
            .Where(filter => filter.Kind == QueryFilterKind.Relationship &&
                !filter.Negate &&
                filter.Alternatives.Any(alternative => alternative.Names.Count > 0) &&
                filter.CriterionSource is not null &&
                residualSources.Contains(filter.CriterionSource))
            .Select(filter => filter.CriterionSource)
            .ToHashSet(StringComparer.Ordinal);
        if (removedSources.Count == 0)
        {
            return;
        }

        filters.RemoveAll(filter => filter.Kind == QueryFilterKind.Relationship &&
            filter.CriterionSource is not null &&
            removedSources.Contains(filter.CriterionSource));

        for (var index = 0; index < criterionDispositions.Count; index++)
        {
            var disposition = criterionDispositions[index];
            if (!removedSources.Contains(disposition.Source))
            {
                continue;
            }

            criterionDispositions[index] = disposition with
            {
                Disposition = CriterionDispositionKind.Residual,
                Reason = "Planned for local residual evaluation behind an exact identity server bound."
            };
        }
    }

    private bool MapCriteria(
        MetadataCriteria criteria,
        EntityKind entityKind,
        List<QueryFilter> filters,
        List<ResidualFilter> residualFilters,
        List<CriterionDisposition> criterionDispositions)
    {
        var matchesNothing = false;

        void Add(string source, MappedCriterion mapped)
        {
            AddMapped(source, mapped, filters, residualFilters, criterionDispositions);
            matchesNothing |= mapped.MatchesNothing;
        }

        foreach (var (criterion, index) in criteria.Title.And.Select((value, index) => (value, index)))
        {
            Add(
                CriterionSource(nameof(MetadataCriteria.Title), "And", index),
                MapText(MetadataField.Title, nameof(MetadataCriteria.Title), criterion));
        }

        foreach (var (criterion, index) in criteria.Language.And.Select((value, index) => (value, index)))
        {
            Add(
                CriterionSource(nameof(MetadataCriteria.Language), "And", index),
                MapText(MetadataField.Language, nameof(MetadataCriteria.Language), criterion));
        }

        foreach (var (criterion, index) in criteria.PublishDate.And.Select((value, index) => (value, index)))
        {
            Add(
                CriterionSource(nameof(MetadataCriteria.PublishDate), "And", index),
                MapDate(
                    entityKind,
                    criterion,
                    QueryFilterKind.PublishDate,
                    nameof(MetadataCriteria.PublishDate)));
        }

        foreach (var (criterion, index) in criteria.OriginalTitle.And.Select((value, index) => (value, index)))
        {
            Add(
                CriterionSource(nameof(MetadataCriteria.OriginalTitle), "And", index),
                MapText(
                    MetadataField.OriginalTitle,
                    nameof(MetadataCriteria.OriginalTitle),
                    criterion));
        }

        foreach (var (criterion, index) in criteria.Overview.And.Select((value, index) => (value, index)))
        {
            Add(
                CriterionSource(nameof(MetadataCriteria.Overview), "And", index),
                MapText(MetadataField.Overview, nameof(MetadataCriteria.Overview), criterion));
        }

        foreach (var (criterion, index) in criteria.Duration.And.Select((value, index) => (value, index)))
        {
            Add(
                CriterionSource(nameof(MetadataCriteria.Duration), "And", index),
                MapDuration(criterion));
        }

        foreach (var (criterion, index) in criteria.ProductionStatus.And.Select((value, index) => (value, index)))
        {
            Add(
                CriterionSource(nameof(MetadataCriteria.ProductionStatus), "And", index),
                MapProductionStatus(entityKind, criterion));
        }

        foreach (var (criterion, index) in criteria.Established.And.Select((value, index) => (value, index)))
        {
            Add(
                CriterionSource(nameof(MetadataCriteria.Established), "And", index),
                MapDate(
                    entityKind,
                    criterion,
                    QueryFilterKind.Established,
                    nameof(MetadataCriteria.Established)));
        }

        foreach (var (criterion, index) in criteria.Dissolved.And.Select((value, index) => (value, index)))
        {
            Add(
                CriterionSource(nameof(MetadataCriteria.Dissolved), "And", index),
                MapDate(
                    entityKind,
                    criterion,
                    QueryFilterKind.Dissolved,
                    nameof(MetadataCriteria.Dissolved)));
        }

        foreach (var (criterion, index) in criteria.BirthDate.And.Select((value, index) => (value, index)))
        {
            Add(
                CriterionSource(nameof(MetadataCriteria.BirthDate), "And", index),
                MapDate(
                    entityKind,
                    criterion,
                    QueryFilterKind.BirthDate,
                    nameof(MetadataCriteria.BirthDate)));
        }

        foreach (var (criterion, index) in criteria.DeathDate.And.Select((value, index) => (value, index)))
        {
            Add(
                CriterionSource(nameof(MetadataCriteria.DeathDate), "And", index),
                MapDate(
                    entityKind,
                    criterion,
                    QueryFilterKind.DeathDate,
                    nameof(MetadataCriteria.DeathDate)));
        }

        var customMetadataIndex = 0;
        foreach (var dictionary in criteria.CustomMetadata.And)
        {
            var items = MergeCustomItems(dictionary.Items);
            foreach (var (key, criterion) in items)
            {
                Add(
                    CriterionSource(nameof(MetadataCriteria.CustomMetadata), "And", customMetadataIndex) + "." + key,
                    MapCustom(key, criterion, items));
            }

            customMetadataIndex++;
        }

        for (var index = 0; index < criteria.Genres.Some.Count; index++)
        {
            Add(
                CriterionSource(nameof(MetadataCriteria.Genres), "Some", index),
                MapGenres(criteria.Genres.Some[index], outerNegate: false));
        }

        for (var index = 0; index < criteria.Genres.None.Count; index++)
        {
            Add(
                CriterionSource(nameof(MetadataCriteria.Genres), "None", index),
                MapGenres([criteria.Genres.None[index]], outerNegate: true));
        }

        for (var index = 0; index < criteria.ExternalIds.Some.Count; index++)
        {
            Add(
                CriterionSource(nameof(MetadataCriteria.ExternalIds), "Some", index),
                MapExternalIds(criteria.ExternalIds.Some[index], entityKind, negate: false));
        }

        for (var index = 0; index < criteria.ExternalIds.None.Count; index++)
        {
            Add(
                CriterionSource(nameof(MetadataCriteria.ExternalIds), "None", index),
                MapExternalIds([criteria.ExternalIds.None[index]], entityKind, negate: true));
        }

        for (var index = 0; index < criteria.Relationships.Some.Count; index++)
        {
            Add(
                CriterionSource(nameof(MetadataCriteria.Relationships), "Some", index),
                MapRelationships(criteria.Relationships.Some[index], negate: false));
        }

        for (var index = 0; index < criteria.Relationships.None.Count; index++)
        {
            Add(
                CriterionSource(nameof(MetadataCriteria.Relationships), "None", index),
                MapRelationships([criteria.Relationships.None[index]], negate: true));
        }

        for (var index = 0; index < criteria.ContentRatings.Some.Count; index++)
        {
            var group = criteria.ContentRatings.Some[index];
            Add(
                CriterionSource(nameof(MetadataCriteria.ContentRatings), "Some", index),
                group.Count == 0 ? Ignored("The collection group is empty.") : MapContentRatings(group, false));
        }

        for (var index = 0; index < criteria.ContentRatings.None.Count; index++)
        {
            Add(
                CriterionSource(nameof(MetadataCriteria.ContentRatings), "None", index),
                MapContentRatings([criteria.ContentRatings.None[index]], outerNegate: true));
        }

        for (var index = 0; index < criteria.Awards.Some.Count; index++)
        {
            var group = criteria.Awards.Some[index];
            Add(
                CriterionSource(nameof(MetadataCriteria.Awards), "Some", index),
                group.Count == 0 ? Ignored("The collection group is empty.") : MapAwards(group, false));
        }

        for (var index = 0; index < criteria.Awards.None.Count; index++)
        {
            Add(
                CriterionSource(nameof(MetadataCriteria.Awards), "None", index),
                MapAwards([criteria.Awards.None[index]], outerNegate: true));
        }

        for (var index = 0; index < criteria.Images.Some.Count; index++)
        {
            var group = criteria.Images.Some[index];
            Add(
                CriterionSource(nameof(MetadataCriteria.Images), "Some", index),
                group.Count == 0 ? Ignored("The collection group is empty.") : MapImages(group, entityKind, false));
        }

        for (var index = 0; index < criteria.Images.None.Count; index++)
        {
            Add(
                CriterionSource(nameof(MetadataCriteria.Images), "None", index),
                MapImages([criteria.Images.None[index]], entityKind, outerNegate: true));
        }

        for (var index = 0; index < criteria.References.Some.Count; index++)
        {
            var group = criteria.References.Some[index];
            Add(
                CriterionSource(nameof(MetadataCriteria.References), "Some", index),
                group.Count == 0 ? Ignored("The collection group is empty.") : MapReferences(group, false));
        }

        for (var index = 0; index < criteria.References.None.Count; index++)
        {
            Add(
                CriterionSource(nameof(MetadataCriteria.References), "None", index),
                MapReferences([criteria.References.None[index]], outerNegate: true));
        }

        for (var index = 0; index < criteria.AlternateTexts.Some.Count; index++)
        {
            var group = criteria.AlternateTexts.Some[index];
            Add(
                CriterionSource(nameof(MetadataCriteria.AlternateTexts), "Some", index),
                group.Count == 0 ? Ignored("The collection group is empty.") : MapAlternateTexts(group, false));
        }

        for (var index = 0; index < criteria.AlternateTexts.None.Count; index++)
        {
            Add(
                CriterionSource(nameof(MetadataCriteria.AlternateTexts), "None", index),
                MapAlternateTexts([criteria.AlternateTexts.None[index]], outerNegate: true));
        }

        for (var index = 0; index < criteria.Previews.Some.Count; index++)
        {
            var group = criteria.Previews.Some[index];
            Add(
                CriterionSource(nameof(MetadataCriteria.Previews), "Some", index),
                group.Count == 0 ? Ignored("The collection group is empty.") : MapPreviews(group, false));
        }

        for (var index = 0; index < criteria.Previews.None.Count; index++)
        {
            Add(
                CriterionSource(nameof(MetadataCriteria.Previews), "None", index),
                MapPreviews([criteria.Previews.None[index]], outerNegate: true));
        }

        for (var index = 0; index < criteria.Tags.Some.Count; index++)
        {
            Add(
                CriterionSource(nameof(MetadataCriteria.Tags), "Some", index),
                KnownEmpty("Wikidata does not materialize tags."));
        }

        for (var index = 0; index < criteria.Tags.None.Count; index++)
        {
            Add(
                CriterionSource(nameof(MetadataCriteria.Tags), "None", index),
                Ignored("Wikidata does not materialize tags, so the criterion is always satisfied."));
        }

        return matchesNothing;
    }

    private void AddMapped(
        string source,
        MappedCriterion mapped,
        List<QueryFilter> filters,
        List<ResidualFilter> residualFilters,
        List<CriterionDisposition> criterionDispositions)
    {
        if (mapped.Filter is not null)
        {
            filters.Add(mapped.Filter with { CriterionSource = source });
        }

        if (mapped.ResidualFilter is not null)
        {
            residualFilters.Add(mapped.ResidualFilter);
        }

        criterionDispositions.Add(ToDisposition(source, mapped));
    }

    private MappedCriterion MapText(
        MetadataField field,
        string propertyName,
        PropertyCriterion<string> criterion)
    {
        var values = TextValues(criterion);
        var (op, negate) = InterpretOperator(criterion.Operator, criterion.Negate);
        if (values.Length > 0 &&
            op == CriteriaValueOperator.Regex &&
            field is MetadataField.Title or MetadataField.OriginalTitle or MetadataField.Overview)
        {
            var textField = field switch
            {
                MetadataField.Title => ResidualTextField.Title,
                MetadataField.OriginalTitle => ResidualTextField.OriginalTitle,
                _ => ResidualTextField.Overview
            };
            return Residual(new ResidualFilter
            {
                Kind = ResidualFilterKind.Text,
                Source = propertyName,
                Hydration = field == MetadataField.OriginalTitle
                    ? ResidualHydration.Entity | ResidualHydration.Support
                    : ResidualHydration.None,
                TextField = textField,
                Criterion = StringCriterion(op, negate, values)
            });
        }

        if (field == MetadataField.Title &&
            values.Length > 0 &&
            op is CriteriaValueOperator.Equals
                or CriteriaValueOperator.In
                or CriteriaValueOperator.ContainsString
                or CriteriaValueOperator.ContainsWord
                or CriteriaValueOperator.StartsWithString
                or CriteriaValueOperator.EndsWithString)
        {
            return Supported(
                new QueryFilter
                {
                    Kind = QueryFilterKind.Title,
                    Operator = op,
                    Negate = negate,
                    Values = values
                });
        }

        if (field is MetadataField.OriginalTitle or MetadataField.Overview &&
            (op is CriteriaValueOperator.IsNull or CriteriaValueOperator.IsNotNull ||
                values.Length > 0 &&
                op is CriteriaValueOperator.Equals
                    or CriteriaValueOperator.In
                    or CriteriaValueOperator.ContainsString
                    or CriteriaValueOperator.StartsWithString
                    or CriteriaValueOperator.EndsWithString))
        {
            return Supported(new QueryFilter
            {
                Kind = field == MetadataField.OriginalTitle
                    ? QueryFilterKind.OriginalTitle
                    : QueryFilterKind.Overview,
                Operator = op,
                Negate = negate,
                Values = values
            });
        }

        if (field == MetadataField.Language)
        {
            if (op is CriteriaValueOperator.IsNull or CriteriaValueOperator.IsNotNull)
            {
                return Supported(new QueryFilter
                {
                    Kind = QueryFilterKind.Language,
                    Operator = op,
                    Negate = negate
                });
            }

            if (values.Length == 0 || op is not (CriteriaValueOperator.Equals or CriteriaValueOperator.In))
            {
                return Unsupported(propertyName);
            }

            var qids = new List<string>();
            var codes = new List<string>();
            var names = new List<string>();
            foreach (var value in values)
            {
                if (value.TryNormalizeQid(out var qid))
                {
                    qids.Add(qid);
                }
                else if (TryNormalizeLanguageCode(value, out var code))
                {
                    codes.Add(code);
                }
                else
                {
                    names.Add(value);
                }
            }

            if (names.Count > 0)
            {
                return Residual(new ResidualFilter
                {
                    Kind = ResidualFilterKind.Language,
                    Source = propertyName,
                    Hydration = ResidualHydration.Entity | ResidualHydration.Support,
                    Criterion = StringCriterion(op, negate, values),
                    LanguageCodes = [.. codes.Distinct(StringComparer.Ordinal)],
                    LanguageQids = [.. qids.Distinct(StringComparer.Ordinal)],
                    LanguageNames = [.. names.Distinct(StringComparer.OrdinalIgnoreCase)]
                });
            }

            return Supported(new QueryFilter
            {
                Kind = QueryFilterKind.Language,
                Operator = op,
                Negate = negate,
                Values = [.. codes.Distinct(StringComparer.Ordinal)],
                LanguageQids = [.. qids.Distinct(StringComparer.Ordinal)]
            });
        }

        return Unsupported(propertyName);
    }

    private MappedCriterion MapDate(
        EntityKind entityKind,
        PropertyCriterion<TemporalValue> criterion,
        QueryFilterKind kind,
        string propertyName)
    {
        var (op, negate) = InterpretOperator(criterion.Operator, criterion.Negate);
        var propertyId = FilterPropertyId(entityKind, kind);
        if (propertyId is null)
        {
            return Inapplicable(op, negate);
        }

        var values = TemporalValues(criterion);
        var supportedOperator = op is CriteriaValueOperator.Equals
            or CriteriaValueOperator.In
            or CriteriaValueOperator.GreaterThan
            or CriteriaValueOperator.GreaterThanOrEqual
            or CriteriaValueOperator.LessThan
            or CriteriaValueOperator.LessThanOrEqual
            or CriteriaValueOperator.IsNull
            or CriteriaValueOperator.IsNotNull;
        if (!supportedOperator ||
            op is not (CriteriaValueOperator.IsNull or CriteriaValueOperator.IsNotNull) &&
            values.Length == 0)
        {
            return Unsupported(propertyName);
        }

        return Supported(new QueryFilter
        {
            Kind = kind,
            Operator = op,
            Negate = negate,
            PropertyId = propertyId,
            TemporalValues = values
        });
    }

    private MappedCriterion MapDuration(PropertyCriterion<TimeSpan> criterion)
    {
        var (op, negate) = InterpretOperator(criterion.Operator, criterion.Negate);
        var supportedOperator = op is CriteriaValueOperator.Equals
            or CriteriaValueOperator.In
            or CriteriaValueOperator.GreaterThan
            or CriteriaValueOperator.GreaterThanOrEqual
            or CriteriaValueOperator.LessThan
            or CriteriaValueOperator.LessThanOrEqual
            or CriteriaValueOperator.IsNull
            or CriteriaValueOperator.IsNotNull;
        if (!supportedOperator)
        {
            return Unsupported(nameof(MetadataCriteria.Duration));
        }

        var equality = op is CriteriaValueOperator.Equals or CriteriaValueOperator.In;
        var values = equality
            ? [.. DurationValues(criterion).Where(value => value > TimeSpan.Zero)]
            : DurationValues(criterion);
        if (equality && values.Count == 0)
        {
            return negate ? Ignored() : KnownEmpty();
        }

        if (op is not (CriteriaValueOperator.IsNull or CriteriaValueOperator.IsNotNull) &&
            values.Count == 0)
        {
            return Unsupported(nameof(MetadataCriteria.Duration));
        }

        return Supported(new QueryFilter
        {
            Kind = QueryFilterKind.Duration,
            Operator = op,
            Negate = negate,
            DurationValues = values
        });
    }

    private MappedCriterion MapProductionStatus(
        EntityKind entityKind,
        PropertyCriterion<string> criterion)
    {
        var (op, negate) = InterpretOperator(criterion.Operator, criterion.Negate);
        var propertyId = FilterPropertyId(entityKind, QueryFilterKind.ProductionStatus);
        if (propertyId is null)
        {
            return Inapplicable(op, negate);
        }

        if (op is CriteriaValueOperator.IsNull or CriteriaValueOperator.IsNotNull)
        {
            return Supported(new QueryFilter
            {
                Kind = QueryFilterKind.ProductionStatus,
                Operator = op,
                Negate = negate,
                PropertyId = propertyId
            });
        }

        var values = TextValues(criterion);
        if (values.Length == 0 || op is not (CriteriaValueOperator.Equals or CriteriaValueOperator.In))
        {
            return Unsupported(nameof(MetadataCriteria.ProductionStatus));
        }

        if (values.All(value => string.Equals(
            value,
            Constants.EndedProductionStatus,
            StringComparison.OrdinalIgnoreCase)))
        {
            return Supported(new QueryFilter
            {
                Kind = QueryFilterKind.ProductionStatus,
                Operator = op,
                Negate = negate,
                PropertyId = propertyId,
                Values = values
            });
        }

        return Residual(new ResidualFilter
        {
            Kind = ResidualFilterKind.ProductionStatus,
            Source = nameof(MetadataCriteria.ProductionStatus),
            Hydration = ResidualHydration.None,
            Criterion = StringCriterion(op, negate, values)
        });
    }

    private string? FilterPropertyId(
        EntityKind entityKind,
        QueryFilterKind filterKind) => (entityKind, filterKind) switch
        {
            (EntityKind.Movie, QueryFilterKind.PublishDate) =>
                Constants.PublicationDatePropertyId,
            (EntityKind.Show,
                QueryFilterKind.PublishDate or QueryFilterKind.Established) =>
                Constants.SeriesStartPropertyId,
            (EntityKind.Show,
                QueryFilterKind.Dissolved or QueryFilterKind.ProductionStatus) =>
                Constants.SeriesEndPropertyId,
            (EntityKind.Person, QueryFilterKind.BirthDate) =>
                Constants.BirthDatePropertyId,
            (EntityKind.Person, QueryFilterKind.DeathDate) =>
                Constants.DeathDatePropertyId,
            (EntityKind.Studio, QueryFilterKind.Established) =>
                Constants.InceptionPropertyId,
            (EntityKind.Studio, QueryFilterKind.Dissolved) =>
                Constants.DissolutionPropertyId,
            _ => null
        };

    private MappedCriterion MapGenres(
        IReadOnlyList<GenreCriteria> alternatives,
        bool outerNegate) => MapCollectionGroup(
            alternatives,
            outerNegate,
            ResidualCollectionKind.Genre,
            QueryFilterKind.Genre,
            nameof(MetadataCriteria.Genres),
            ResidualHydration.None,
            CompileGenreAlternative);

    private CollectionAlternativeCompilation CompileGenreAlternative(GenreCriteria alternative)
    {
        var compilation = new CollectionAlternativeCompilation();
        var values = CompileDynamicStringField(
            alternative.Name,
            ResidualCollectionField.GenreName,
            nameof(MetadataCriteria.Genres) + "." + nameof(GenreCriteria.Name),
            alwaysNonNull: true,
            allowRemoteExact: true,
            compilation);
        if (compilation.MatchesNothing || compilation.UnsupportedField is not null)
        {
            return compilation;
        }

        SplitEntityValues(values, out var qids, out var names);
        SplitEntityPredicateValues(compilation, ResidualCollectionField.GenreName);
        compilation.RequiresResidual |= compilation.Predicates.Any(predicate =>
            predicate.Field == ResidualCollectionField.GenreName &&
            (predicate.Operator is not (CriteriaValueOperator.Equals or CriteriaValueOperator.In) ||
                predicate.Negate ||
                predicate.StringValues.Count > 0));
        compilation.Coarse = new SubjectFilter
        {
            PropertyIds = [Constants.GenrePropertyId],
            Qids = qids,
            Names = names
        };
        return compilation;
    }

    private MappedCriterion MapExternalIds(
        IReadOnlyList<ExternalIdCriteria> alternatives,
        EntityKind entityKind,
        bool negate)
    {
        var mapped = new List<SubjectFilter>();
        foreach (var alternative in alternatives)
        {
            if (!TrySimpleValues(alternative.Value, out var values) ||
                !TryOptionalValues(alternative.Source, out var sources))
            {
                if (!negate && alternatives.Count > 1)
                {
                    continue;
                }

                return Unsupported(nameof(MetadataCriteria.ExternalIds), negate);
            }

            var qids = new List<string>();
            var externalIds = new List<PropertyValue>();
            foreach (var source in SourcesOrUnspecified(sources))
            {
                foreach (var value in values)
                {
                    if (!TryMapExternalValue(entityKind, source, value, out var qid, out var propertyValue))
                    {
                        if (negate)
                        {
                            return Unsupported(nameof(MetadataCriteria.ExternalIds), negate);
                        }

                        continue;
                    }

                    if (qid is not null)
                    {
                        qids.Add(qid);
                    }

                    if (propertyValue is not null)
                    {
                        externalIds.Add(propertyValue);
                    }
                }
            }

            if (qids.Count == 0 && externalIds.Count == 0)
            {
                if (!negate && alternatives.Count > 1)
                {
                    continue;
                }

                return Unsupported(nameof(MetadataCriteria.ExternalIds), negate);
            }

            mapped.Add(new SubjectFilter
            {
                Qids = [.. qids.Distinct(StringComparer.Ordinal)],
                ExternalIds = [.. externalIds.Distinct()]
            });
        }

        if (mapped.Count == 0)
        {
            return Unsupported(nameof(MetadataCriteria.ExternalIds), negate);
        }

        return Supported(new QueryFilter
        {
            Kind = QueryFilterKind.ExternalId,
            Negate = negate,
            Alternatives = mapped
        });
    }

    private MappedCriterion MapRelationships(
        IReadOnlyList<RelationshipCriteria> alternatives,
        bool negate)
    {
        var applicable = new List<RelationshipCriteria>();
        var cannotCompile = false;
        foreach (var alternative in alternatives)
        {
            if (alternative.RelatedMetadataContainerId.And.Any())
            {
                continue;
            }

            if (TryRelationshipProperties(alternative, out _, out var propertiesCannotCompile))
            {
                applicable.Add(alternative);
            }
            else
            {
                cannotCompile |= propertiesCannotCompile;
            }
        }

        if (applicable.Count == 0)
        {
            return cannotCompile
                ? Unsupported(nameof(MetadataCriteria.Relationships), negate)
                : InapplicableRelationships(negate);
        }

        if (TryCompileResidualRelationshipGroup(
            applicable,
            negate,
            out var residual,
            out var coarseFilter))
        {
            return new MappedCriterion(
                Filter: coarseFilter,
                ResidualFilter: residual);
        }

        var mapped = new List<SubjectFilter>();
        var mappedCriteria = new List<RelationshipCriteria>();
        foreach (var alternative in applicable)
        {
            if (!TryMapRelationshipAlternative(
                alternative,
                allowFuzzySubjectNames: !negate,
                out var mappedAlternative,
                out var residualAlternative))
            {
                if (!negate && applicable.Count > 1)
                {
                    continue;
                }

                return TryCompileResidualRelationshipGroup(
                    applicable,
                    negate,
                    out residual,
                    out coarseFilter)
                    ? new MappedCriterion(
                        Filter: coarseFilter,
                        ResidualFilter: residual)
                    : Unsupported(nameof(MetadataCriteria.Relationships), negate);
            }

            if (RequiresRelationshipResidual(residualAlternative, negate) &&
                !TryCompileResidualRelationshipAlternative(residualAlternative, out _))
            {
                if (!negate && applicable.Count > 1)
                {
                    continue;
                }

                return Unsupported(nameof(MetadataCriteria.Relationships), negate);
            }

            mapped.Add(mappedAlternative);
            mappedCriteria.Add(residualAlternative);
        }

        if (mapped.Count == 0)
        {
            return Unsupported(nameof(MetadataCriteria.Relationships), negate);
        }

        if (TryCompileResidualRelationshipGroup(
            mappedCriteria,
            negate,
            out residual,
            out coarseFilter))
        {
            return new MappedCriterion(
                Filter: coarseFilter,
                ResidualFilter: residual);
        }

        return Supported(new QueryFilter
        {
            Kind = QueryFilterKind.Relationship,
            Negate = negate,
            Alternatives = mapped
        });
    }

    private bool TryMapRelationshipAlternative(
        RelationshipCriteria alternative,
        bool allowFuzzySubjectNames,
        [NotNullWhen(true)] out SubjectFilter? mapped,
        [NotNullWhen(true)] out RelationshipCriteria? residualAlternative)
    {
        mapped = null;
        residualAlternative = null;
        if (!TryRelationshipProperties(alternative, out var propertyIds, out _) ||
            !TryOptionalRelationshipSubjectValues(
                alternative.Subject,
                out var subjectValues,
                out var subjectOperator) ||
            alternative.ExternalIds.None.Count > 0 ||
            alternative.RelatedMetadataContainerId.And.Any())
        {
            return false;
        }

        var exactSubjectOperator = subjectOperator is CriteriaValueOperator.Equals or CriteriaValueOperator.In;
        if (!allowFuzzySubjectNames && !exactSubjectOperator)
        {
            return false;
        }

        var qids = new List<string>();
        var names = new List<string>();
        foreach (var value in subjectValues)
        {
            if (exactSubjectOperator && value.TryNormalizeQid(out var qid))
            {
                qids.Add(qid);
            }
            else
            {
                names.Add(value);
            }
        }

        if (names.Count > 5)
        {
            if (!allowFuzzySubjectNames || qids.Count == 0)
            {
                return false;
            }

            names.Clear();
            alternative = alternative with
            {
                Subject = new()
                {
                    And = [alternative.Subject.And.Single() with { Value = null, Values = qids }]
                }
            };
        }

        var externalIdGroups = new List<ExternalIdGroup>();
        var residualExternalIdGroups = new List<List<ExternalIdCriteria>>();
        foreach (var group in alternative.ExternalIds.Some)
        {
            var qidGroup = new List<string>();
            var externalIdGroup = new List<PropertyValue>();
            var residualExternalIds = new List<ExternalIdCriteria>();
            foreach (var external in group)
            {
                if (!TrySimpleValues(external.Value, out var values) ||
                    !TryOptionalValues(external.Source, out var sources))
                {
                    return false;
                }

                if (!allowFuzzySubjectNames &&
                    sources.Any(source => !IsSupportedSubjectExternalSource(source)))
                {
                    return false;
                }

                var supportedSources = sources.Where(IsSupportedSubjectExternalSource).ToArray();
                var mappedValues = new HashSet<string>(StringComparer.Ordinal);
                var mappedSources = sources.Count == 0
                    ? SourcesOrUnspecified(sources)
                    : supportedSources.Select(source => (string?)source);
                foreach (var source in mappedSources)
                {
                    foreach (var value in values)
                    {
                        if (!TryMapSubjectExternalValue(source, value, out var qid, out var mappedExternals))
                        {
                            continue;
                        }

                        if (qid is not null)
                        {
                            qidGroup.Add(qid);
                        }

                        mappedValues.Add(value);
                        externalIdGroup.AddRange(mappedExternals);
                    }
                }

                if (mappedValues.Count > 0)
                {
                    residualExternalIds.Add(external with
                    {
                        Source = sources.Count == 0
                            ? external.Source
                            : new CriteriaSet<PropertyCriterion<string>>
                            {
                                And = [external.Source.And.Single() with
                                {
                                    Value = null,
                                    Values = supportedSources
                                }]
                            },
                        Value = new()
                        {
                            And = [external.Value.And.Single() with
                            {
                                Value = null,
                                Values = mappedValues
                            }]
                        }
                    });
                }
            }

            if (qidGroup.Count == 0 && externalIdGroup.Count == 0 || residualExternalIds.Count == 0)
            {
                return false;
            }

            externalIdGroups.Add(new ExternalIdGroup
            {
                Qids = [.. qidGroup.Distinct(StringComparer.Ordinal)],
                ExternalIds = [.. externalIdGroup.Distinct()]
            });
            residualExternalIdGroups.Add(residualExternalIds);
        }

        mapped = new SubjectFilter
        {
            PropertyIds = propertyIds,
            Qids = qids,
            Names = names,
            ExternalIdGroups = externalIdGroups
        };
        residualAlternative = alternative with
        {
            ExternalIds = alternative.ExternalIds with { Some = residualExternalIdGroups }
        };
        return true;
    }

    private bool TryCompileResidualRelationshipGroup(
        IReadOnlyList<RelationshipCriteria> alternatives,
        bool outerNegate,
        out ResidualFilter? residual,
        out QueryFilter? coarseFilter)
    {
        residual = null;
        coarseFilter = null;
        if (!alternatives.Any(alternative => RequiresRelationshipResidual(alternative, outerNegate)))
        {
            return false;
        }

        var compiled = new List<ResidualRelationshipAlternative>();
        foreach (var alternative in alternatives)
        {
            if (!TryCompileResidualRelationshipAlternative(alternative, out var compiledAlternative))
            {
                return false;
            }

            compiled.Add(compiledAlternative);
        }

        residual = new ResidualFilter
        {
            Kind = ResidualFilterKind.Relationship,
            Source = nameof(MetadataCriteria.Relationships),
            Hydration = ResidualHydration.Entity | ResidualHydration.Support,
            RelationshipGroup = new()
            {
                OuterNegate = outerNegate,
                Alternatives = compiled
            }
        };
        if (!outerNegate)
        {
            coarseFilter = new QueryFilter
            {
                Kind = QueryFilterKind.Relationship,
                Alternatives =
                [
                    .. compiled.Select(CoarseRelationshipAlternative)
                ]
            };
        }

        return true;
    }

    private SubjectFilter CoarseRelationshipAlternative(
        ResidualRelationshipAlternative alternative)
    {
        var externalIdGroups = alternative.ExternalIdGroups
            .Select(group => new ExternalIdGroup
            {
                Qids = group.Qids,
                ExternalIds = group.ExternalIds
            })
            .Where(group => group.Qids.Count > 0 || group.ExternalIds.Count > 0)
            .ToArray();
        var exactSubject = alternative.Subjects.FirstOrDefault(subject =>
            subject.Criterion?.Negate == false &&
            subject.Qids.Count > 0 &&
            subject.Names.Count == 0);
        var positiveSubject = exactSubject ??
            alternative.Subjects.FirstOrDefault(subject =>
                subject.Criterion?.Negate == false);
        var useSubjectSelector = exactSubject is not null || externalIdGroups.Length == 0;
        var useNames = useSubjectSelector && exactSubject is null;
        return new SubjectFilter
        {
            PropertyIds = alternative.PropertyIds,
            Qids = useSubjectSelector ? positiveSubject?.Qids ?? [] : [],
            Names = useNames ? positiveSubject?.Names ?? [] : [],
            ExternalIdGroups = externalIdGroups
        };
    }

    private bool RequiresRelationshipResidual(RelationshipCriteria alternative, bool outerNegate)
    {
        if (alternative.Qualifier.And.Any())
        {
            return true;
        }

        var subjectCriteria = alternative.Subject.And.ToArray();
        if (subjectCriteria.Length == 0)
        {
            return false;
        }

        if (subjectCriteria.Length > 1)
        {
            return true;
        }

        var (op, negate) = InterpretOperator(subjectCriteria[0].Operator, subjectCriteria[0].Negate);
        if (negate || outerNegate || IsFuzzySubjectOperator(op))
        {
            return true;
        }

        return TextValues(subjectCriteria[0]).Any(value => !value.TryNormalizeQid(out _));
    }

    private bool TryCompileResidualRelationshipAlternative(
        RelationshipCriteria alternative,
        [NotNullWhen(true)] out ResidualRelationshipAlternative? compiled)
    {
        compiled = null;
        if (!TryRelationshipProperties(alternative, out var propertyIds, out _) ||
            alternative.RelatedMetadataContainerId.And.Any() ||
            alternative.ExternalIds.None.Count > 0)
        {
            return false;
        }

        var subjects = new List<ResidualRelationshipFacet>();
        var subjectCriteria = alternative.Subject.And.ToArray();
        foreach (var criterion in subjectCriteria)
        {
            var (op, negate) = InterpretOperator(criterion.Operator, criterion.Negate);
            var values = TextValues(criterion);
            if (values.Length == 0 ||
                op is not (CriteriaValueOperator.Equals
                    or CriteriaValueOperator.In
                    or CriteriaValueOperator.ContainsString
                    or CriteriaValueOperator.ContainsWord
                    or CriteriaValueOperator.StartsWithString
                    or CriteriaValueOperator.EndsWithString))
            {
                return false;
            }

            var qids = new List<string>();
            var names = new List<string>();
            foreach (var value in values)
            {
                if (op is CriteriaValueOperator.Equals or CriteriaValueOperator.In &&
                    value.TryNormalizeQid(out var qid))
                {
                    qids.Add(qid);
                }
                else
                {
                    names.Add(value);
                }
            }

            if (names.Count > 5)
            {
                return false;
            }

            subjects.Add(new ResidualRelationshipFacet
            {
                Criterion = StringCriterion(op, negate, values),
                Qids = qids,
                Names = names
            });
        }

        var qualifiers = new List<ResidualRelationshipFacet>();
        foreach (var criterion in alternative.Qualifier.And)
        {
            if (!TryCompileResidualStringCriterion(criterion, out var compiledCriterion))
            {
                return false;
            }

            qualifiers.Add(new ResidualRelationshipFacet { Criterion = compiledCriterion });
        }

        var externalIdGroups = new List<ResidualRelationshipFacet>();
        foreach (var group in alternative.ExternalIds.Some)
        {
            var qids = new List<string>();
            var externalIds = new List<PropertyValue>();
            foreach (var external in group)
            {
                if (!TrySimpleValues(external.Value, out var values) ||
                    !TryOptionalValues(external.Source, out var sources))
                {
                    return false;
                }

                if (sources.Any(source => !IsSupportedSubjectExternalSource(source)))
                {
                    return false;
                }

                foreach (var source in SourcesOrUnspecified(sources))
                {
                    foreach (var value in values)
                    {
                        if (!TryMapSubjectExternalValue(source, value, out var qid, out var mappedExternals))
                        {
                            continue;
                        }

                        if (qid is not null)
                        {
                            qids.Add(qid);
                        }

                        externalIds.AddRange(mappedExternals);
                    }
                }
            }

            if (qids.Count == 0 && externalIds.Count == 0)
            {
                return false;
            }

            externalIdGroups.Add(new ResidualRelationshipFacet
            {
                Qids = [.. qids.Distinct(StringComparer.Ordinal)],
                ExternalIds = [.. externalIds.Distinct()]
            });
        }

        compiled = new ResidualRelationshipAlternative
        {
            PropertyIds = propertyIds,
            Subjects = subjects,
            Qualifiers = qualifiers,
            ExternalIdGroups = externalIdGroups
        };
        return true;
    }

    private bool TryCompileResidualStringCriterion(
        PropertyCriterion<string> criterion,
        [NotNullWhen(true)] out ResidualStringCriterion? compiled)
    {
        compiled = null;
        var (op, negate) = InterpretOperator(criterion.Operator, criterion.Negate);
        var values = TextValues(criterion);
        var supported = op is CriteriaValueOperator.Equals
            or CriteriaValueOperator.In
            or CriteriaValueOperator.IsNull
            or CriteriaValueOperator.IsNotNull
            or CriteriaValueOperator.ContainsString
            or CriteriaValueOperator.ContainsWord
            or CriteriaValueOperator.StartsWithString
            or CriteriaValueOperator.EndsWithString
            or CriteriaValueOperator.Regex;
        if (!supported ||
            op is not (CriteriaValueOperator.IsNull or CriteriaValueOperator.IsNotNull) &&
            values.Length == 0)
        {
            return false;
        }

        compiled = StringCriterion(op, negate, values);
        return true;
    }

    private bool IsFuzzySubjectOperator(CriteriaValueOperator valueOperator) =>
        valueOperator is CriteriaValueOperator.ContainsString
            or CriteriaValueOperator.ContainsWord
            or CriteriaValueOperator.StartsWithString
            or CriteriaValueOperator.EndsWithString;

    private MappedCriterion MapContentRatings(
        IReadOnlyList<ContentRatingCriteria> alternatives,
        bool outerNegate) => MapCollectionGroup(
            alternatives,
            outerNegate,
            ResidualCollectionKind.ContentRating,
            QueryFilterKind.ContentRating,
            nameof(MetadataCriteria.ContentRatings),
            ResidualHydration.Entity | ResidualHydration.Support,
            CompileContentRatingAlternative);

    private CollectionAlternativeCompilation CompileContentRatingAlternative(
        ContentRatingCriteria alternative)
    {
        var compilation = new CollectionAlternativeCompilation();
        var systems = Constants.RatingSystems.ToList();
        foreach (var criterion in alternative.Body.And)
        {
            if (!TryCollectionStringPredicate(
                criterion,
                ResidualCollectionField.ContentRatingBody,
                nameof(MetadataCriteria.ContentRatings) + "." + nameof(ContentRatingCriteria.Body),
                out var predicate,
                out var field))
            {
                compilation.UnsupportedField ??= field;
                continue;
            }

            compilation.Predicates.Add(predicate);
            var unverifiable = false;
            systems = [.. systems.Where(system =>
            {
                var match = CriteriaMatcher.Match(
                    system.Body,
                    predicate.Operator,
                    predicate.StringValues,
                    predicate.Negate);
                unverifiable |= match == CriteriaMatchResult.Unverifiable;
                return match != CriteriaMatchResult.Rejected;
            })];
            compilation.RequiresResidual |= unverifiable;
            compilation.ValueBearing |= IsValueBearing(predicate);
        }

        if (systems.Count == 0)
        {
            compilation.MatchesNothing = true;
            return compilation;
        }

        var ratingValues = CompileDynamicStringField(
            alternative.Rating,
            ResidualCollectionField.ContentRatingRating,
            nameof(MetadataCriteria.ContentRatings) + "." + nameof(ContentRatingCriteria.Rating),
            alwaysNonNull: true,
            allowRemoteExact: true,
            compilation);
        if (compilation.MatchesNothing || compilation.UnsupportedField is not null)
        {
            return compilation;
        }

        SplitEntityValues(ratingValues, out var qids, out var names);
        SplitEntityPredicateValues(compilation, ResidualCollectionField.ContentRatingRating);
        compilation.RequiresResidual |= compilation.Predicates.Any(predicate =>
            predicate.Field == ResidualCollectionField.ContentRatingRating &&
            predicate.StringValues.Count > 0);
        compilation.Coarse = new SubjectFilter
        {
            PropertyIds = [.. systems.Select(system => system.PropertyId)],
            Qids = qids,
            Names = names
        };
        return compilation;
    }

    private MappedCriterion MapAwards(
        IReadOnlyList<AwardCriteria> alternatives,
        bool outerNegate) => MapCollectionGroup(
            alternatives,
            outerNegate,
            ResidualCollectionKind.Award,
            QueryFilterKind.Award,
            nameof(MetadataCriteria.Awards),
            ResidualHydration.Entity | ResidualHydration.Support,
            CompileAwardAlternative);

    private CollectionAlternativeCompilation CompileAwardAlternative(AwardCriteria alternative)
    {
        var compilation = new CollectionAlternativeCompilation();
        CompileDynamicStringField(
            alternative.Body,
            ResidualCollectionField.AwardBody,
            nameof(MetadataCriteria.Awards) + "." + nameof(AwardCriteria.Body),
            alwaysNonNull: true,
            allowRemoteExact: false,
            compilation);
        var nameValues = CompileDynamicStringField(
            alternative.Name,
            ResidualCollectionField.AwardName,
            nameof(MetadataCriteria.Awards) + "." + nameof(AwardCriteria.Name),
            alwaysNonNull: true,
            allowRemoteExact: true,
            compilation);
        CompileKnownNullStringField(
            alternative.Category,
            nameof(MetadataCriteria.Awards) + "." + nameof(AwardCriteria.Category),
            compilation);
        CompileKnownStringField(
            alternative.Status,
            ResidualCollectionField.AwardStatus,
            nameof(MetadataCriteria.Awards) + "." + nameof(AwardCriteria.Status),
            Constants.WonAwardStatus,
            compilation);
        CompileDynamicIntegerField(
            alternative.Year,
            ResidualCollectionField.AwardYear,
            nameof(MetadataCriteria.Awards) + "." + nameof(AwardCriteria.Year),
            alwaysNonNull: false,
            compilation);
        CompileKnownNullStringField(
            alternative.Language,
            nameof(MetadataCriteria.Awards) + "." + nameof(AwardCriteria.Language),
            compilation);
        CompileKnownNullStringField(
            alternative.Country,
            nameof(MetadataCriteria.Awards) + "." + nameof(AwardCriteria.Country),
            compilation);
        if (compilation.MatchesNothing || compilation.UnsupportedField is not null)
        {
            return compilation;
        }

        SplitEntityValues(nameValues, out var qids, out var names);
        SplitEntityPredicateValues(compilation, ResidualCollectionField.AwardName);
        compilation.RequiresResidual |= compilation.Predicates.Any(predicate =>
            predicate.Field == ResidualCollectionField.AwardName &&
            predicate.StringValues.Count > 0);
        compilation.Coarse = new SubjectFilter
        {
            PropertyIds = [Constants.AwardReceivedPropertyId],
            Qids = qids,
            Names = names
        };
        return compilation;
    }

    private MappedCriterion MapImages(
        IReadOnlyList<ImageCriteria> alternatives,
        EntityKind entityKind,
        bool outerNegate) => MapCollectionGroup(
            alternatives,
            outerNegate,
            ResidualCollectionKind.Image,
            QueryFilterKind.Image,
            nameof(MetadataCriteria.Images),
            ResidualHydration.Entity,
            alternative => CompileImageAlternative(alternative, entityKind));

    private CollectionAlternativeCompilation CompileImageAlternative(
        ImageCriteria alternative,
        EntityKind entityKind)
    {
        var compilation = new CollectionAlternativeCompilation();
        var properties = entityKind is EntityKind.Movie or EntityKind.Show
            ? new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
            {
                [Constants.PosterPropertyId] = ["poster"],
                [Constants.ImagePropertyId] = ["poster", "backdrop"],
                [Constants.LogoPropertyId] = ["logo"]
            }
            : [];
        foreach (var criterion in alternative.Type.And)
        {
            if (!TryCollectionStringPredicate(
                criterion,
                ResidualCollectionField.ImageType,
                nameof(MetadataCriteria.Images) + "." + nameof(ImageCriteria.Type),
                out var predicate,
                out var field))
            {
                compilation.UnsupportedField ??= field;
                continue;
            }

            compilation.Predicates.Add(predicate);
            var filtered = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
            foreach (var (propertyId, possibleTypes) in properties)
            {
                var results = possibleTypes.Select(type => CriteriaMatcher.Match(
                    type,
                    predicate.Operator,
                    predicate.StringValues,
                    predicate.Negate)).ToArray();
                if (results.Any(result => result != CriteriaMatchResult.Rejected))
                {
                    filtered[propertyId] = possibleTypes;
                    compilation.RequiresResidual |= results.Any(result =>
                        result != CriteriaMatchResult.Matched);
                }
            }

            properties = filtered;
            compilation.ValueBearing |= IsValueBearing(predicate);
        }

        if (properties.Count == 0)
        {
            compilation.MatchesNothing = true;
            return compilation;
        }

        CompileKnownNullStringField(
            alternative.MimeType,
            nameof(MetadataCriteria.Images) + "." + nameof(ImageCriteria.MimeType),
            compilation);
        CompileDynamicStringField(
            alternative.Url,
            ResidualCollectionField.ImageUrl,
            nameof(MetadataCriteria.Images) + "." + nameof(ImageCriteria.Url),
            alwaysNonNull: true,
            allowRemoteExact: false,
            compilation);
        CompileKnownStringField(
            alternative.Source,
            ResidualCollectionField.ImageSource,
            nameof(MetadataCriteria.Images) + "." + nameof(ImageCriteria.Source),
            Constants.ProviderType,
            compilation);
        CompileKnownNullStringField(
            alternative.Language,
            nameof(MetadataCriteria.Images) + "." + nameof(ImageCriteria.Language),
            compilation);
        CompileKnownNullIntegerField(
            alternative.Width,
            nameof(MetadataCriteria.Images) + "." + nameof(ImageCriteria.Width),
            compilation);
        CompileKnownNullIntegerField(
            alternative.Height,
            nameof(MetadataCriteria.Images) + "." + nameof(ImageCriteria.Height),
            compilation);
        if (compilation.MatchesNothing || compilation.UnsupportedField is not null)
        {
            return compilation;
        }

        compilation.Coarse = new SubjectFilter { PropertyIds = [.. properties.Keys] };
        return compilation;
    }

    private MappedCriterion MapReferences(
        IReadOnlyList<ReferenceMetadataCriteria> alternatives,
        bool outerNegate) => MapCollectionGroup(
            alternatives,
            outerNegate,
            ResidualCollectionKind.Reference,
            QueryFilterKind.Reference,
            nameof(MetadataCriteria.References),
            ResidualHydration.Entity,
            CompileReferenceAlternative);

    private CollectionAlternativeCompilation CompileReferenceAlternative(
        ReferenceMetadataCriteria alternative)
    {
        var compilation = new CollectionAlternativeCompilation();
        CompileDynamicIntegerField(
            alternative.Ordinal,
            ResidualCollectionField.ReferenceOrdinal,
            nameof(MetadataCriteria.References) + "." + nameof(ReferenceMetadataCriteria.Ordinal),
            alwaysNonNull: true,
            compilation);
        CompileKnownStringField(
            alternative.Kind,
            ResidualCollectionField.ReferenceKind,
            nameof(MetadataCriteria.References) + "." + nameof(ReferenceMetadataCriteria.Kind),
            "official",
            compilation);
        var uriValues = CompileDynamicUriField(
            alternative.Url,
            nameof(MetadataCriteria.References) + "." + nameof(ReferenceMetadataCriteria.Url),
            compilation);
        CompileKnownNullStringField(
            alternative.DisplayName,
            nameof(MetadataCriteria.References) + "." + nameof(ReferenceMetadataCriteria.DisplayName),
            compilation);
        CompileKnownNullStringField(
            alternative.Language,
            nameof(MetadataCriteria.References) + "." + nameof(ReferenceMetadataCriteria.Language),
            compilation);
        if (compilation.MatchesNothing || compilation.UnsupportedField is not null)
        {
            return compilation;
        }

        compilation.Coarse = new SubjectFilter
        {
            PropertyIds = [Constants.OfficialWebsitePropertyId],
            UriSchemes = uriValues.Count == 0 ? [Uri.UriSchemeHttp, Uri.UriSchemeHttps] : [],
            UriValues = uriValues
        };
        return compilation;
    }

    private MappedCriterion MapAlternateTexts(
        IReadOnlyList<AlternateTextCriteria> alternatives,
        bool outerNegate) => MapCollectionGroup(
            alternatives,
            outerNegate,
            ResidualCollectionKind.AlternateText,
            QueryFilterKind.Title,
            nameof(MetadataCriteria.AlternateTexts),
            ResidualHydration.Entity,
            CompileAlternateTextAlternative);

    private CollectionAlternativeCompilation CompileAlternateTextAlternative(
        AlternateTextCriteria alternative)
    {
        var compilation = new CollectionAlternativeCompilation { RequiresResidual = true };
        CompileDynamicStringField(
            alternative.Kind,
            ResidualCollectionField.AlternateTextKind,
            nameof(MetadataCriteria.AlternateTexts) + "." + nameof(AlternateTextCriteria.Kind),
            alwaysNonNull: true,
            allowRemoteExact: false,
            compilation);
        CompileDynamicStringField(
            alternative.Language,
            ResidualCollectionField.AlternateTextLanguage,
            nameof(MetadataCriteria.AlternateTexts) + "." + nameof(AlternateTextCriteria.Language),
            alwaysNonNull: true,
            allowRemoteExact: false,
            compilation);
        CompileDynamicStringField(
            alternative.Text,
            ResidualCollectionField.AlternateTextText,
            nameof(MetadataCriteria.AlternateTexts) + "." + nameof(AlternateTextCriteria.Text),
            alwaysNonNull: true,
            allowRemoteExact: false,
            compilation);
        return compilation;
    }

    private MappedCriterion MapPreviews(
        IReadOnlyList<PreviewMetadataCriteria> alternatives,
        bool outerNegate) => MapCollectionGroup(
            alternatives,
            outerNegate,
            ResidualCollectionKind.Preview,
            QueryFilterKind.Preview,
            nameof(MetadataCriteria.Previews),
            ResidualHydration.Entity,
            CompilePreviewAlternative);

    private CollectionAlternativeCompilation CompilePreviewAlternative(
        PreviewMetadataCriteria alternative)
    {
        var compilation = new CollectionAlternativeCompilation { RequiresResidual = true };
        CompileKnownNullStringField(
            alternative.MimeType,
            nameof(MetadataCriteria.Previews) + "." + nameof(PreviewMetadataCriteria.MimeType),
            compilation);
        CompileDynamicStringField(
            alternative.Url,
            ResidualCollectionField.PreviewUrl,
            nameof(MetadataCriteria.Previews) + "." + nameof(PreviewMetadataCriteria.Url),
            alwaysNonNull: true,
            allowRemoteExact: false,
            compilation);
        CompileKnownStringField(
            alternative.Source,
            ResidualCollectionField.PreviewSource,
            nameof(MetadataCriteria.Previews) + "." + nameof(PreviewMetadataCriteria.Source),
            Constants.ProviderType,
            compilation);
        CompileKnownNullStringField(
            alternative.Title,
            nameof(MetadataCriteria.Previews) + "." + nameof(PreviewMetadataCriteria.Title),
            compilation);
        CompileKnownNullDateField(
            alternative.ReleaseDate,
            nameof(MetadataCriteria.Previews) + "." + nameof(PreviewMetadataCriteria.ReleaseDate),
            compilation);
        CompileKnownNullStringField(
            alternative.Language,
            nameof(MetadataCriteria.Previews) + "." + nameof(PreviewMetadataCriteria.Language),
            compilation);
        return compilation;
    }

    private MappedCriterion MapCollectionGroup<T>(
        IReadOnlyList<T> alternatives,
        bool outerNegate,
        ResidualCollectionKind collectionKind,
        QueryFilterKind filterKind,
        string source,
        ResidualHydration hydration,
        Func<T, CollectionAlternativeCompilation> compile)
    {
        var compiled = alternatives.Select(compile).ToArray();
        var possible = compiled.Where(value => !value.MatchesNothing).ToArray();
        if (possible.Length == 0)
        {
            return outerNegate ? Ignored() : KnownEmpty();
        }

        if (!outerNegate && possible.Any(value => value.UnsupportedField is null))
        {
            possible = possible.Where(value => value.UnsupportedField is null).ToArray();
        }

        var unsupportedField = possible
            .Select(value => value.UnsupportedField)
            .FirstOrDefault(value => value is not null);
        if (unsupportedField is not null)
        {
            return Unsupported(unsupportedField, outerNegate);
        }

        var requiresResidual = possible.Any(value => value.RequiresResidual || value.Coarse is null);
        if (!requiresResidual)
        {
            var directFilter = CollectionQueryFilter(filterKind, possible, outerNegate);
            return Supported(directFilter);
        }

        var residual = new ResidualFilter
        {
            Kind = ResidualFilterKind.Collection,
            Source = source,
            Hydration = hydration,
            CollectionGroup = new()
            {
                Kind = collectionKind,
                OuterNegate = outerNegate,
                Alternatives =
                [
                    .. possible.Select(value => new ResidualCollectionAlternative
                    {
                        Predicates = value.Predicates
                    })
                ]
            }
        };
        var canApplyCoarse = !outerNegate && possible.All(value => value.Coarse is not null);
        var filter = canApplyCoarse ? CollectionQueryFilter(filterKind, possible, negate: false) : null;
        return new MappedCriterion(
            Filter: filter,
            ResidualFilter: residual);
    }

    private QueryFilter CollectionQueryFilter(
        QueryFilterKind filterKind,
        IReadOnlyList<CollectionAlternativeCompilation> alternatives,
        bool negate) => new()
        {
            Kind = filterKind,
            Operator = alternatives.All(value => value.ValueBearing)
                ? CriteriaValueOperator.Equals
                : CriteriaValueOperator.IsNotNull,
            Negate = negate,
            Alternatives = [.. alternatives.Select(value => value.Coarse!)]
        };

    private IReadOnlyList<string> CompileDynamicStringField(
        CriteriaSet<PropertyCriterion<string>> criteria,
        ResidualCollectionField field,
        string fieldName,
        bool alwaysNonNull,
        bool allowRemoteExact,
        CollectionAlternativeCompilation compilation)
    {
        var exactValueSets = new List<IReadOnlyList<string>>();
        foreach (var criterion in criteria.And)
        {
            if (!TryCollectionStringPredicate(criterion, field, fieldName, out var predicate, out var unsupported))
            {
                compilation.UnsupportedField ??= unsupported;
                continue;
            }

            if (alwaysNonNull && predicate.Operator is CriteriaValueOperator.IsNull or CriteriaValueOperator.IsNotNull)
            {
                compilation.MatchesNothing |= predicate.Operator == CriteriaValueOperator.IsNull;
                continue;
            }

            var compiledPredicate = predicate;
            compilation.Predicates.Add(compiledPredicate);
            var remoteExact = allowRemoteExact &&
                !compiledPredicate.Negate &&
                compiledPredicate.Operator is CriteriaValueOperator.Equals or CriteriaValueOperator.In;
            if (!remoteExact)
            {
                compilation.RequiresResidual = true;
                continue;
            }

            exactValueSets.Add(compiledPredicate.StringValues);
            compilation.ValueBearing = true;
        }

        return CompileEntityExactCoarseValues(exactValueSets, compilation);
    }

    private void CompileDynamicIntegerField(
        CriteriaSet<PropertyCriterion<int>> criteria,
        ResidualCollectionField field,
        string fieldName,
        bool alwaysNonNull,
        CollectionAlternativeCompilation compilation)
    {
        foreach (var criterion in criteria.And)
        {
            if (!TryCollectionIntegerPredicate(criterion, field, fieldName, out var predicate, out var unsupported))
            {
                compilation.UnsupportedField ??= unsupported;
                continue;
            }

            if (alwaysNonNull && predicate.Operator is CriteriaValueOperator.IsNull or CriteriaValueOperator.IsNotNull)
            {
                compilation.MatchesNothing |= predicate.Operator == CriteriaValueOperator.IsNull;
                continue;
            }

            compilation.Predicates.Add(predicate);
            compilation.RequiresResidual = true;
        }
    }

    private IReadOnlyList<string> CompileDynamicUriField(
        CriteriaSet<PropertyCriterion<Uri>> criteria,
        string fieldName,
        CollectionAlternativeCompilation compilation)
    {
        HashSet<string>? exactValues = null;
        foreach (var criterion in criteria.And)
        {
            if (!TryCollectionUriPredicate(criterion, fieldName, out var predicate, out var unsupported))
            {
                compilation.UnsupportedField ??= unsupported;
                continue;
            }

            if (predicate.Operator is CriteriaValueOperator.IsNull or CriteriaValueOperator.IsNotNull)
            {
                compilation.MatchesNothing |= predicate.Operator == CriteriaValueOperator.IsNull;
                continue;
            }

            compilation.Predicates.Add(predicate);
            var remoteExact = !predicate.Negate &&
                predicate.Operator is CriteriaValueOperator.Equals or CriteriaValueOperator.In;
            if (!remoteExact)
            {
                compilation.RequiresResidual = true;
                continue;
            }

            var predicateValues = predicate.StringValues
                .Where(value => IsSupportedReferenceScheme(new Uri(value).Scheme))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            exactValues = exactValues is null
                ? predicateValues
                : new HashSet<string>(
                    exactValues.Intersect(predicateValues, StringComparer.OrdinalIgnoreCase),
                    StringComparer.OrdinalIgnoreCase);
            compilation.ValueBearing = true;
            compilation.MatchesNothing |= exactValues.Count == 0;
        }

        return exactValues?.ToArray() ?? [];
    }

    private void CompileKnownStringField(
        CriteriaSet<PropertyCriterion<string>> criteria,
        ResidualCollectionField field,
        string fieldName,
        string actual,
        CollectionAlternativeCompilation compilation)
    {
        foreach (var criterion in criteria.And)
        {
            if (!TryCollectionStringPredicate(criterion, field, fieldName, out var predicate, out var unsupported))
            {
                compilation.UnsupportedField ??= unsupported;
                continue;
            }

            var match = CriteriaMatcher.Match(
                actual,
                predicate.Operator,
                predicate.StringValues,
                predicate.Negate);
            if (match == CriteriaMatchResult.Rejected)
            {
                compilation.MatchesNothing = true;
            }
            else if (match == CriteriaMatchResult.Unverifiable)
            {
                compilation.Predicates.Add(predicate);
                compilation.RequiresResidual = true;
            }
        }
    }

    private void CompileKnownNullStringField(
        CriteriaSet<PropertyCriterion<string>> criteria,
        string fieldName,
        CollectionAlternativeCompilation compilation)
    {
        foreach (var criterion in criteria.And)
        {
            if (!TryCollectionStringPredicate(
                criterion,
                ResidualCollectionField.AwardCategory,
                fieldName,
                out var predicate,
                out var unsupported))
            {
                compilation.UnsupportedField ??= unsupported;
                continue;
            }

            var matched = predicate.Operator == CriteriaValueOperator.IsNull;
            if (predicate.Negate)
            {
                matched = !matched;
            }

            compilation.MatchesNothing |= !matched;
        }
    }

    private void CompileKnownNullIntegerField(
        CriteriaSet<PropertyCriterion<int>> criteria,
        string fieldName,
        CollectionAlternativeCompilation compilation)
    {
        foreach (var criterion in criteria.And)
        {
            if (!TryCollectionIntegerPredicate(
                criterion,
                ResidualCollectionField.ImageWidth,
                fieldName,
                out var predicate,
                out var unsupported))
            {
                compilation.UnsupportedField ??= unsupported;
                continue;
            }

            var matched = predicate.Operator == CriteriaValueOperator.IsNull;
            if (predicate.Negate)
            {
                matched = !matched;
            }

            compilation.MatchesNothing |= !matched;
        }
    }

    private void CompileKnownNullDateField(
        CriteriaSet<PropertyCriterion<DateTime>> criteria,
        string fieldName,
        CollectionAlternativeCompilation compilation)
    {
        foreach (var criterion in criteria.And)
        {
            var (op, negate) = InterpretOperator(criterion.Operator, criterion.Negate);
            var supported = op is CriteriaValueOperator.Equals
                or CriteriaValueOperator.IsNull
                or CriteriaValueOperator.IsNotNull
                or CriteriaValueOperator.GreaterThan
                or CriteriaValueOperator.GreaterThanOrEqual
                or CriteriaValueOperator.LessThan
                or CriteriaValueOperator.LessThanOrEqual;
            if (!supported)
            {
                compilation.UnsupportedField ??= fieldName;
                continue;
            }

            var matched = op == CriteriaValueOperator.IsNull;
            compilation.MatchesNothing |= negate ? matched : !matched;
        }
    }

    private bool TryCollectionStringPredicate(
        PropertyCriterion<string> criterion,
        ResidualCollectionField field,
        string fieldName,
        [NotNullWhen(true)] out ResidualCollectionPredicate? predicate,
        out string? unsupportedField)
    {
        predicate = null;
        unsupportedField = null;
        var (op, negate) = InterpretOperator(criterion.Operator, criterion.Negate);
        var values = TextValues(criterion);
        var supported = op is CriteriaValueOperator.Equals
            or CriteriaValueOperator.In
            or CriteriaValueOperator.IsNull
            or CriteriaValueOperator.IsNotNull
            or CriteriaValueOperator.ContainsString
            or CriteriaValueOperator.ContainsWord
            or CriteriaValueOperator.StartsWithString
            or CriteriaValueOperator.EndsWithString
            or CriteriaValueOperator.Regex;
        if (!supported ||
            op is not (CriteriaValueOperator.IsNull or CriteriaValueOperator.IsNotNull) && values.Length == 0)
        {
            unsupportedField = fieldName;
            return false;
        }

        predicate = new ResidualCollectionPredicate
        {
            Field = field,
            Operator = op,
            Negate = negate,
            StringValues = values
        };
        return true;
    }

    private bool TryCollectionIntegerPredicate(
        PropertyCriterion<int> criterion,
        ResidualCollectionField field,
        string fieldName,
        [NotNullWhen(true)] out ResidualCollectionPredicate? predicate,
        out string? unsupportedField)
    {
        predicate = null;
        unsupportedField = null;
        var (op, negate) = InterpretOperator(criterion.Operator, criterion.Negate);
        var values = IntegerValues(criterion);
        var supported = op is CriteriaValueOperator.Equals
            or CriteriaValueOperator.In
            or CriteriaValueOperator.IsNull
            or CriteriaValueOperator.IsNotNull
            or CriteriaValueOperator.GreaterThan
            or CriteriaValueOperator.GreaterThanOrEqual
            or CriteriaValueOperator.LessThan
            or CriteriaValueOperator.LessThanOrEqual;
        if (!supported ||
            op is not (CriteriaValueOperator.IsNull or CriteriaValueOperator.IsNotNull) && values.Count == 0)
        {
            unsupportedField = fieldName;
            return false;
        }

        predicate = new ResidualCollectionPredicate
        {
            Field = field,
            Operator = op,
            Negate = negate,
            IntegerValues = values
        };
        return true;
    }

    private bool TryCollectionUriPredicate(
        PropertyCriterion<Uri> criterion,
        string fieldName,
        [NotNullWhen(true)] out ResidualCollectionPredicate? predicate,
        out string? unsupportedField)
    {
        predicate = null;
        unsupportedField = null;
        var (op, negate) = InterpretOperator(criterion.Operator, criterion.Negate);
        if (!TryUriValues(criterion, out var values))
        {
            unsupportedField = fieldName;
            return false;
        }

        var supported = op is CriteriaValueOperator.Equals
            or CriteriaValueOperator.In
            or CriteriaValueOperator.IsNull
            or CriteriaValueOperator.IsNotNull
            or CriteriaValueOperator.ContainsString
            or CriteriaValueOperator.ContainsWord
            or CriteriaValueOperator.StartsWithString
            or CriteriaValueOperator.EndsWithString
            or CriteriaValueOperator.Regex;
        if (!supported ||
            op is not (CriteriaValueOperator.IsNull or CriteriaValueOperator.IsNotNull) && values.Count == 0)
        {
            unsupportedField = fieldName;
            return false;
        }

        predicate = new ResidualCollectionPredicate
        {
            Field = ResidualCollectionField.ReferenceUrl,
            Operator = op,
            Negate = negate,
            StringValues = values
        };
        return true;
    }

    private IReadOnlyList<int> IntegerValues(PropertyCriterion<int> criterion)
    {
        var values = (criterion.Values ?? []).ToList();
        if (criterion.Value != default || values.Count == 0)
        {
            values.Insert(0, criterion.Value);
        }

        return [.. values.Distinct()];
    }

    private bool TryUriValues(
        PropertyCriterion<Uri> criterion,
        out IReadOnlyList<string> normalized)
    {
        var values = new List<Uri>();
        if (criterion.Value is not null)
        {
            values.Add(criterion.Value);
        }

        values.AddRange((criterion.Values ?? []).Where(value => value is not null));
        if (values.Any(value => !value.IsAbsoluteUri))
        {
            normalized = [];
            return false;
        }

        normalized = [.. values.Select(value => value.AbsoluteUri).Distinct(StringComparer.Ordinal)];
        return true;
    }

    private bool IsValueBearing(ResidualCollectionPredicate predicate) =>
        predicate.Operator is not (CriteriaValueOperator.IsNull or CriteriaValueOperator.IsNotNull) &&
        (predicate.StringValues.Count > 0 || predicate.IntegerValues.Count > 0);

    private IReadOnlyList<string> CompileEntityExactCoarseValues(
        IReadOnlyList<IReadOnlyList<string>> exactValueSets,
        CollectionAlternativeCompilation compilation)
    {
        if (exactValueSets.Count == 0)
        {
            return [];
        }

        var qidSets = new List<IReadOnlyList<string>>();
        var nameSets = new List<IReadOnlyList<string>>();
        foreach (var values in exactValueSets)
        {
            SplitEntityValues(values, out var qids, out var names);
            qidSets.Add(qids);
            nameSets.Add(names);
        }

        if (nameSets.All(values => values.Count == 0))
        {
            return IntersectExactValueSets(qidSets, StringComparer.Ordinal, compilation);
        }

        if (qidSets.All(values => values.Count == 0))
        {
            return IntersectExactValueSets(nameSets, StringComparer.OrdinalIgnoreCase, compilation);
        }

        return exactValueSets[0];
    }

    private IReadOnlyList<string> IntersectExactValueSets(
        IReadOnlyList<IReadOnlyList<string>> valueSets,
        StringComparer comparer,
        CollectionAlternativeCompilation compilation)
    {
        var intersection = new HashSet<string>(valueSets[0], comparer);
        foreach (var values in valueSets.Skip(1))
        {
            intersection.IntersectWith(values);
        }

        compilation.MatchesNothing |= intersection.Count == 0;
        return [.. intersection];
    }

    private void SplitEntityValues(
        IReadOnlyList<string> values,
        out IReadOnlyList<string> qids,
        out IReadOnlyList<string> names)
    {
        var mappedQids = new List<string>();
        var mappedNames = new List<string>();
        foreach (var value in values)
        {
            if (value.TryNormalizeQid(out var qid))
            {
                mappedQids.Add(qid);
            }
            else
            {
                mappedNames.Add(value);
            }
        }

        qids = mappedQids;
        names = mappedNames;
    }

    private void SplitEntityPredicateValues(
        CollectionAlternativeCompilation compilation,
        ResidualCollectionField field)
    {
        for (var index = 0; index < compilation.Predicates.Count; index++)
        {
            var predicate = compilation.Predicates[index];
            if (predicate.Field != field ||
                predicate.Operator is not (CriteriaValueOperator.Equals or CriteriaValueOperator.In))
            {
                continue;
            }

            SplitEntityValues(predicate.StringValues, out var qids, out var names);
            compilation.Predicates[index] = predicate with
            {
                StringValues = names,
                EntityQids = qids
            };
        }
    }

    private bool IsSupportedReferenceScheme(string value) =>
        value.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
        value.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);

    private MappedCriterion MapCustom(
        string key,
        PropertyCriterion<string> criterion,
        IReadOnlyDictionary<string, PropertyCriterion<string>> customGroup)
    {
        var normalizedKey = key.Trim().ToLowerInvariant();
        var values = TextValues(criterion);
        var relationshipSubjectKey = IsCustomRelationshipSubjectKey(normalizedKey);
        var exactOperator = criterion.Operator is CriteriaValueOperator.Equals or CriteriaValueOperator.In;
        var fuzzySubjectOperator = relationshipSubjectKey &&
            criterion.Operator is CriteriaValueOperator.ContainsString
                or CriteriaValueOperator.ContainsWord
                or CriteriaValueOperator.StartsWithString
                or CriteriaValueOperator.EndsWithString;
        if (values.Length == 0 || (!exactOperator && !fuzzySubjectOperator))
        {
            return Unsupported(key, criterion.Negate);
        }

        if (normalizedKey is "relationship.externalid" or "relationship.externalidsource")
        {
            if (!TryBuildCustomRelationshipExternalFilter(customGroup, out var pairedFilter))
            {
                return Unsupported(key, criterion.Negate);
            }

            return normalizedKey == "relationship.externalid" &&
                !CustomRelationshipSubjectAbsorbsExternalPair(customGroup)
                ? Supported(pairedFilter)
                : Ignored("The criterion was absorbed by another criterion in the custom relationship group.");
        }

        if (normalizedKey is "relationship.kind" or "relationship.type" &&
            HasCustomKey(customGroup, RelationshipFieldKeys.ExternalId) &&
            TryBuildCustomRelationshipExternalFilter(customGroup, out _))
        {
            return Ignored("The criterion was absorbed by another criterion in the custom relationship group.");
        }

        if (relationshipSubjectKey)
        {
            var role = normalizedKey == RelationshipFieldKeys.Subject
                ? "person"
                : normalizedKey.Substring(RelationshipFieldKeys.SubjectKindPrefix.Length);
            if (!TryMapRole(role, out var properties))
            {
                return InapplicableCustom(key, criterion.Negate);
            }

            if (HasCustomKey(customGroup, RelationshipFieldKeys.Kind) ||
                HasCustomKey(customGroup, RelationshipFieldKeys.Type))
            {
                if (!TryCustomRelationshipProperties(customGroup, out var groupProperties))
                {
                    return InapplicableCustom(key, criterion.Negate);
                }

                properties = [.. properties.Intersect(groupProperties, StringComparer.Ordinal)];
                if (properties.Count == 0)
                {
                    return InapplicableCustom(key, criterion.Negate);
                }
            }

            var hasExternalPair = HasCustomKey(customGroup, RelationshipFieldKeys.ExternalId) ||
                HasCustomKey(customGroup, RelationshipFieldKeys.ExternalIdSource);
            QueryFilter? pairedExternalFilter = null;
            if (hasExternalPair &&
                (!TryBuildCustomRelationshipExternalFilter(customGroup, out pairedExternalFilter) ||
                    pairedExternalFilter.Negate))
            {
                return Unsupported(key, criterion.Negate);
            }

            var pairedExternalGroup = pairedExternalFilter?.Alternatives.Single().ExternalIdGroups.SingleOrDefault();
            var qids = new List<string>();
            var names = new List<string>();
            foreach (var value in values)
            {
                if (exactOperator && value.TryNormalizeQid(out var qid))
                {
                    qids.Add(qid);
                }
                else
                {
                    names.Add(value);
                }
            }

            if (names.Count > 5)
            {
                if (criterion.Negate || qids.Count == 0)
                {
                    return Unsupported(key, criterion.Negate);
                }

                names.Clear();
            }

            if (criterion.Negate && (fuzzySubjectOperator || pairedExternalFilter is not null))
            {
                var residual = new ResidualFilter
                {
                    Kind = ResidualFilterKind.Relationship,
                    Source = key,
                    Hydration = ResidualHydration.Entity | ResidualHydration.Support,
                    RelationshipGroup = new()
                    {
                        OuterNegate = true,
                        Alternatives =
                        [
                            new ResidualRelationshipAlternative
                            {
                                PropertyIds = properties,
                                Subjects =
                                [
                                    new ResidualRelationshipFacet
                                    {
                                        Criterion = StringCriterion(criterion.Operator, negate: false, values),
                                        Qids = qids,
                                        Names = names
                                    }
                                ],
                                ExternalIdGroups = pairedExternalGroup is null
                                    ? []
                                    :
                                    [
                                        new ResidualRelationshipFacet
                                        {
                                            Qids = pairedExternalGroup.Qids,
                                            ExternalIds = pairedExternalGroup.ExternalIds
                                        }
                                    ]
                            }
                        ]
                    }
                };
                var coarseFilter = pairedExternalFilter is null
                    ? null
                    : new QueryFilter
                    {
                        Kind = QueryFilterKind.Relationship,
                        Alternatives =
                        [
                            new SubjectFilter
                            {
                                PropertyIds = properties,
                                ExternalIdGroups = pairedExternalGroup is null ? [] : [pairedExternalGroup]
                            }
                        ]
                    };
                return coarseFilter is null
                    ? Residual(residual)
                    : new MappedCriterion(
                        Filter: coarseFilter,
                        ResidualFilter: residual);
            }

            return Supported(new QueryFilter
            {
                Kind = QueryFilterKind.Relationship,
                Negate = criterion.Negate,
                Alternatives =
                [
                    new SubjectFilter
                    {
                        PropertyIds = properties,
                        Qids = qids,
                        Names = names,
                        ExternalIdGroups = pairedExternalGroup is null ? [] : [pairedExternalGroup]
                    }
                ]
            });
        }

        if (normalizedKey is "relationship.kind" or "relationship.type")
        {
            if (!TryMapRoleProperties(values, out var properties))
            {
                return InapplicableCustom(key, criterion.Negate);
            }

            return Supported(new QueryFilter
            {
                Kind = QueryFilterKind.Relationship,
                Negate = criterion.Negate,
                Alternatives = [new SubjectFilter { PropertyIds = [.. properties.Distinct()] }]
            });
        }

        return InapplicableCustom(key, criterion.Negate);
    }

    private IReadOnlyDictionary<string, PropertyCriterion<string>> MergeCustomItems(
        IReadOnlyDictionary<string, PropertyCriterion<string>> source)
    {
        var items = new Dictionary<string, PropertyCriterion<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, criterion) in source)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                continue;
            }

            var (op, negate) = InterpretOperator(criterion.Operator, criterion.Negate);
            var values = TextValues(criterion);
            var normalized = criterion with { Operator = op, Negate = negate, Value = null, Values = values };
            if (items.TryGetValue(key, out var existing) &&
                existing.Operator == normalized.Operator &&
                existing.Negate == normalized.Negate)
            {
                items[key] = existing with
                {
                    Values = [.. TextValues(existing).Concat(values).Distinct(StringComparer.OrdinalIgnoreCase)]
                };
            }
            else
            {
                items[key] = normalized;
            }
        }

        return items;
    }

    private string[] TextValues(PropertyCriterion<string> criterion)
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

    private TemporalValue[] TemporalValues(PropertyCriterion<TemporalValue> criterion)
    {
        var values = new List<TemporalValue>();
        if (criterion.Value is not null)
        {
            values.Add(criterion.Value);
        }

        values.AddRange((criterion.Values ?? []).Where(value => value is not null));
        return [.. values];
    }

    private IReadOnlyList<TimeSpan> DurationValues(PropertyCriterion<TimeSpan> criterion)
    {
        var values = (criterion.Values ?? []).ToList();
        if (criterion.Value != default || values.Count == 0)
        {
            values.Insert(0, criterion.Value);
        }

        return values;
    }

    private List<CriterionDisposition> MapProviderIdentityCriteria(MetadataCriteria criteria)
    {
        var dispositions = new List<CriterionDisposition>();
        var providerTypeIndex = 0;
        foreach (var criterion in criteria.ProviderType.And)
        {
            var excludes = ExcludesProviderType(criterion);
            dispositions.Add(new CriterionDisposition
            {
                Source = CriterionSource(nameof(MetadataCriteria.ProviderType), "And", providerTypeIndex),
                Disposition = excludes
                    ? CriterionDispositionKind.ProvablyUnsatisfiable
                    : CriterionDispositionKind.Omitted,
                Reason = excludes
                    ? "The criterion excludes the Wikidata provider type."
                    : "The criterion was not used to exclude the Wikidata provider type; no search filter is required."
            });
            providerTypeIndex++;
        }

        var sourceIndex = 0;
        foreach (var criterion in criteria.Source.And)
        {
            var excludes = ExcludesSource(criterion);
            dispositions.Add(new CriterionDisposition
            {
                Source = CriterionSource(nameof(MetadataCriteria.Source), "And", sourceIndex),
                Disposition = excludes
                    ? CriterionDispositionKind.ProvablyUnsatisfiable
                    : CriterionDispositionKind.Omitted,
                Reason = excludes
                    ? "The criterion excludes every Wikidata source key."
                    : "The criterion was not used to exclude Wikidata source keys; no search filter is required."
            });
            sourceIndex++;
        }

        return dispositions;
    }

    private bool ExcludesProviderType(PropertyCriterion<string> criterion)
    {
        var (op, negate) = InterpretOperator(criterion.Operator, criterion.Negate);
        var values = TextValues(criterion);
        bool? matches = op switch
        {
            CriteriaValueOperator.IsNull => false,
            CriteriaValueOperator.IsNotNull => true,
            CriteriaValueOperator.Equals or CriteriaValueOperator.In or CriteriaValueOperator.ContainsWord =>
                values.Any(value => string.Equals(
                    value,
                    Constants.ProviderType,
                    StringComparison.OrdinalIgnoreCase)),
            CriteriaValueOperator.ContainsString =>
                values.Any(value => Constants.ProviderType.Contains(value, StringComparison.OrdinalIgnoreCase)),
            CriteriaValueOperator.StartsWithString =>
                values.Any(value => Constants.ProviderType.StartsWith(value, StringComparison.OrdinalIgnoreCase)),
            CriteriaValueOperator.EndsWithString =>
                values.Any(value => Constants.ProviderType.EndsWith(value, StringComparison.OrdinalIgnoreCase)),
            _ => null
        };
        return matches is not null && matches.Value == negate;
    }

    private bool ExcludesSource(PropertyCriterion<string> criterion)
    {
        var (op, negate) = InterpretOperator(criterion.Operator, criterion.Negate);
        if (op is CriteriaValueOperator.IsNull or CriteriaValueOperator.IsNotNull)
        {
            return op == CriteriaValueOperator.IsNull;
        }

        var values = TextValues(criterion);
        if (!negate)
        {
            var satisfiable = op switch
            {
                CriteriaValueOperator.Equals or CriteriaValueOperator.In => values.Any(IsSourceIdentityToken),
                CriteriaValueOperator.StartsWithString => values.Any(value =>
                    SourceKeyPrefix.StartsWith(value, StringComparison.OrdinalIgnoreCase) ||
                    value.StartsWith(SourceKeyPrefix, StringComparison.OrdinalIgnoreCase)),
                CriteriaValueOperator.ContainsString or CriteriaValueOperator.ContainsWord =>
                    values.Any(SourceKeyCouldContain),
                CriteriaValueOperator.EndsWithString => values.Any(SourceKeyCouldEndWith),
                _ => true
            };
            return !satisfiable;
        }

        var tautology = op switch
        {
            CriteriaValueOperator.Equals or CriteriaValueOperator.In => values.Any(value =>
                string.Equals(value, Constants.ProviderType, StringComparison.OrdinalIgnoreCase)),
            CriteriaValueOperator.ContainsString => values.Any(value =>
                SourceKeyPrefix.Contains(value, StringComparison.OrdinalIgnoreCase)),
            CriteriaValueOperator.ContainsWord => values.Any(value =>
                string.Equals(value, Constants.ProviderType, StringComparison.OrdinalIgnoreCase)),
            CriteriaValueOperator.StartsWithString => values.Any(value =>
                SourceKeyPrefix.StartsWith(value, StringComparison.OrdinalIgnoreCase)),
            _ => false
        };
        return tautology;
    }

    private bool IsSourceIdentityToken(string value) =>
        string.Equals(value, Constants.ProviderType, StringComparison.OrdinalIgnoreCase) ||
        value.StartsWith(SourceKeyPrefix, StringComparison.OrdinalIgnoreCase);

    private bool SourceKeyCouldContain(string value)
    {
        var (head, digits) = SplitTrailingDigits(value.ToLowerInvariant());
        if (head.Length == 0)
        {
            return true;
        }

        if (digits.Length == 0)
        {
            return SourceKeyTemplates.Any(template => template.Contains(head, StringComparison.Ordinal));
        }

        return digits[0] != '0' &&
            SourceKeyTemplates.Any(template => template.EndsWith(head, StringComparison.Ordinal));
    }

    private bool SourceKeyCouldEndWith(string value)
    {
        var (head, digits) = SplitTrailingDigits(value.ToLowerInvariant());
        if (head.Length == 0)
        {
            return true;
        }

        if (digits.Length == 0)
        {
            return false;
        }

        return digits[0] != '0' &&
            SourceKeyTemplates.Any(template => template.EndsWith(head, StringComparison.Ordinal));
    }

    private (string Head, string Digits) SplitTrailingDigits(string value)
    {
        var boundary = value.Length;
        while (boundary > 0 && char.IsDigit(value[boundary - 1]))
        {
            boundary--;
        }

        return (value.Substring(0, boundary), value.Substring(boundary));
    }

    private (CriteriaValueOperator Operator, bool Negate) InterpretOperator(
        CriteriaValueOperator op,
        bool negate)
    {
        var (primitive, negative) = op switch
        {
            CriteriaValueOperator.NotEquals => (CriteriaValueOperator.Equals, true),
            CriteriaValueOperator.NotIn => (CriteriaValueOperator.In, true),
            CriteriaValueOperator.NotContainsString => (CriteriaValueOperator.ContainsString, true),
            CriteriaValueOperator.NotStartsWithString => (CriteriaValueOperator.StartsWithString, true),
            CriteriaValueOperator.NotEndsWithString => (CriteriaValueOperator.EndsWithString, true),
            _ => (op, false)
        };
        var effective = negate ^ negative;
        return (primitive, effective) switch
        {
            (CriteriaValueOperator.IsNull, true) => (CriteriaValueOperator.IsNotNull, false),
            (CriteriaValueOperator.IsNotNull, true) => (CriteriaValueOperator.IsNull, false),
            _ => (primitive, effective)
        };
    }

    private bool HasCustomKey(
        IReadOnlyDictionary<string, PropertyCriterion<string>> group,
        string key) => group.Keys.Any(candidate => string.Equals(candidate, key, StringComparison.OrdinalIgnoreCase));

    private bool CustomRelationshipSubjectAbsorbsExternalPair(
        IReadOnlyDictionary<string, PropertyCriterion<string>> group)
    {
        var subjects = group.Where(entry => IsCustomRelationshipSubjectKey(entry.Key)).Take(2).ToArray();
        if (subjects.Length != 1)
        {
            return false;
        }

        var mapped = MapCustom(subjects[0].Key, subjects[0].Value, group);
        return mapped.Filter?.Alternatives.Any(alternative => alternative.ExternalIds.Count > 0 ||
            alternative.ExternalIdGroups.Count > 0) == true &&
            (mapped.ResidualFilter is null ||
                mapped.ResidualFilter.RelationshipGroup?.Alternatives.Any(alternative =>
                    alternative.ExternalIdGroups.Count > 0) == true);
    }

    private bool IsCustomRelationshipSubjectKey(string key)
    {
        var normalizedKey = key.Trim().ToLowerInvariant();
        return normalizedKey == RelationshipFieldKeys.Subject ||
            normalizedKey.StartsWith(RelationshipFieldKeys.SubjectKindPrefix, StringComparison.Ordinal);
    }

    private bool TryGetCustomCriterion(
        IReadOnlyDictionary<string, PropertyCriterion<string>> group,
        string key,
        [NotNullWhen(true)] out PropertyCriterion<string>? criterion)
    {
        var matches = group
            .Where(entry => string.Equals(entry.Key, key, StringComparison.OrdinalIgnoreCase))
            .Take(2)
            .ToArray();
        criterion = matches.Length == 1 ? matches[0].Value : null;
        return criterion is not null;
    }

    private bool TryBuildCustomRelationshipExternalFilter(
        IReadOnlyDictionary<string, PropertyCriterion<string>> group,
        [NotNullWhen(true)] out QueryFilter? filter)
    {
        filter = null;
        if (!TryGetCustomCriterion(group, RelationshipFieldKeys.ExternalId, out var externalIdCriterion) ||
            !TryGetCustomCriterion(group, RelationshipFieldKeys.ExternalIdSource, out var sourceCriterion) ||
            externalIdCriterion.Operator is not (CriteriaValueOperator.Equals or CriteriaValueOperator.In) ||
            sourceCriterion.Operator is not (CriteriaValueOperator.Equals or CriteriaValueOperator.In) ||
            sourceCriterion.Negate ||
            !TryCustomRelationshipProperties(group, out var properties))
        {
            return false;
        }

        var qids = new List<string>();
        var externalIds = new List<PropertyValue>();
        if (externalIdCriterion.Negate &&
            (sourceCriterion.Values ?? []).Any(source => !IsSupportedSubjectExternalSource(source)))
        {
            return false;
        }

        foreach (var source in (sourceCriterion.Values ?? []).Where(IsSupportedSubjectExternalSource))
        {
            foreach (var value in externalIdCriterion.Values ?? [])
            {
                if (!TryMapSubjectExternalValue(source, value, out var qid, out var mappedExternals))
                {
                    continue;
                }

                if (qid is not null)
                {
                    qids.Add(qid);
                }

                externalIds.AddRange(mappedExternals);
            }
        }

        if (qids.Count == 0 && externalIds.Count == 0)
        {
            return false;
        }

        filter = new QueryFilter
        {
            Kind = QueryFilterKind.Relationship,
            Negate = externalIdCriterion.Negate,
            Alternatives =
            [
                new SubjectFilter
                {
                    PropertyIds = properties,
                    ExternalIdGroups =
                    [
                        new ExternalIdGroup
                        {
                            Qids = [.. qids.Distinct(StringComparer.Ordinal)],
                            ExternalIds = [.. externalIds.Distinct()]
                        }
                    ]
                }
            ]
        };
        return true;
    }

    private bool TryCustomRelationshipProperties(
        IReadOnlyDictionary<string, PropertyCriterion<string>> group,
        out IReadOnlyList<string> properties)
    {
        properties = [];
        var hasKind = TryGetCustomCriterion(group, RelationshipFieldKeys.Kind, out var kindCriterion);
        var hasType = TryGetCustomCriterion(group, RelationshipFieldKeys.Type, out var typeCriterion);
        if (!TryCustomRoleProperties(hasKind ? kindCriterion : null, out var kinds) ||
            !TryCustomRoleProperties(hasType ? typeCriterion : null, out var types))
        {
            return false;
        }

        if (!hasKind && !hasType)
        {
            return TryMapRole("person", out properties);
        }

        if (!hasKind)
        {
            properties = types;
            return true;
        }

        if (!hasType)
        {
            properties = kinds;
            return true;
        }

        properties = [.. kinds.Intersect(types, StringComparer.Ordinal)];
        return properties.Count > 0;
    }

    private bool TryCustomRoleProperties(
        PropertyCriterion<string>? criterion,
        out IReadOnlyList<string> properties)
    {
        properties = [];
        if (criterion is null)
        {
            return true;
        }

        if (criterion.Negate ||
            criterion.Operator is not (CriteriaValueOperator.Equals or CriteriaValueOperator.In))
        {
            return false;
        }

        return TryMapRoleProperties(criterion.Values ?? [], out properties);
    }

    private bool IsSelective(QueryFilter filter)
    {
        if (filter.Negate || filter.Operator is CriteriaValueOperator.IsNull or CriteriaValueOperator.IsNotNull)
        {
            return false;
        }

        return filter.Kind is QueryFilterKind.Title
            or QueryFilterKind.OriginalTitle
            or QueryFilterKind.Overview
            or QueryFilterKind.PublishDate
            or QueryFilterKind.Duration
            or QueryFilterKind.Established
            or QueryFilterKind.Dissolved
            or QueryFilterKind.BirthDate
            or QueryFilterKind.DeathDate
            or QueryFilterKind.Language
            or QueryFilterKind.Genre
            or QueryFilterKind.ExternalId
            or QueryFilterKind.Relationship
            or QueryFilterKind.ContentRating
            or QueryFilterKind.Award
            or QueryFilterKind.Image
            or QueryFilterKind.Reference;
    }

    private bool TryRelationshipProperties(
        RelationshipCriteria criteria,
        out IReadOnlyList<string> properties,
        out bool cannotCompile)
    {
        properties = [];
        var kindsMapped = TryRoleProperties(criteria.Kind, out var kinds, out var kindsCannotCompile);
        var typesMapped = TryRoleProperties(criteria.Type, out var types, out var typesCannotCompile);
        var knownAbsent = !kindsMapped && !kindsCannotCompile || !typesMapped && !typesCannotCompile;
        cannotCompile = !knownAbsent && (kindsCannotCompile || typesCannotCompile);
        if (!kindsMapped || !typesMapped)
        {
            return false;
        }

        if (kinds.Count == 0 && types.Count == 0)
        {
            return TryMapRole("person", out properties);
        }

        if (kinds.Count == 0)
        {
            properties = types;
            return true;
        }

        if (types.Count == 0)
        {
            properties = kinds;
            return true;
        }

        properties = [.. kinds.Intersect(types, StringComparer.Ordinal)];
        return properties.Count > 0;
    }

    private bool TryRoleProperties(
        CriteriaSet<PropertyCriterion<string>> criteria,
        out IReadOnlyList<string> properties,
        out bool cannotCompile)
    {
        properties = [];
        cannotCompile = false;
        if (!criteria.And.Any())
        {
            return true;
        }

        if (!TrySimpleValues(criteria, out var roles))
        {
            cannotCompile = true;
            return false;
        }

        return TryMapRoleProperties(roles, out properties);
    }

    private bool TryMapRoleProperties(
        IEnumerable<string> roles,
        out IReadOnlyList<string> properties)
    {
        var mapped = new List<string>();
        foreach (var role in roles)
        {
            if (TryMapRole(role, out var roleProperties))
            {
                mapped.AddRange(roleProperties);
            }
        }

        properties = [.. mapped.Distinct(StringComparer.Ordinal)];
        return properties.Count > 0;
    }

    private bool TryMapRole(string role, out IReadOnlyList<string> properties)
        => role.TryGetRelationshipPropertyIds(out properties);

    private bool TryMapExternalValue(
        EntityKind kind,
        string? source,
        string value,
        out string? qid,
        out PropertyValue? propertyValue)
    {
        qid = null;
        propertyValue = null;
        var actualSource = source;
        var actualValue = value.Trim();
        var separator = actualValue.IndexOfAny([':', '=']);
        if (actualSource is null && separator > 0 && separator < actualValue.Length - 1)
        {
            actualSource = actualValue.Substring(0, separator);
            actualValue = actualValue.Substring(separator + 1).Trim();
        }

        if (actualSource is null && actualValue.TryNormalizeQid(out qid))
        {
            return true;
        }

        if (actualSource is null)
        {
            actualSource = actualValue.StartsWith("tt", StringComparison.OrdinalIgnoreCase)
                ? Constants.ImdbSource
                : Constants.TmdbSource;
        }

        if (IsQidSource(actualSource))
        {
            return actualValue.TryNormalizeQid(out qid);
        }

        var propertyId = MapExternalProperty(kind, actualSource);
        if (propertyId is null || string.IsNullOrWhiteSpace(actualValue))
        {
            return false;
        }

        propertyValue = new PropertyValue { PropertyId = propertyId, Value = actualValue };
        return true;
    }

    private bool TryMapSubjectExternalValue(
        string? source,
        string value,
        out string? qid,
        out IReadOnlyList<PropertyValue> propertyValues)
    {
        qid = null;
        propertyValues = [];
        var actualSource = source;
        var actualValue = value.Trim();
        var separator = actualValue.IndexOfAny([':', '=']);
        if (actualSource is null && separator > 0 && separator < actualValue.Length - 1)
        {
            actualSource = actualValue.Substring(0, separator);
            actualValue = actualValue.Substring(separator + 1).Trim();
        }

        if (actualSource is null && actualValue.TryNormalizeQid(out qid))
        {
            return true;
        }

        if (actualSource is not null && IsQidSource(actualSource))
        {
            return actualValue.TryNormalizeQid(out qid);
        }

        var normalized = actualSource?.Trim().ToLowerInvariant();
        var propertyIds = normalized switch
        {
            null when actualValue.StartsWith("nm", StringComparison.OrdinalIgnoreCase) =>
                [Constants.ImdbPropertyId],
            null => [],
            _ => SubjectPropertyIds(normalized)
        };
        if (propertyIds.Length == 0 || string.IsNullOrWhiteSpace(actualValue))
        {
            return false;
        }

        propertyValues =
            [.. propertyIds.Select(id => new PropertyValue { PropertyId = id, Value = actualValue })];
        return true;
    }

    private bool IsSupportedSubjectExternalSource(string source)
    {
        var normalized = source.Trim().ToLowerInvariant();
        return IsQidSource(normalized) || SubjectPropertyIds(normalized).Length > 0;
    }

    private string[] SubjectPropertyIds(string source) =>
    [
        .. Constants.PersonExternalIds
            .Concat(Constants.StudioExternalIds)
            .Where(id => id.Source == source ||
                string.Equals(id.PropertyId, source, StringComparison.OrdinalIgnoreCase))
            .Select(id => id.PropertyId)
            .Distinct()
    ];

    private string? MapExternalProperty(EntityKind kind, string source)
    {
        var normalized = source.Trim().ToLowerInvariant();
        if (normalized is "imdb" or "p345")
        {
            return Constants.ImdbPropertyId;
        }

        if (normalized is "tmdb" or "themoviedb" or "p4947" or "p4983")
        {
            return kind switch
            {
                EntityKind.Movie when normalized != "p4983" => Constants.TmdbMoviePropertyId,
                EntityKind.Show when normalized != "p4947" => Constants.TmdbShowPropertyId,
                _ => null
            };
        }

        if (normalized is "tvdb" or "thetvdb" or "p4835")
        {
            return kind == EntityKind.Show ? Constants.TvdbShowPropertyId : null;
        }

        return null;
    }

    private bool TrySimpleValues(CriteriaSet<PropertyCriterion<string>> set, out IReadOnlyList<string> values)
    {
        values = [];
        var criteria = set.And.ToArray();
        if (criteria.Length != 1)
        {
            return false;
        }

        var interpreted = InterpretOperator(criteria[0].Operator, criteria[0].Negate);
        if (interpreted.Negate ||
            interpreted.Operator is not (CriteriaValueOperator.Equals or CriteriaValueOperator.In))
        {
            return false;
        }

        var mapped = TextValues(criteria[0]);
        if (mapped.Length == 0)
        {
            return false;
        }

        values = mapped;
        return true;
    }

    private bool TryOptionalRelationshipSubjectValues(
        CriteriaSet<PropertyCriterion<string>> set,
        out IReadOnlyList<string> values,
        out CriteriaValueOperator valueOperator)
    {
        values = [];
        valueOperator = CriteriaValueOperator.Equals;
        if (!set.And.Any())
        {
            return true;
        }

        var criteria = set.And.ToArray();
        if (criteria.Length != 1)
        {
            return false;
        }

        var interpreted = InterpretOperator(criteria[0].Operator, criteria[0].Negate);
        if (interpreted.Negate ||
            interpreted.Operator is not (CriteriaValueOperator.Equals
                or CriteriaValueOperator.In
                or CriteriaValueOperator.ContainsString
                or CriteriaValueOperator.ContainsWord
                or CriteriaValueOperator.StartsWithString
                or CriteriaValueOperator.EndsWithString))
        {
            return false;
        }

        var mapped = TextValues(criteria[0]);
        if (mapped.Length == 0)
        {
            return false;
        }

        values = mapped;
        valueOperator = interpreted.Operator;
        return true;
    }

    private bool TryOptionalValues(
        CriteriaSet<PropertyCriterion<string>> set,
        out IReadOnlyList<string> values)
    {
        values = [];
        return !set.And.Any() || TrySimpleValues(set, out values);
    }

    private IEnumerable<string?> SourcesOrUnspecified(IReadOnlyList<string> sources)
    {
        if (sources.Count == 0)
        {
            yield return null;
            yield break;
        }

        foreach (var source in sources)
        {
            yield return source;
        }
    }

    private bool TryNormalizeLanguageCode(string value, [NotNullWhen(true)] out string? language)
    {
        language = null;
        var trimmed = value.Trim();
        if (trimmed.Length is < 2 or > 3 || !trimmed.All(IsAsciiLetter))
        {
            return false;
        }

        language = trimmed.ToLowerInvariant();
        return true;
    }

    private bool IsAsciiLetter(char value) => value is >= 'A' and <= 'Z' or >= 'a' and <= 'z';

    private bool IsQidSource(string source)
    {
        var normalized = source.Trim().ToLowerInvariant();
        return normalized is "wikidata" or "wd" or "qid";
    }

    private MappedCriterion Supported(QueryFilter filter) => new(filter);

    private MappedCriterion Residual(ResidualFilter filter) =>
        new(Filter: null, ResidualFilter: filter);

    private ResidualStringCriterion StringCriterion(
        CriteriaValueOperator valueOperator,
        bool negate,
        IReadOnlyList<string> values) => new()
        {
            Operator = valueOperator,
            Negate = negate,
            Values = values
        };

    private MappedCriterion Inapplicable(CriteriaValueOperator valueOperator, bool negate) =>
        negate || valueOperator == CriteriaValueOperator.IsNull
            ? Ignored("The field is absent for the requested entity kind, so the criterion is always satisfied.")
            : KnownEmpty("The field is absent for the requested entity kind.");

    private MappedCriterion KnownEmpty(string? reason = null) =>
        new(
            Filter: null,
            MatchesNothing: true,
            Reason: reason ?? "The criterion is provably unsatisfiable for Wikidata.");

    private MappedCriterion Ignored(string? reason = null) =>
        new(
            Filter: null,
            Reason: reason ?? "The criterion is always satisfied and requires no filter.");

    private MappedCriterion Unsupported(string field, bool negate = false) =>
        new(
            Filter: null,
            MatchesNothing: true,
            Reason: negate
                ? "Wikidata cannot apply the excluded " + field + " criterion safely."
                : "Wikidata cannot apply the " + field + " criterion.",
            IsUnsupported: true);

    private MappedCriterion InapplicableCustom(string field, bool negate) =>
        negate
            ? Ignored("Wikidata has no data for the " + field + " criterion, so it is always satisfied.")
            : KnownEmpty("Wikidata has no data for the " + field + " criterion.");

    private MappedCriterion InapplicableRelationships(bool negate) =>
        negate
            ? Ignored("Wikidata has no relationship data for the criterion, so it is always satisfied.")
            : KnownEmpty("Wikidata has no relationship data for the criterion.");

    private CriterionDisposition ToDisposition(string source, MappedCriterion mapped)
    {
        var disposition = mapped.IsUnsupported
            ? CriterionDispositionKind.Refused
            : (mapped.Filter, mapped.ResidualFilter, mapped.MatchesNothing) switch
            {
                (not null, not null, _) => CriterionDispositionKind.ServerFilterAndResidual,
                (not null, null, _) => CriterionDispositionKind.ServerFilter,
                (null, not null, _) => CriterionDispositionKind.Residual,
                (null, null, true) => CriterionDispositionKind.ProvablyUnsatisfiable,
                _ => CriterionDispositionKind.Omitted
            };
        var reason = mapped.Reason ??
            disposition switch
            {
                CriterionDispositionKind.ServerFilter => "Planned as a server-side filter.",
                CriterionDispositionKind.Residual =>
                    "Planned for local residual evaluation when a server bound is available.",
                CriterionDispositionKind.ServerFilterAndResidual =>
                    "Planned as a coarse server filter and a bounded local residual.",
                CriterionDispositionKind.ProvablyUnsatisfiable =>
                    "The criterion is provably unsatisfiable for Wikidata.",
                CriterionDispositionKind.Omitted =>
                    "The criterion is always satisfied and requires no filter.",
                _ => "The criterion cannot be applied safely by Wikidata."
            };
        return new CriterionDisposition
        {
            Source = source,
            Disposition = disposition,
            Reason = reason
        };
    }

    private string CriterionSource(string field, string group, int index) =>
        field + "." + group + "[" + index.ToString(System.Globalization.CultureInfo.InvariantCulture) + "]";
}
