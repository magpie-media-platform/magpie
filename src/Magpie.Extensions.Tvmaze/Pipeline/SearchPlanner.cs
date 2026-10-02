using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Filtering;
using Magpie.Sdk.Content;

using Microsoft.Extensions.Logging;

using CriterionGrammar = Magpie.Extensions.Tvmaze.Pipeline.CriterionGrammarExtensions;

namespace Magpie.Extensions.Tvmaze.Pipeline;

internal sealed class SearchPlanner(ILogger<SearchPlanner> logger) : ISearchPlanner
{
    private enum EligibilityOutcome
    {
        Included,
        Excluded,
        Unknown
    }

    private sealed record PersonSeedCandidate(
        int GroupIndex,
        SearchSeed.PersonChain Seed,
        bool IsSettled,
        int RequestCost);

    private sealed record ImageResidualCompilation(
        IReadOnlyList<ImageCriteriaResidual> Residuals,
        bool KnownEmpty,
        IReadOnlyList<CriterionDisposition> Dispositions);

    private sealed record EligibilityEvaluation(
        EligibilityOutcome Outcome,
        IReadOnlyList<CriterionDisposition> Dispositions);

    [SuppressMessage("Ordering", "SA1201", Justification = "Nested types come first in this codebase.")]
    private const string TvContentType = "tv";

    private const string ShowLevel = "show";

    private static readonly string[] KnownExternalIdSources =
    [
        Constants.ProviderType,
        Constants.ImdbSource,
        Constants.TheTvdbSource,
        Constants.TvRageSource
    ];

    private static readonly string[] SampleSources =
    [Constants.ShowSourcePrefix + "1", Constants.ShowSourcePrefix + "2"];

    private static readonly JsonSerializerOptions TraceJsonOptions = new()
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public QueryPlan Plan(ContentHierarchyItemCriteria query, int maxResults)
    {
        var metadata = query.MergeMetadata();
        var maximumResults = Math.Max(1, maxResults);
        var contentTypeEligibility = EvaluateEligibility(
            query.ContentType.And,
            TvContentType,
            "ContentType");
        var levelEligibility = EvaluateEligibility(query.Level.And, ShowLevel, "Level");
        var eligibilityDispositions = contentTypeEligibility.Dispositions
            .Concat(levelEligibility.Dispositions)
            .ToArray();
        if (contentTypeEligibility.Outcome == EligibilityOutcome.Excluded ||
            levelEligibility.Outcome == EligibilityOutcome.Excluded)
        {
            var emptyPlan = new QueryPlan
            {
                Query = query,
                Metadata = metadata,
                MaximumResults = maximumResults,
                Root = new UnionPlanNode { Id = 1, Contributes = "known-empty show candidates", Children = [] },
                Dispositions = eligibilityDispositions
            };
            TracePlan(emptyPlan);
            return emptyPlan;
        }

        var compiled = Compile(metadata);
        var plan = BuildQueryPlan(query, metadata, maximumResults, compiled);
        var completed = plan with
        {
            Dispositions = [.. eligibilityDispositions.Concat(plan.Dispositions)]
        };
        TracePlan(completed);
        return completed;
    }

    private void TracePlan(QueryPlan plan)
    {
        if (logger.IsEnabled(LogLevel.Trace))
        {
            logger.LogTrace("PLAN TVMaze {PlanJson}", JsonSerializer.Serialize(plan, TraceJsonOptions));
        }
    }

    private SearchPlanNode Compile(MetadataCriteria criteria)
    {
        var residualDispositions = new List<CriterionDisposition>();
        var filters = new List<LocalFilter>();
        var idSeedCandidates = new List<IReadOnlyList<SearchSeed.Lookup>>();
        var dispositions = new List<CriterionDisposition>();
        var knownEmpty = false;
        var titleTermCandidates = new List<IReadOnlyList<string>>();
        var akaTermCandidates = new List<IReadOnlyList<string>>();

        foreach (var criterion in criteria.ProviderType.And)
        {
            var residualCount = residualDispositions.Count;
            var excludes = ExcludesProviderType(criterion, residualDispositions);
            knownEmpty |= excludes;
            if (excludes)
            {
                dispositions.Add(new CriterionDisposition("ProviderType", criterion, "known-empty"));
            }
            else if (residualDispositions.Count == residualCount)
            {
                dispositions.Add(new CriterionDisposition("ProviderType", criterion, "provider-constant-match"));
            }
        }

        foreach (var criterion in criteria.Source.And)
        {
            var filterCount = filters.Count;
            var residualCount = residualDispositions.Count;
            var excludes = CompileSource(criterion, filters, residualDispositions);
            knownEmpty |= excludes;
            if (excludes)
            {
                dispositions.Add(new CriterionDisposition("Source", criterion, "known-empty"));
            }
            else if (filters.Count == filterCount && residualDispositions.Count == residualCount)
            {
                dispositions.Add(new CriterionDisposition("Source", criterion, "provider-constant-match"));
            }

            var sourceSeed = CompileSourceSeed(criterion);
            if (sourceSeed.Count > 0)
            {
                idSeedCandidates.Add(sourceSeed);
            }
        }

        foreach (var criterion in criteria.Title.And)
        {
            var compiledTerm = CompileText(
                LocalFilterKind.Title,
                MetadataField.Title,
                criterion,
                filters,
                residualDispositions);
            if (compiledTerm.Count > 0)
            {
                titleTermCandidates.Add(compiledTerm);
            }
        }

        foreach (var criterion in criteria.OriginalTitle.And)
        {
            var compiledTerm = CompileText(
                LocalFilterKind.OriginalTitle,
                MetadataField.OriginalTitle,
                criterion,
                filters,
                residualDispositions);
            if (compiledTerm.Count > 0)
            {
                akaTermCandidates.Add(compiledTerm);
            }
        }

        foreach (var criterion in criteria.Language.And)
        {
            CompileText(LocalFilterKind.Language, MetadataField.Language, criterion, filters, residualDispositions);
        }

        foreach (var criterion in criteria.Overview.And)
        {
            CompileText(LocalFilterKind.Overview, MetadataField.Overview, criterion, filters, residualDispositions);
        }

        foreach (var criterion in criteria.ProductionStatus.And)
        {
            CompileText(
                LocalFilterKind.ProductionStatus,
                MetadataField.ProductionStatus,
                criterion,
                filters,
                residualDispositions);
        }

        foreach (var criterion in criteria.PublishDate.And)
        {
            CompileTemporal(
                LocalFilterKind.PublishDate,
                MetadataField.PublishDate,
                criterion,
                filters,
                residualDispositions);
        }

        foreach (var criterion in criteria.Established.And)
        {
            CompileTemporal(
                LocalFilterKind.Established,
                MetadataField.Established,
                criterion,
                filters,
                residualDispositions);
        }

        foreach (var criterion in criteria.Dissolved.And)
        {
            CompileTemporal(
                LocalFilterKind.Dissolved,
                MetadataField.Dissolved,
                criterion,
                filters,
                residualDispositions);
        }

        foreach (var criterion in criteria.Duration.And)
        {
            CompileDuration(criterion, filters, residualDispositions);
        }

        foreach (var criterion in criteria.BirthDate.And)
        {
            var excludes = AbsentPropertyExcludes(criterion);
            knownEmpty |= excludes;
            if (excludes)
            {
                dispositions.Add(new CriterionDisposition(
                    MetadataField.BirthDate.ToString(),
                    criterion,
                    "known-empty"));
            }
            else
            {
                dispositions.Add(new CriterionDisposition(
                    MetadataField.BirthDate.ToString(),
                    criterion,
                    "vacuous-omission"));
            }
        }

        foreach (var criterion in criteria.DeathDate.And)
        {
            var excludes = AbsentPropertyExcludes(criterion);
            knownEmpty |= excludes;
            if (excludes)
            {
                dispositions.Add(new CriterionDisposition(
                    MetadataField.DeathDate.ToString(),
                    criterion,
                    "known-empty"));
            }
            else
            {
                dispositions.Add(new CriterionDisposition(
                    MetadataField.DeathDate.ToString(),
                    criterion,
                    "vacuous-omission"));
            }
        }

        foreach (var dictionary in criteria.CustomMetadata.And)
        {
            foreach (var item in dictionary.Items)
            {
                var excludes = AbsentPropertyExcludes(item.Value);
                knownEmpty |= excludes;
                if (excludes)
                {
                    var source = string.Concat("CustomMetadata.", item.Key);
                    dispositions.Add(new CriterionDisposition(source, item.Value, "known-empty"));
                }
                else
                {
                    dispositions.Add(new CriterionDisposition(
                        string.Concat("CustomMetadata.", item.Key),
                        item.Value,
                        "vacuous-omission"));
                }
            }
        }

        if (criteria.Awards.Some.Count > 0)
        {
            knownEmpty = true;
            dispositions.Add(new CriterionDisposition(
                MetadataField.Awards.ToString(),
                criteria.Awards.Some,
                "known-empty"));
        }

        if (criteria.ContentRatings.Some.Count > 0)
        {
            knownEmpty = true;
            dispositions.Add(new CriterionDisposition(
                MetadataField.ContentRatings.ToString(),
                criteria.ContentRatings.Some,
                "known-empty"));
        }

        if (criteria.Previews.Some.Count > 0)
        {
            knownEmpty = true;
            dispositions.Add(new CriterionDisposition(
                MetadataField.Previews.ToString(),
                criteria.Previews.Some,
                "known-empty"));
        }

        if (criteria.Awards.None.Count > 0)
        {
            dispositions.Add(new CriterionDisposition(
                MetadataField.Awards.ToString(),
                criteria.Awards.None,
                "vacuous-omission"));
        }

        if (criteria.ContentRatings.None.Count > 0)
        {
            dispositions.Add(new CriterionDisposition(
                MetadataField.ContentRatings.ToString(),
                criteria.ContentRatings.None,
                "vacuous-omission"));
        }

        if (criteria.Previews.None.Count > 0)
        {
            dispositions.Add(new CriterionDisposition(
                MetadataField.Previews.ToString(),
                criteria.Previews.None,
                "vacuous-omission"));
        }

        if (criteria.Tags.Some.Count > 0)
        {
            knownEmpty = true;
            dispositions.Add(new CriterionDisposition("Tags", criteria.Tags.Some, "known-empty"));
        }

        if (criteria.Tags.None.Count > 0)
        {
            dispositions.Add(new CriterionDisposition("Tags", criteria.Tags.None, "vacuous-omission"));
        }

        foreach (var group in criteria.Genres.Some)
        {
            CompileGenreGroup(group, negate: false, filters, residualDispositions);
        }

        foreach (var excluded in criteria.Genres.None)
        {
            CompileGenreGroup([excluded], negate: true, filters, residualDispositions);
        }

        foreach (var group in criteria.ExternalIds.Some)
        {
            var excludes = CompileExternalIdGroup(group, negate: false, filters, residualDispositions);
            knownEmpty |= excludes;
            if (excludes)
            {
                dispositions.Add(new CriterionDisposition(MetadataField.ExternalIds.ToString(), group, "known-empty"));
            }

            var externalIdSeed = CompileExternalIdSeed(group);
            if (externalIdSeed.Count > 0)
            {
                idSeedCandidates.Add(externalIdSeed);
            }
        }

        foreach (var excluded in criteria.ExternalIds.None)
        {
            CompileExternalIdGroup([excluded], negate: true, filters, residualDispositions);
        }

        foreach (var group in criteria.AlternateTexts.Some)
        {
            var compiledTerm = CompileAlternateTextGroup(group, negate: false, filters, residualDispositions);
            if (compiledTerm.Count > 0)
            {
                akaTermCandidates.Add(compiledTerm);
            }
        }

        foreach (var excluded in criteria.AlternateTexts.None)
        {
            CompileAlternateTextGroup([excluded], negate: true, filters, residualDispositions);
        }

        foreach (var group in criteria.References.Some)
        {
            CompileReferenceGroup(group, negate: false, filters, residualDispositions);
        }

        foreach (var excluded in criteria.References.None)
        {
            CompileReferenceGroup([excluded], negate: true, filters, residualDispositions);
        }

        var imageCompilation = CompileImageResiduals(criteria.Images, residualDispositions);
        knownEmpty |= imageCompilation.KnownEmpty;
        dispositions.AddRange(imageCompilation.Dispositions);
        for (var groupIndex = 0; groupIndex < criteria.Relationships.Some.Count; groupIndex++)
        {
            var group = criteria.Relationships.Some[groupIndex];
            if (group.Count == 0)
            {
                dispositions.Add(new CriterionDisposition(
                    string.Concat("Relationships.Some[", (groupIndex + 1).ToString(CultureInfo.InvariantCulture), "]"),
                    group,
                    "vacuous-omission"));
            }
        }

        for (var exclusionIndex = 0; exclusionIndex < criteria.Relationships.None.Count; exclusionIndex++)
        {
            var exclusion = criteria.Relationships.None[exclusionIndex];
            if (exclusion.RelatedMetadataContainerId.And.Any())
            {
                dispositions.Add(new CriterionDisposition(
                    string.Concat(
                        "Relationships.None[",
                        (exclusionIndex + 1).ToString(CultureInfo.InvariantCulture),
                        "]"),
                    exclusion,
                    "core-owned-omission"));
            }
        }

        knownEmpty |= criteria.Relationships.Some.Any(group =>
            group.Count > 0 && group.All(IsImpossibleRelationship));
        var hitFilters = filters.Where(filter => !RequiresAkas(filter)).ToArray();
        var akaFilters = filters.Where(filter => RequiresAkas(filter)).ToArray();

        if (knownEmpty)
        {
            dispositions.AddRange(residualDispositions.Select(disposition =>
                disposition with { Disposition = "not-planned-known-empty" }));

            AddFilterDispositions(dispositions, hitFilters, "not-executed-known-empty");
            AddFilterDispositions(dispositions, akaFilters, "not-executed-known-empty");
            AddImageResidualDispositions(dispositions, imageCompilation.Residuals, "not-executed-known-empty");
            for (var groupIndex = 0; groupIndex < criteria.Relationships.Some.Count; groupIndex++)
            {
                var group = criteria.Relationships.Some[groupIndex];
                if (group.Count > 0)
                {
                    dispositions.Add(new CriterionDisposition(
                        string.Concat(
                            "Relationships.Some[",
                            (groupIndex + 1).ToString(CultureInfo.InvariantCulture),
                            "]"),
                        group,
                        "not-planned-known-empty"));
                }
            }

            for (var exclusionIndex = 0; exclusionIndex < criteria.Relationships.None.Count; exclusionIndex++)
            {
                var exclusion = criteria.Relationships.None[exclusionIndex];
                if (!exclusion.RelatedMetadataContainerId.And.Any())
                {
                    dispositions.Add(new CriterionDisposition(
                        string.Concat(
                            "Relationships.None[",
                            (exclusionIndex + 1).ToString(CultureInfo.InvariantCulture),
                            "]"),
                        exclusion,
                        "not-planned-known-empty",
                        Negate: true));
                }
            }

            return new SearchPlanNode.KnownEmpty([.. dispositions]);
        }

        var personSeeds = CompilePersonSeeds(criteria.Relationships.Some)
            .OrderBy(candidate => candidate.RequestCost)
            .ThenBy(candidate => candidate.GroupIndex)
            .ToArray();
        var personSeed = personSeeds.FirstOrDefault();
        SearchSeed? seed;
        string? seedReason = null;
        IReadOnlyCollection<int> settledRelationshipGroups = new HashSet<int>();
        var nativeIdLookups = (
            from candidate in idSeedCandidates
            where candidate.Count > 0
            where candidate.Any(lookup => lookup.ShowId.HasValue)
            orderby candidate.Count
            select candidate).FirstOrDefault();
        var externalIdLookups = (
            from candidate in idSeedCandidates
            where candidate.Count > 0
            where candidate.All(lookup => !lookup.ShowId.HasValue)
            orderby candidate.Count
            select candidate).FirstOrDefault();
        var idLookups = nativeIdLookups ?? externalIdLookups;
        if (idLookups is not null)
        {
            seed = new SearchSeed.IdLookup(idLookups);
            seedReason = "identifier lookup is the cheapest sufficient exact seed";
        }
        else if (personSeed is not null)
        {
            seed = personSeed.Seed;
            seedReason = "person relationship chain outranks fuzzy title search";
            settledRelationshipGroups = SettledRelationshipGroups(personSeeds);
        }
        else if (titleTermCandidates.Count > 0)
        {
            var titleTerms = titleTermCandidates.OrderBy(candidate => candidate.Count).First();
            seed = new SearchSeed.FuzzyShow(titleTerms, IsAka: false);
            seedReason = "title fuzzy search is cheaper than alternate-title verification or a person chain";
        }
        else if (akaTermCandidates.Count > 0)
        {
            var akaTerms = akaTermCandidates.OrderBy(candidate => candidate.Count).First();
            seed = new SearchSeed.FuzzyShow(akaTerms, IsAka: true);
            seedReason = "alternate-title fuzzy search is cheaper than a person chain and no cheaper seed exists";
        }
        else
        {
            seed = null;
        }

        var relationshipResiduals = CompileRelationshipResiduals(
            criteria.Relationships,
            settledRelationshipGroups,
            residualDispositions);
        for (var groupIndex = 0; groupIndex < criteria.Relationships.Some.Count; groupIndex++)
        {
            var group = criteria.Relationships.Some[groupIndex];
            if (settledRelationshipGroups.Contains(groupIndex))
            {
                dispositions.Add(new CriterionDisposition(
                    string.Concat("Relationships.Some[", (groupIndex + 1).ToString(CultureInfo.InvariantCulture), "]"),
                    group,
                    "seed-settled"));
            }
        }

        AddFilterDispositions(
            dispositions,
            hitFilters,
            "hit-filter");
        AddFilterDispositions(
            dispositions,
            akaFilters,
            "aka-verification");
        AddRelationshipResidualDispositions(dispositions, relationshipResiduals);
        AddImageResidualDispositions(dispositions, imageCompilation.Residuals);
        dispositions.AddRange(residualDispositions);

        SearchPlanNode terminalPlan;
        if (seed is null)
        {
            terminalPlan = new SearchPlanNode.Browse(
                hitFilters,
                akaFilters,
                relationshipResiduals,
                imageCompilation.Residuals,
                [.. dispositions]);
        }
        else
        {
            terminalPlan = new SearchPlanNode.Seeded(
                seed,
                seedReason ?? string.Empty,
                hitFilters,
                akaFilters,
                relationshipResiduals,
                imageCompilation.Residuals,
                [.. dispositions]);
        }

        return terminalPlan;
    }

    private QueryPlan BuildQueryPlan(
        ContentHierarchyItemCriteria query,
        MetadataCriteria metadata,
        int maximumResults,
        SearchPlanNode compiled)
    {
        var nextNodeId = 1;
        if (compiled is SearchPlanNode.KnownEmpty knownEmpty)
        {
            return new QueryPlan
            {
                Query = query,
                Metadata = metadata,
                MaximumResults = maximumResults,
                Root = new UnionPlanNode
                {
                    Id = nextNodeId,
                    Contributes = "known-empty show candidates",
                    Children = []
                },
                Dispositions = knownEmpty.Dispositions
            };
        }

        var (seed, hitFilters, akaFilters, relationshipResiduals, imageResiduals, dispositions) =
            compiled switch
            {
                SearchPlanNode.Seeded seeded =>
                    (
                        seeded.Seed,
                        seeded.HitFilters,
                        seeded.AkaFilters,
                        seeded.RelationshipResiduals,
                        seeded.ImageResiduals,
                        seeded.Dispositions),
                SearchPlanNode.Browse browse =>
                    (
                        new SearchSeed.Browse(),
                        browse.HitFilters,
                        browse.AkaFilters,
                        browse.RelationshipResiduals,
                        browse.ImageResiduals,
                        browse.Dispositions),
                _ => throw new InvalidOperationException("Unknown TVMaze search plan.")
            };
        var planDispositions = dispositions.ToList();
        if (seed is SearchSeed.Browse)
        {
            planDispositions.Add(new CriterionDisposition("Anchor", "shows?page", "browse-anchor"));
        }

        var anchor = BuildAnchor(seed, metadata, ref nextNodeId);
        var root = new FilterPlanNode
        {
            Id = nextNodeId,
            Contributes = "locally verified show candidates",
            Child = anchor,
            HitFilters = hitFilters,
            AkaFilters = akaFilters,
            RelationshipResiduals = relationshipResiduals,
            ImageResiduals = imageResiduals
        };
        return new QueryPlan
        {
            Query = query,
            Metadata = metadata,
            MaximumResults = maximumResults,
            Root = root,
            Dispositions = planDispositions
        };
    }

    private PlanNode BuildAnchor(SearchSeed seed, MetadataCriteria metadata, ref int nextNodeId)
    {
        if (seed is SearchSeed.PersonChain)
        {
            var personSeeds = CompilePersonSeeds(metadata.Relationships.Some)
                .OrderBy(candidate => candidate.RequestCost)
                .ThenBy(candidate => candidate.GroupIndex)
                .ToArray();
            if (personSeeds.Length > 1)
            {
                var children = new List<PlanNode>(personSeeds.Length);
                foreach (var candidate in personSeeds)
                {
                    children.Add(BuildPersonAnchor(candidate.Seed, ref nextNodeId));
                }

                return new IntersectPlanNode
                {
                    Id = nextNodeId++,
                    Contributes = "shows shared by required person groups",
                    Children = children
                };
            }
        }

        return seed switch
        {
            SearchSeed.IdLookup lookup => BuildLookupAnchor(lookup, ref nextNodeId),
            SearchSeed.FuzzyShow fuzzy => BuildFuzzyAnchor(fuzzy, ref nextNodeId),
            SearchSeed.PersonChain person => BuildPersonAnchor(person, ref nextNodeId),
            SearchSeed.Browse browse => new FetchPlanNode
            {
                Id = nextNodeId++,
                Contributes = "pageable show-index candidates",
                AnchorKind = SearchAnchorKind.Browse,
                Seed = browse
            },
            _ => throw new InvalidOperationException("Unknown TVMaze search seed.")
        };
    }

    private PlanNode BuildLookupAnchor(SearchSeed.IdLookup seed, ref int nextNodeId)
    {
        var children = new List<PlanNode>(seed.Lookups.Count);
        foreach (var lookup in seed.Lookups)
        {
            children.Add(new FetchPlanNode
            {
                Id = nextNodeId++,
                Contributes = lookup.ShowId.HasValue
                    ? "native show-id candidate"
                    : "external-id show candidate",
                AnchorKind = lookup.ShowId.HasValue
                    ? SearchAnchorKind.NativeId
                    : SearchAnchorKind.ExternalId,
                Seed = new SearchSeed.IdLookup([lookup])
            });
        }

        return children.Count == 1
            ? children[0]
            : new UnionPlanNode
            {
                Id = nextNodeId++,
                Contributes = "shows matching identifier alternatives",
                Children = children
            };
    }

    private PlanNode BuildFuzzyAnchor(SearchSeed.FuzzyShow seed, ref int nextNodeId)
    {
        var children = new List<PlanNode>(seed.Terms.Count);
        foreach (var term in seed.Terms)
        {
            children.Add(new FetchPlanNode
            {
                Id = nextNodeId++,
                Contributes = "unpaged show-search candidates",
                AnchorKind = SearchAnchorKind.Title,
                Seed = new SearchSeed.FuzzyShow([term], seed.IsAka)
            });
        }

        return children.Count == 1
            ? children[0]
            : new UnionPlanNode
            {
                Id = nextNodeId++,
                Contributes = "shows matching title alternatives",
                Children = children
            };
    }

    private PlanNode BuildPersonAnchor(SearchSeed.PersonChain seed, ref int nextNodeId)
    {
        var resolve = new ResolvePlanNode
        {
            Id = nextNodeId++,
            Contributes = "resolved person identities",
            Request = seed
        };
        var children = new List<PlanNode>();
        var castAlternatives = seed.Alternatives
            .Where(alternative => alternative.IncludeCast)
            .Select(alternative => alternative with { IncludeCrew = false, CrewKinds = [] })
            .ToArray();
        if (castAlternatives.Length > 0)
        {
            children.Add(new FetchPlanNode
            {
                Id = nextNodeId++,
                Contributes = "shows with cast credits for resolved people",
                AnchorKind = SearchAnchorKind.Person,
                Seed = new SearchSeed.PersonChain(castAlternatives),
                Resolve = resolve
            });
        }

        var crewAlternatives = seed.Alternatives
            .Where(alternative => alternative.IncludeCrew)
            .Select(alternative => alternative with { IncludeCast = false })
            .ToArray();
        if (crewAlternatives.Length > 0)
        {
            children.Add(new FetchPlanNode
            {
                Id = nextNodeId++,
                Contributes = "shows with crew credits for resolved people",
                AnchorKind = SearchAnchorKind.Person,
                Seed = new SearchSeed.PersonChain(crewAlternatives),
                Resolve = resolve
            });
        }

        return children.Count == 1
            ? children[0]
            : new UnionPlanNode
            {
                Id = nextNodeId++,
                Contributes = "shows credited to resolved people",
                Children = children
            };
    }

    private EligibilityEvaluation EvaluateEligibility(
        IEnumerable<PropertyCriterion<string>> criteria,
        string constant,
        string source)
    {
        var outcome = EligibilityOutcome.Included;
        var dispositions = new List<CriterionDisposition>();
        foreach (var criterion in criteria)
        {
            var (op, negate) = CriterionGrammar.InterpretOperator(criterion.Operator, criterion.Negate);
            if (!CriterionGrammar.IsTextOperator(op))
            {
                outcome = outcome == EligibilityOutcome.Excluded
                    ? outcome
                    : EligibilityOutcome.Unknown;

                dispositions.Add(new CriterionDisposition(source, criterion, "safe-superset", criterion.Negate));
                continue;
            }

            var values = CriterionGrammar.TextValues(criterion);
            if (op is not (CriteriaValueOperator.IsNull or CriteriaValueOperator.IsNotNull) && values.Length == 0)
            {
                outcome = outcome == EligibilityOutcome.Excluded
                    ? outcome
                    : EligibilityOutcome.Unknown;

                dispositions.Add(new CriterionDisposition(source, criterion, "safe-superset", criterion.Negate));
                continue;
            }

            var matches = CriterionGrammar.MatchesText(op, constant, values);
            matches = negate ? !matches : matches;
            if (matches)
            {
                dispositions.Add(new CriterionDisposition(
                    source,
                    criterion,
                    "provider-constant-match",
                    criterion.Negate));
            }
            else
            {
                outcome = EligibilityOutcome.Excluded;
                dispositions.Add(new CriterionDisposition(source, criterion, "known-empty", criterion.Negate));
            }
        }

        return new EligibilityEvaluation(outcome, dispositions);
    }

    private void AddFilterDispositions(
        List<CriterionDisposition> dispositions,
        IReadOnlyList<LocalFilter> filters,
        string disposition)
    {
        foreach (var filter in filters)
        {
            dispositions.Add(new CriterionDisposition(
                filter.Kind.ToString(),
                FilterOperatorsAndValues(filter),
                disposition,
                filter.Negate));
        }
    }

    private object FilterOperatorsAndValues(LocalFilter filter)
    {
        return filter.Kind switch
        {
            LocalFilterKind.Genre => filter.GenreAlternatives,
            LocalFilterKind.ExternalId => filter.ExternalIdAlternatives,
            LocalFilterKind.AlternateText => filter.AlternateTextAlternatives,
            LocalFilterKind.Reference => filter.ReferenceAlternatives,
            LocalFilterKind.PublishDate or LocalFilterKind.Established
                or LocalFilterKind.Dissolved => new { filter.Operator, Values = filter.TemporalValues },
            LocalFilterKind.Duration => new { filter.Operator, Values = filter.DurationValues },
            _ => new { filter.Operator, Values = filter.StringValues }
        };
    }

    private void AddRelationshipResidualDispositions(
        List<CriterionDisposition> dispositions,
        IReadOnlyList<RelationshipCriteriaResidual> residuals)
    {
        foreach (var residual in residuals)
        {
            var disposition = residual.Alternatives.Any(alternative => alternative.PassThrough)
                ? "pass-through-residual"
                : residual.Alternatives.Any(alternative => alternative.MatchesPerson)
                    ? residual.Alternatives.Any(alternative => alternative.MatchesNetwork)
                        ? "hit-or-credits-residual"
                        : "credits-residual"
                    : "hit-residual";
            dispositions.Add(new CriterionDisposition("relationship", residual, disposition, residual.Negate));
        }
    }

    private void AddImageResidualDispositions(
        List<CriterionDisposition> dispositions,
        IReadOnlyList<ImageCriteriaResidual> residuals)
    {
        foreach (var residual in residuals)
        {
            var disposition = residual.Alternatives.Any(alternative =>
                alternative.Kind == ImageCriteriaResidual.EvaluationKind.PassThrough)
                ? "pass-through-residual"
                : residual.Alternatives.Any(alternative =>
                    alternative.Kind == ImageCriteriaResidual.EvaluationKind.Presence)
                    ? "hit-or-images-residual"
                    : "images-residual";
            dispositions.Add(new CriterionDisposition("image", residual, disposition, residual.Negate));
        }
    }

    private void AddImageResidualDispositions(
        List<CriterionDisposition> dispositions,
        IReadOnlyList<ImageCriteriaResidual> residuals,
        string disposition)
    {
        foreach (var residual in residuals)
        {
            dispositions.Add(new CriterionDisposition("image", residual, disposition, residual.Negate));
        }
    }

    private IReadOnlyList<string> CompileText(
        LocalFilterKind kind,
        MetadataField field,
        PropertyCriterion<string> criterion,
        List<LocalFilter> filters,
        List<CriterionDisposition> residualDispositions)
    {
        var (op, negate) = CriterionGrammar.InterpretOperator(criterion.Operator, criterion.Negate);
        if (op is CriteriaValueOperator.IsNull or CriteriaValueOperator.IsNotNull)
        {
            filters.Add(new LocalFilter { Kind = kind, Operator = op, Negate = negate });
            return [];
        }

        if (!CriterionGrammar.IsTextOperator(op))
        {
            residualDispositions.Add(new CriterionDisposition(field.ToString(), criterion, "pass-through-residual"));
            return [];
        }

        var values = CriterionGrammar.TextValues(criterion);
        if (values.Length == 0)
        {
            residualDispositions.Add(new CriterionDisposition(field.ToString(), criterion, "pass-through-residual"));
            return [];
        }

        filters.Add(new LocalFilter { Kind = kind, Operator = op, Negate = negate, StringValues = values });
        return negate ? [] : values;
    }

    private void CompileTemporal(
        LocalFilterKind kind,
        MetadataField field,
        PropertyCriterion<TemporalValue> criterion,
        List<LocalFilter> filters,
        List<CriterionDisposition> residualDispositions)
    {
        var (op, negate) = CriterionGrammar.InterpretOperator(criterion.Operator, criterion.Negate);
        if (op is CriteriaValueOperator.IsNull or CriteriaValueOperator.IsNotNull)
        {
            filters.Add(new LocalFilter { Kind = kind, Operator = op, Negate = negate });
            return;
        }

        if (!CriterionGrammar.IsOrderedOperator(op))
        {
            residualDispositions.Add(new CriterionDisposition(field.ToString(), criterion, "pass-through-residual"));
            return;
        }

        var values = CriterionGrammar.TemporalValues(criterion);
        if (values.Length == 0)
        {
            residualDispositions.Add(new CriterionDisposition(field.ToString(), criterion, "pass-through-residual"));
            return;
        }

        filters.Add(new LocalFilter { Kind = kind, Operator = op, Negate = negate, TemporalValues = values });
    }

    private void CompileDuration(
        PropertyCriterion<TimeSpan> criterion,
        List<LocalFilter> filters,
        List<CriterionDisposition> residualDispositions)
    {
        var (op, negate) = CriterionGrammar.InterpretOperator(criterion.Operator, criterion.Negate);
        if (op is CriteriaValueOperator.IsNull or CriteriaValueOperator.IsNotNull)
        {
            filters.Add(new LocalFilter { Kind = LocalFilterKind.Duration, Operator = op, Negate = negate });
            return;
        }

        if (!CriterionGrammar.IsOrderedOperator(op))
        {
            residualDispositions.Add(new CriterionDisposition(
                MetadataField.Duration.ToString(),
                criterion,
                "pass-through-residual"));
            return;
        }

        var values = CriterionGrammar.DurationValues(criterion);
        if (values.Count == 0)
        {
            residualDispositions.Add(new CriterionDisposition(
                MetadataField.Duration.ToString(),
                criterion,
                "pass-through-residual"));
            return;
        }

        filters.Add(new LocalFilter
        {
            Kind = LocalFilterKind.Duration,
            Operator = op,
            Negate = negate,
            DurationValues = values
        });
    }

    private bool AbsentPropertyExcludes<T>(PropertyCriterion<T> criterion)
    {
        var (op, negate) = CriterionGrammar.InterpretOperator(criterion.Operator, criterion.Negate);
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

    private bool ExcludesProviderType(
        PropertyCriterion<string> criterion,
        List<CriterionDisposition> residualDispositions)
    {
        var (op, negate) = CriterionGrammar.InterpretOperator(criterion.Operator, criterion.Negate);
        if (op == CriteriaValueOperator.IsNull)
        {
            return true;
        }

        if (op == CriteriaValueOperator.IsNotNull)
        {
            return false;
        }

        if (!CriterionGrammar.IsTextOperator(op))
        {
            residualDispositions.Add(new CriterionDisposition("ProviderType", criterion, "pass-through-residual"));
            return false;
        }

        var values = CriterionGrammar.TextValues(criterion);
        if (values.Length == 0)
        {
            residualDispositions.Add(new CriterionDisposition("ProviderType", criterion, "pass-through-residual"));
            return false;
        }

        var matches = CriterionGrammar.MatchesText(op, Constants.ProviderType, values);
        return matches == negate;
    }

    private bool CompileSource(
        PropertyCriterion<string> criterion,
        List<LocalFilter> filters,
        List<CriterionDisposition> residualDispositions)
    {
        var (op, negate) = CriterionGrammar.InterpretOperator(criterion.Operator, criterion.Negate);
        if (op == CriteriaValueOperator.IsNull)
        {
            return true;
        }

        if (op == CriteriaValueOperator.IsNotNull)
        {
            return false;
        }

        if (!CriterionGrammar.IsTextOperator(op))
        {
            residualDispositions.Add(new CriterionDisposition("Source", criterion, "pass-through-residual"));
            return false;
        }

        var values = CriterionGrammar.TextValues(criterion);
        if (values.Length == 0)
        {
            residualDispositions.Add(new CriterionDisposition("Source", criterion, "pass-through-residual"));
            return false;
        }

        if (!negate && !values.Any(value => SourceCouldMatch(op, value)))
        {
            return true;
        }

        if (negate && values.Any(value => SourceAlwaysMatches(op, value)))
        {
            return true;
        }

        filters.Add(new LocalFilter
        {
            Kind = LocalFilterKind.Source,
            Operator = op,
            Negate = negate,
            StringValues = values
        });
        return false;
    }

    private IReadOnlyList<SearchSeed.Lookup> CompileSourceSeed(PropertyCriterion<string> criterion)
    {
        var (op, negate) = CriterionGrammar.InterpretOperator(criterion.Operator, criterion.Negate);
        if (negate || op is not (CriteriaValueOperator.Equals or CriteriaValueOperator.In))
        {
            return [];
        }

        var lookups = new List<SearchSeed.Lookup>();
        foreach (var value in CriterionGrammar.TextValues(criterion))
        {
            if (!TryParseShowSource(value, out var showId))
            {
                continue;
            }

            var id = showId.ToString(CultureInfo.InvariantCulture);
            lookups.Add(new SearchSeed.Lookup(Constants.ProviderType, id, showId));
        }

        return [.. lookups.Distinct()];
    }

    private bool TryParseShowSource(string value, out int showId)
    {
        showId = 0;
        var candidate = value.Trim();
        if (!candidate.StartsWith(Constants.ShowSourcePrefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var id = candidate.Substring(Constants.ShowSourcePrefix.Length);
        return IsValidId(id) &&
            int.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out showId) &&
            showId > 0;
    }

    private bool SourceCouldMatch(CriteriaValueOperator op, string value)
    {
        var candidate = value.Trim().ToLowerInvariant();
        var prefix = Constants.ShowSourcePrefix;
        switch (op)
        {
            case CriteriaValueOperator.Equals or CriteriaValueOperator.In:
                return candidate.Length > prefix.Length &&
                    candidate.StartsWith(prefix, StringComparison.Ordinal) &&
                    IsValidId(candidate.Substring(prefix.Length));
            case CriteriaValueOperator.StartsWithString:
                if (prefix.StartsWith(candidate, StringComparison.Ordinal))
                {
                    return true;
                }

                return candidate.StartsWith(prefix, StringComparison.Ordinal) &&
                    IsValidId(candidate.Substring(prefix.Length));
            case CriteriaValueOperator.EndsWithString:
                {
                    var (head, digits) = SplitTrailingDigits(candidate);
                    if (head.Length == 0)
                    {
                        return digits.Length > 0;
                    }

                    return digits.Length > 0 &&
                        digits[0] != '0' &&
                        prefix.EndsWith(head, StringComparison.Ordinal);
                }
            case CriteriaValueOperator.ContainsString:
                {
                    var (head, digits) = SplitTrailingDigits(candidate);
                    if (head.Length == 0)
                    {
                        return true;
                    }

                    if (digits.Length == 0)
                    {
                        return prefix.Contains(head, StringComparison.Ordinal);
                    }

                    return digits[0] != '0' && prefix.EndsWith(head, StringComparison.Ordinal);
                }
            case CriteriaValueOperator.ContainsWord:
                return ContainsWordSamples(candidate).Any(source =>
                    CriterionGrammar.MatchesText(CriteriaValueOperator.ContainsWord, source, [candidate]));
            default:
                return true;
        }
    }

    private bool SourceAlwaysMatches(CriteriaValueOperator op, string value)
    {
        return SampleSources.All(source => CriterionGrammar.MatchesText(op, source, [value]));
    }

    private IEnumerable<string> ContainsWordSamples(string candidate)
    {
        yield return Constants.ShowSourcePrefix + "1";
        if (IsValidId(candidate))
        {
            yield return Constants.ShowSourcePrefix + candidate;
        }

        if (candidate.StartsWith(Constants.ShowSourcePrefix, StringComparison.Ordinal) &&
            IsValidId(candidate.Substring(Constants.ShowSourcePrefix.Length)))
        {
            yield return candidate;
        }
    }

    private bool IsValidId(string value) => value.Length > 0 && value[0] != '0' && value.All(char.IsDigit);

    private (string Head, string Digits) SplitTrailingDigits(string value)
    {
        var boundary = value.Length;
        while (boundary > 0 && char.IsDigit(value[boundary - 1]))
        {
            boundary--;
        }

        return (value.Substring(0, boundary), value.Substring(boundary));
    }

    private void CompileGenreGroup(
        IReadOnlyList<GenreCriteria> alternatives,
        bool negate,
        List<LocalFilter> filters,
        List<CriterionDisposition> residualDispositions)
    {
        foreach (var alternative in alternatives)
        {
            if (!ValidateItemCriteria(alternative.Name.And, MetadataField.Genres, residualDispositions))
            {
                return;
            }
        }

        filters.Add(new LocalFilter
        {
            Kind = LocalFilterKind.Genre,
            Negate = negate,
            GenreAlternatives = alternatives
        });
    }

    private bool CompileExternalIdGroup(
        IReadOnlyList<ExternalIdCriteria> alternatives,
        bool negate,
        List<LocalFilter> filters,
        List<CriterionDisposition> residualDispositions)
    {
        foreach (var alternative in alternatives)
        {
            if (!ValidateItemCriteria(alternative.Source.And, MetadataField.ExternalIds, residualDispositions) ||
                !ValidateItemCriteria(alternative.Value.And, MetadataField.ExternalIds, residualDispositions))
            {
                return false;
            }
        }

        if (!negate &&
            alternatives.Count > 0 &&
            alternatives.All(alternative => !AlternativeCouldMatchSources(alternative)))
        {
            return true;
        }

        filters.Add(new LocalFilter
        {
            Kind = LocalFilterKind.ExternalId,
            Negate = negate,
            ExternalIdAlternatives = alternatives
        });
        return false;
    }

    private bool AlternativeCouldMatchSources(ExternalIdCriteria alternative)
    {
        foreach (var criterion in alternative.Source.And)
        {
            var (op, negate) = CriterionGrammar.InterpretOperator(criterion.Operator, criterion.Negate);
            if (negate || op is not (CriteriaValueOperator.Equals or CriteriaValueOperator.In))
            {
                continue;
            }

            var values = CriterionGrammar.TextValues(criterion);
            if (values.Length == 0)
            {
                continue;
            }

            if (!values.Any(value => KnownExternalIdSources.Contains(value, StringComparer.OrdinalIgnoreCase)))
            {
                return false;
            }
        }

        return true;
    }

    private IReadOnlyList<SearchSeed.Lookup> CompileExternalIdSeed(IReadOnlyList<ExternalIdCriteria> alternatives)
    {
        var lookups = new List<SearchSeed.Lookup>();
        foreach (var alternative in alternatives)
        {
            if (!TryIntersectPositiveText(alternative.Source.And, out var sources) ||
                !TryIntersectPositiveText(alternative.Value.And, out var values))
            {
                return [];
            }

            var possibleSources = sources
                .Where(source => KnownExternalIdSources.Contains(source, StringComparer.OrdinalIgnoreCase))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (possibleSources.Contains(Constants.ProviderType))
            {
                foreach (var value in values.OrderBy(value => value, StringComparer.OrdinalIgnoreCase))
                {
                    if (!IsValidId(value) ||
                        !int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var showId) ||
                        showId <= 0)
                    {
                        continue;
                    }

                    lookups.Add(new SearchSeed.Lookup(Constants.ProviderType, value, showId));
                }
            }

            possibleSources.IntersectWith(
                [Constants.ImdbSource, Constants.TheTvdbSource, Constants.TvRageSource]);
            if (possibleSources.Count == 0)
            {
                continue;
            }

            foreach (var source in possibleSources.OrderBy(value => value, StringComparer.OrdinalIgnoreCase))
            {
                var canonicalSource = source.ToLowerInvariant();
                foreach (var value in values.OrderBy(value => value, StringComparer.OrdinalIgnoreCase))
                {
                    if (lookups.Any(existing => existing.Source == canonicalSource &&
                        existing.Value.Equals(value, StringComparison.OrdinalIgnoreCase)))
                    {
                        continue;
                    }

                    lookups.Add(new SearchSeed.Lookup(canonicalSource, value, ShowId: null));
                }
            }
        }

        return lookups;
    }

    private bool TryIntersectPositiveText(
        IEnumerable<PropertyCriterion<string>> criteria,
        out HashSet<string> intersection)
    {
        intersection = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var first = true;
        foreach (var criterion in criteria)
        {
            var (op, negate) = CriterionGrammar.InterpretOperator(criterion.Operator, criterion.Negate);
            if (negate || op is not (CriteriaValueOperator.Equals or CriteriaValueOperator.In))
            {
                return false;
            }

            var values = CriterionGrammar.TextValues(criterion)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (values.Count == 0)
            {
                return false;
            }

            if (first)
            {
                intersection.UnionWith(values);
                first = false;
            }
            else
            {
                intersection.IntersectWith(values);
            }
        }

        return !first;
    }

    private IReadOnlyList<string> CompileAlternateTextGroup(
        IReadOnlyList<AlternateTextCriteria> alternatives,
        bool negate,
        List<LocalFilter> filters,
        List<CriterionDisposition> residualDispositions)
    {
        foreach (var alternative in alternatives)
        {
            if (!ValidateItemCriteria(alternative.Kind.And, MetadataField.AlternateTexts, residualDispositions) ||
                !ValidateItemCriteria(alternative.Language.And, MetadataField.AlternateTexts, residualDispositions) ||
                !ValidateItemCriteria(alternative.Text.And, MetadataField.AlternateTexts, residualDispositions))
            {
                return [];
            }
        }

        filters.Add(new LocalFilter
        {
            Kind = LocalFilterKind.AlternateText,
            Negate = negate,
            AlternateTextAlternatives = alternatives
        });
        return negate
            ? []
            : [.. alternatives
                .SelectMany(alternative => PositiveTexts(alternative.Text.And))
                .Distinct(StringComparer.OrdinalIgnoreCase)];
    }

    private IReadOnlyList<string> PositiveTexts(IEnumerable<PropertyCriterion<string>> criteria)
    {
        foreach (var criterion in criteria)
        {
            var (op, negate) = CriterionGrammar.InterpretOperator(criterion.Operator, criterion.Negate);
            if (negate ||
                !CriterionGrammar.IsTextOperator(op) ||
                op is CriteriaValueOperator.IsNull or CriteriaValueOperator.IsNotNull)
            {
                continue;
            }

            var values = CriterionGrammar.TextValues(criterion);
            if (values.Length > 0)
            {
                return values;
            }
        }

        return [];
    }

    private void CompileReferenceGroup(
        IReadOnlyList<ReferenceMetadataCriteria> alternatives,
        bool negate,
        List<LocalFilter> filters,
        List<CriterionDisposition> residualDispositions)
    {
        foreach (var alternative in alternatives)
        {
            if (!ValidateItemCriteria(alternative.Kind.And, MetadataField.References, residualDispositions) ||
                !ValidateItemCriteria(alternative.DisplayName.And, MetadataField.References, residualDispositions) ||
                !ValidateItemCriteria(alternative.Language.And, MetadataField.References, residualDispositions))
            {
                return;
            }

            foreach (var criterion in alternative.Url.And)
            {
                var (op, _) = CriterionGrammar.InterpretOperator(criterion.Operator, criterion.Negate);
                if (!CriterionGrammar.IsTextOperator(op))
                {
                    residualDispositions.Add(new CriterionDisposition(
                        MetadataField.References.ToString(),
                        criterion,
                        "pass-through-residual"));
                    return;
                }
            }

            foreach (var criterion in alternative.Ordinal.And)
            {
                var (op, _) = CriterionGrammar.InterpretOperator(criterion.Operator, criterion.Negate);
                if (!CriterionGrammar.IsOrderedOperator(op))
                {
                    residualDispositions.Add(new CriterionDisposition(
                        MetadataField.References.ToString(),
                        criterion,
                        "pass-through-residual"));
                    return;
                }
            }
        }

        filters.Add(new LocalFilter
        {
            Kind = LocalFilterKind.Reference,
            Negate = negate,
            ReferenceAlternatives = alternatives
        });
    }

    private ImageResidualCompilation CompileImageResiduals(
        CollectionCriteria<ImageCriteria> criteria,
        List<CriterionDisposition> residualDispositions)
    {
        var residuals = new List<ImageCriteriaResidual>();
        var dispositions = new List<CriterionDisposition>();
        var knownEmpty = false;
        for (var groupIndex = 0; groupIndex < criteria.Some.Count; groupIndex++)
        {
            var group = criteria.Some[groupIndex];
            var source = string.Concat("Images.Some[", (groupIndex + 1).ToString(CultureInfo.InvariantCulture), "]");
            if (group.Count == 0)
            {
                dispositions.Add(new CriterionDisposition(source, group, "vacuous-omission"));
                continue;
            }

            var alternatives = new List<ImageCriteriaResidual.Alternative>();
            for (var alternativeIndex = 0; alternativeIndex < group.Count; alternativeIndex++)
            {
                var image = group[alternativeIndex];
                if (!TryCompileImageAlternative(image, out var alternative, out var impossible))
                {
                    residualDispositions.Add(new CriterionDisposition(source, image, "pass-through-residual"));
                    alternatives.Add(new ImageCriteriaResidual.Alternative(
                        image,
                        ImageCriteriaResidual.EvaluationKind.PassThrough));
                    continue;
                }

                if (impossible)
                {
                    dispositions.Add(new CriterionDisposition(
                        string.Concat(
                            source,
                            ".Alternative[",
                            (alternativeIndex + 1).ToString(CultureInfo.InvariantCulture),
                            "]"),
                        image,
                        "vacuous-omission"));
                    continue;
                }

                alternatives.Add(alternative!);
            }

            if (alternatives.Count == 0)
            {
                knownEmpty = true;
                dispositions.Add(new CriterionDisposition(source, group, "known-empty"));
                continue;
            }

            residuals.Add(new ImageCriteriaResidual(Negate: false, alternatives));
        }

        for (var exclusionIndex = 0; exclusionIndex < criteria.None.Count; exclusionIndex++)
        {
            var image = criteria.None[exclusionIndex];
            if (!TryCompileImageAlternative(image, out var alternative, out var impossible))
            {
                residualDispositions.Add(new CriterionDisposition(
                    string.Concat("Images.None[", (exclusionIndex + 1).ToString(CultureInfo.InvariantCulture), "]"),
                    image,
                    "pass-through-residual"));
                continue;
            }

            if (impossible)
            {
                dispositions.Add(new CriterionDisposition(
                    string.Concat("Images.None[", (exclusionIndex + 1).ToString(CultureInfo.InvariantCulture), "]"),
                    image,
                    "vacuous-omission",
                    Negate: true));
                continue;
            }

            residuals.Add(new ImageCriteriaResidual(Negate: true, [alternative!]));
        }

        return new ImageResidualCompilation(residuals, knownEmpty, dispositions);
    }

    private bool TryCompileImageAlternative(
        ImageCriteria criteria,
        out ImageCriteriaResidual.Alternative? alternative,
        out bool impossible)
    {
        alternative = null;
        var requiredFieldCriteria = criteria.Type.And
            .Concat(criteria.Url.And)
            .Concat(criteria.Source.And)
            .ToArray();
        impossible = requiredFieldCriteria.Any(criterion =>
            CriterionGrammar.InterpretOperator(criterion.Operator, criterion.Negate).Operator ==
                CriteriaValueOperator.IsNull);
        if (impossible)
        {
            return true;
        }

        var typeValid = ValidateImageText(criteria.Type.And);
        var sourceValid = ValidateImageText(criteria.Source.And);
        if ((typeValid && !FiniteDomainCanMatch(criteria.Type.And, Constants.ShowImageTypes)) ||
            (sourceValid && !FiniteDomainCanMatch(criteria.Source.And, [Constants.ProviderType])))
        {
            impossible = true;
            return true;
        }

        if (!typeValid)
        {
            return false;
        }

        if (!sourceValid)
        {
            return false;
        }

        if (!ValidateImageText(criteria.MimeType.And) ||
            !ValidateImageText(criteria.Language.And) ||
            !ValidateImageText(criteria.Url.And) ||
            !ValidateImageNumber(criteria.Width.And) ||
            !ValidateImageNumber(criteria.Height.And))
        {
            return false;
        }

        var presenceOnly = !criteria.MimeType.And.Any() &&
            !criteria.Language.And.Any() &&
            !criteria.Width.And.Any() &&
            !criteria.Height.And.Any() &&
            requiredFieldCriteria.All(criterion =>
                CriterionGrammar.InterpretOperator(criterion.Operator, criterion.Negate).Operator ==
                    CriteriaValueOperator.IsNotNull);
        alternative = new ImageCriteriaResidual.Alternative(
            criteria,
            presenceOnly
                ? ImageCriteriaResidual.EvaluationKind.Presence
                : ImageCriteriaResidual.EvaluationKind.Gallery);

        return true;
    }

    private bool FiniteDomainCanMatch(IEnumerable<PropertyCriterion<string>> criteria, IReadOnlyList<string> domain)
    {
        var constraints = criteria.ToArray();
        return domain.Any(candidate => constraints.All(criterion => CriterionGrammar.Matches(criterion, candidate)));
    }

    private bool ValidateImageText(IEnumerable<PropertyCriterion<string>> criteria)
    {
        foreach (var criterion in criteria)
        {
            var (op, _) = CriterionGrammar.InterpretOperator(criterion.Operator, criterion.Negate);
            if (!CriterionGrammar.IsTextOperator(op))
            {
                return false;
            }

            if (op is not (CriteriaValueOperator.IsNull or CriteriaValueOperator.IsNotNull) &&
                CriterionGrammar.TextValues(criterion).Length == 0)
            {
                return false;
            }
        }

        return true;
    }

    private bool ValidateImageNumber(IEnumerable<PropertyCriterion<int>> criteria)
    {
        foreach (var criterion in criteria)
        {
            var (op, _) = CriterionGrammar.InterpretOperator(criterion.Operator, criterion.Negate);
            if (!CriterionGrammar.IsOrderedOperator(op))
            {
                return false;
            }
        }

        return true;
    }

    private IReadOnlyList<RelationshipCriteriaResidual> CompileRelationshipResiduals(
        CollectionCriteria<RelationshipCriteria> criteria,
        IReadOnlyCollection<int> settledGroupIndexes,
        List<CriterionDisposition> residualDispositions)
    {
        var residuals = new List<RelationshipCriteriaResidual>();
        for (var groupIndex = 0; groupIndex < criteria.Some.Count; groupIndex++)
        {
            if (settledGroupIndexes.Contains(groupIndex) || criteria.Some[groupIndex].Count == 0)
            {
                continue;
            }

            var alternatives = new List<RelationshipCriteriaResidual.Alternative>();
            foreach (var relationship in criteria.Some[groupIndex])
            {
                if (!HasProviderRelationshipFacets(relationship))
                {
                    residualDispositions.Add(new CriterionDisposition(
                        string.Concat(
                            "Relationships.Some[",
                            (groupIndex + 1).ToString(CultureInfo.InvariantCulture),
                            "]"), relationship, "pass-through-residual"));
                    alternatives.Add(new RelationshipCriteriaResidual.Alternative(
                        relationship,
                        MatchesNetwork: false,
                        MatchesPerson: false,
                        PassThrough: true));
                    continue;
                }

                if (!ValidateRelationship(relationship))
                {
                    residualDispositions.Add(new CriterionDisposition(
                        string.Concat(
                            "Relationships.Some[",
                            (groupIndex + 1).ToString(CultureInfo.InvariantCulture),
                            "]"), relationship, "pass-through-residual"));
                    alternatives.Add(new RelationshipCriteriaResidual.Alternative(
                        relationship,
                        MatchesNetwork: false,
                        MatchesPerson: false,
                        PassThrough: true));
                    continue;
                }

                var compiled = CompileRelationshipAlternative(relationship);
                if (!compiled.MatchesNetwork && !compiled.MatchesPerson)
                {
                    residualDispositions.Add(new CriterionDisposition(
                        string.Concat(
                            "Relationships.Some[",
                            (groupIndex + 1).ToString(CultureInfo.InvariantCulture),
                            "]"), relationship, "pass-through-residual"));
                    compiled = compiled with { PassThrough = true };
                }

                alternatives.Add(compiled);
            }

            residuals.Add(new RelationshipCriteriaResidual(groupIndex, Negate: false, alternatives));
        }

        for (var exclusionIndex = 0; exclusionIndex < criteria.None.Count; exclusionIndex++)
        {
            var relationship = criteria.None[exclusionIndex];
            if (relationship.RelatedMetadataContainerId.And.Any())
            {
                continue;
            }

            if (!HasProviderRelationshipFacets(relationship))
            {
                residualDispositions.Add(new CriterionDisposition(
                    string.Concat(
                        "Relationships.None[",
                        (exclusionIndex + 1).ToString(CultureInfo.InvariantCulture),
                        "]"), relationship, "pass-through-residual"));
                continue;
            }

            if (!ValidateRelationship(relationship))
            {
                residualDispositions.Add(new CriterionDisposition(
                    string.Concat(
                        "Relationships.None[",
                        (exclusionIndex + 1).ToString(CultureInfo.InvariantCulture),
                        "]"), relationship, "pass-through-residual"));
                continue;
            }

            var compiled = CompileRelationshipAlternative(relationship);
            if (!compiled.MatchesNetwork && !compiled.MatchesPerson)
            {
                residualDispositions.Add(new CriterionDisposition(
                    string.Concat(
                        "Relationships.None[",
                        (exclusionIndex + 1).ToString(CultureInfo.InvariantCulture),
                        "]"), relationship, "pass-through-residual"));
                continue;
            }

            residuals.Add(new RelationshipCriteriaResidual(SourceGroupIndex: null, Negate: true, [compiled]));
        }

        return residuals;
    }

    private RelationshipCriteriaResidual.Alternative CompileRelationshipAlternative(RelationshipCriteria relationship)
    {
        var matchesNetwork = CanMatchRelationship(
            relationship,
            RelationshipKinds.StudioSubject,
            RelationshipKinds.Network,
            Constants.ProviderType);
        var matchesPerson = RelationshipKinds.PersonRelationships.Any(kind => CanMatchRelationship(
            relationship,
            RelationshipKinds.PersonSubject,
            kind,
            Constants.ProviderType));
        return new RelationshipCriteriaResidual.Alternative(
            relationship,
            matchesNetwork,
            matchesPerson,
            PassThrough: false);
    }

    private bool CanMatchRelationship(
        RelationshipCriteria relationship,
        string subjectKind,
        string relationshipKind,
        string externalIdSource)
    {
        return MatchesSetAny(relationship.Kind, subjectKind, relationshipKind) &&
            MatchesSetAny(relationship.Type, relationshipKind, subjectKind) &&
            relationship.ExternalIds.Some.All(group => group.Any(alternative =>
                MatchesSetAny(alternative.Source, externalIdSource)));
    }

    private bool MatchesSetAny(CriteriaSet<PropertyCriterion<string>> criteria, params string[] candidates)
    {
        var constraints = criteria.And.ToArray();
        return constraints.Length == 0 ||
            candidates.Any(candidate => constraints.All(criterion => CriterionGrammar.Matches(criterion, candidate)));
    }

    private bool IsImpossibleRelationship(RelationshipCriteria relationship) =>
        !relationship.RelatedMetadataContainerId.And.Any() &&
        HasProviderRelationshipFacets(relationship) &&
        ValidateRelationship(relationship) &&
        CompileRelationshipAlternative(relationship) is { MatchesNetwork: false, MatchesPerson: false };

    private bool HasProviderRelationshipFacets(RelationshipCriteria relationship)
    {
        return relationship.Kind.And.Any() ||
            relationship.Type.And.Any() ||
            relationship.Qualifier.And.Any() ||
            relationship.Subject.And.Any() ||
            relationship.ExternalIds.Some.Count > 0 ||
            relationship.ExternalIds.None.Count > 0;
    }

    private bool ValidateRelationship(RelationshipCriteria relationship)
    {
        if (!ValidateRelationshipText(relationship.Kind.And) ||
            !ValidateRelationshipText(relationship.Type.And) ||
            !ValidateRelationshipText(relationship.Qualifier.And) ||
            !ValidateRelationshipText(relationship.Subject.And))
        {
            return false;
        }

        foreach (var group in relationship.ExternalIds.Some)
        {
            foreach (var alternative in group)
            {
                if (!ValidateExternalId(alternative))
                {
                    return false;
                }

                if (!ExternalIdCanTargetKnownRelationship(alternative))
                {
                    return false;
                }
            }
        }

        foreach (var excluded in relationship.ExternalIds.None)
        {
            if (!ValidateExternalId(excluded))
            {
                return false;
            }

            if (!ExternalIdCanTargetKnownRelationship(excluded))
            {
                return false;
            }
        }

        return true;
    }

    private bool ValidateExternalId(ExternalIdCriteria criteria)
    {
        return ValidateRelationshipText(criteria.Source.And) && ValidateRelationshipText(criteria.Value.And);
    }

    private bool ExternalIdCanTargetKnownRelationship(ExternalIdCriteria criteria)
    {
        return MatchesSetAny(criteria.Source, Constants.ProviderType);
    }

    private bool ValidateRelationshipText(IEnumerable<PropertyCriterion<string>> criteria)
    {
        foreach (var criterion in criteria)
        {
            var (op, _) = CriterionGrammar.InterpretOperator(criterion.Operator, criterion.Negate);
            if (!CriterionGrammar.IsTextOperator(op))
            {
                return false;
            }

            if (op is not (CriteriaValueOperator.IsNull or CriteriaValueOperator.IsNotNull) &&
                CriterionGrammar.TextValues(criterion).Length == 0)
            {
                return false;
            }
        }

        return true;
    }

    private IReadOnlyList<PersonSeedCandidate> CompilePersonSeeds(IReadOnlyList<List<RelationshipCriteria>> groups)
    {
        var candidates = new List<PersonSeedCandidate>();
        for (var groupIndex = 0; groupIndex < groups.Count; groupIndex++)
        {
            var group = groups[groupIndex];
            if (group.Count == 0)
            {
                continue;
            }

            var alternatives = new List<SearchSeed.PersonAlternative>(group.Count);
            var complete = true;
            var settled = true;
            foreach (var relationship in group)
            {
                if (!TryCompilePersonAlternative(relationship, out var alternative, out var alternativeIsSettled))
                {
                    complete = false;
                    break;
                }

                alternatives.Add(alternative);
                settled &= alternativeIsSettled;
            }

            if (complete)
            {
                var seed = new SearchSeed.PersonChain(alternatives);
                candidates.Add(new PersonSeedCandidate(groupIndex, seed, settled, PersonSeedRequestCost(seed)));
            }
        }

        return candidates;
    }

    private IReadOnlyCollection<int> SettledRelationshipGroups(IReadOnlyList<PersonSeedCandidate> candidates)
    {
        return candidates
            .Where(candidate => candidate.IsSettled)
            .Select(candidate => candidate.GroupIndex)
            .ToHashSet();
    }

    private int PersonSeedRequestCost(SearchSeed.PersonChain seed)
    {
        var termCount = seed.Alternatives
            .SelectMany(alternative => alternative.Names)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
        termCount += seed.Alternatives
            .SelectMany(alternative => alternative.PersonIds)
            .Distinct()
            .Count();
        var endpointCount = seed.Alternatives.Any(alternative => alternative.IncludeCast) ? 1 : 0;
        endpointCount += seed.Alternatives.Any(alternative => alternative.IncludeCrew) ? 1 : 0;
        return termCount + endpointCount;
    }

    private bool TryCompilePersonAlternative(
        RelationshipCriteria relationship,
        [NotNullWhen(true)] out SearchSeed.PersonAlternative? alternative,
        out bool isSettled)
    {
        alternative = null;
        var hasNames = TryCompilePersonNameTerms(relationship.Subject.And, out var names);
        var hasExactPersonIds = TryCompilePersonIds(relationship.ExternalIds, out var personIds);
        if (!hasNames && personIds.Count == 0)
        {
            isSettled = false;
            return false;
        }

        isSettled = !relationship.Qualifier.And.Any() &&
            relationship.ExternalIds.None.Count == 0 &&
            (hasNames && relationship.ExternalIds.Some.Count == 0 ||
                !relationship.Subject.And.Any() && hasExactPersonIds);
        var direction = CompileRelationshipAlternative(relationship);
        if (!direction.MatchesPerson || direction.MatchesNetwork)
        {
            return false;
        }

        if (!TryCompilePersonRoles(relationship, out var roles))
        {
            return false;
        }

        var includeCast = roles.Contains(RelationshipKinds.Cast);
        var crewKinds = roles
            .Where(role => role != RelationshipKinds.Cast)
            .OrderBy(role => role, StringComparer.Ordinal)
            .ToArray();
        alternative = new SearchSeed.PersonAlternative(
            [.. names.OrderBy(name => name, StringComparer.OrdinalIgnoreCase)],
            [.. relationship.Subject.And],
            personIds,
            includeCast,
            crewKinds.Length > 0,
            crewKinds.Length == RelationshipKinds.PersonRelationships.Count - 1 ? [] : crewKinds);
        return true;
    }

    private bool TryCompilePersonNameTerms(
        IEnumerable<PropertyCriterion<string>> criteria,
        out IReadOnlyList<string> terms)
    {
        var candidates = new List<IReadOnlyList<string>>();
        foreach (var criterion in criteria)
        {
            var (op, negate) = CriterionGrammar.InterpretOperator(criterion.Operator, criterion.Negate);
            if (!CriterionGrammar.IsTextOperator(op))
            {
                terms = [];
                return false;
            }

            var values = CriterionGrammar.TextValues(criterion);
            if (op is not (CriteriaValueOperator.IsNull or CriteriaValueOperator.IsNotNull) && values.Length == 0)
            {
                terms = [];
                return false;
            }

            if (!negate &&
                op is CriteriaValueOperator.Equals
                    or CriteriaValueOperator.In
                    or CriteriaValueOperator.ContainsString
                    or CriteriaValueOperator.ContainsWord
                    or CriteriaValueOperator.StartsWithString
                    or CriteriaValueOperator.EndsWithString)
            {
                candidates.Add(values);
            }
        }

        terms = candidates
            .OrderBy(candidate => candidate.Count)
            .FirstOrDefault() ??
            [];
        return terms.Count > 0;
    }

    private bool TryCompilePersonIds(
        CollectionCriteria<ExternalIdCriteria> externalIds,
        out IReadOnlyList<long> personIds)
    {
        personIds = [];
        if (externalIds.Some.Count == 0)
        {
            return false;
        }

        HashSet<long>? intersection = null;
        foreach (var group in externalIds.Some)
        {
            if (group.Count == 0)
            {
                return false;
            }

            var groupIds = new HashSet<long>();
            foreach (var alternative in group)
            {
                if (!TryIntersectPositiveText(alternative.Source.And, out var sources) ||
                    sources.Any(source => !source.Equals(
                        Constants.ProviderType,
                        StringComparison.OrdinalIgnoreCase)) ||
                    !TryIntersectPositiveText(alternative.Value.And, out var values))
                {
                    return false;
                }

                foreach (var value in values)
                {
                    if (!long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var personId) ||
                        personId <= 0)
                    {
                        return false;
                    }

                    groupIds.Add(personId);
                }
            }

            if (groupIds.Count == 0)
            {
                return false;
            }

            if (intersection is null)
            {
                intersection = groupIds;
            }
            else
            {
                intersection.IntersectWith(groupIds);
            }
        }

        if (intersection is null || intersection.Count == 0)
        {
            return false;
        }

        personIds = [.. intersection.OrderBy(personId => personId)];
        return true;
    }

    private bool TryCompilePersonRoles(RelationshipCriteria relationship, out HashSet<string> roles)
    {
        roles = new HashSet<string>(RelationshipKinds.PersonRelationships, StringComparer.OrdinalIgnoreCase);
        foreach (var criterion in relationship.Kind.And.Concat(relationship.Type.And))
        {
            var (op, negate) = CriterionGrammar.InterpretOperator(criterion.Operator, criterion.Negate);
            if (negate || op is not (CriteriaValueOperator.Equals or CriteriaValueOperator.In))
            {
                return false;
            }

            var criterionRoles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var value in CriterionGrammar.TextValues(criterion))
            {
                if (value.Equals(RelationshipKinds.PersonSubject, StringComparison.OrdinalIgnoreCase))
                {
                    criterionRoles.UnionWith(RelationshipKinds.PersonRelationships);
                }
                else if (RelationshipKinds.PersonRelationships.Contains(value, StringComparer.OrdinalIgnoreCase))
                {
                    criterionRoles.Add(value);
                }
                else
                {
                    return false;
                }
            }

            if (criterionRoles.Count == 0)
            {
                return false;
            }

            roles.IntersectWith(criterionRoles);
        }

        return roles.Count > 0;
    }

    private bool ValidateItemCriteria(
        IEnumerable<PropertyCriterion<string>> criteria,
        MetadataField field,
        List<CriterionDisposition> residualDispositions)
    {
        foreach (var criterion in criteria)
        {
            var (op, _) = CriterionGrammar.InterpretOperator(criterion.Operator, criterion.Negate);
            if (!CriterionGrammar.IsTextOperator(op))
            {
                residualDispositions.Add(new CriterionDisposition(
                    field.ToString(),
                    criterion,
                    "pass-through-residual"));
                return false;
            }
        }

        return true;
    }

    private bool RequiresAkas(LocalFilter filter)
    {
        return filter.Kind is LocalFilterKind.OriginalTitle or LocalFilterKind.AlternateText;
    }
}
