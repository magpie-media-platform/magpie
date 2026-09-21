using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Filtering;
using Magpie.Sdk.Content;

namespace Magpie.Extensions.MusicBrainz.QueryEngine;

internal sealed class QueryPlanner : IQueryPlanner
{
    private enum ArtistCompositionParseOutcome
    {
        Success,

        Empty,

        Unsupported
    }

    public QueryPlan Plan(QueryRequest request)
    {
        if (request.IsOrdinary)
        {
            return OrdinaryPlan(request);
        }

        var compilations = Compile(request.Target, request.Criteria).ToList();
        var hasTargetIndexSeed = compilations.Any(compilation => compilation is
        {
            SelectiveSeed: true,
            Filter: { RemoteApplied: true, ReleaseIndexApplied: false }
        });
        var distinctCompositions = compilations
            .Where(compilation => compilation.ArtistComposition is not null)
            .Select(compilation => compilation.ArtistComposition!)
            .Distinct()
            .ToArray();
        var hasIndependentSeed = compilations.Any(compilation =>
            compilation.ArtistComposition is null && compilation.SelectiveSeed);
        var hasSeed = hasIndependentSeed ||
            distinctCompositions.Any(composition => composition.Constraint == ArtistCompositionConstraint.Include);
        var knownEmpty = compilations.Any(compilation => compilation.MatchesNothing);
        var hasRefusedCriteria = compilations.Any(IsRefusedCompilation);
        var filters = new List<QueryFilter>();
        var residuals = new List<ResidualCriterion>();
        var artistCompositions = new List<ArtistComposition>();
        var criterionTraces = new List<CriterionTrace>();
        var filterSources = new List<string>();

        foreach (var compilation in compilations)
        {
            int? filterIndex = null;
            int? residualIndex = null;
            int? artistCompositionIndex = null;
            var refused = false;
            if (compilation.Residual is not null)
            {
                residualIndex = residuals.Count;
                residuals.Add(compilation.Residual);
            }

            if (compilation.RequiresSeed && !hasSeed)
            {
                criterionTraces.Add(new CriterionTrace
                {
                    Source = compilation.Source,
                    ResidualIndex = residualIndex,
                    Refused = true
                });
                continue;
            }

            if (compilation.Filter is not null)
            {
                filterIndex = filters.Count;
                filters.Add(compilation.Filter);
                filterSources.Add(compilation.Source);
            }

            if (compilation.ArtistComposition is not null)
            {
                artistCompositionIndex = artistCompositions.Count;
                artistCompositions.Add(compilation.ArtistComposition);
            }

            if (compilation.Unsupported)
            {
                refused = filterIndex is null &&
                    residualIndex is null &&
                    artistCompositionIndex is null &&
                    !compilation.MatchesNothing;
            }

            criterionTraces.Add(new CriterionTrace
            {
                Source = compilation.Source,
                FilterIndex = filterIndex,
                ResidualIndex = residualIndex,
                ArtistCompositionIndex = artistCompositionIndex,
                PlannedEmpty = compilation.MatchesNothing,
                Refused = refused,
                Vacuous = filterIndex is null &&
                    residualIndex is null &&
                    artistCompositionIndex is null &&
                    !compilation.Unsupported &&
                    !compilation.MatchesNothing
            });
        }

        if (request.Target == QueryTarget.Album && hasTargetIndexSeed)
        {
            filters = [.. filters.Select(filter => filter.ReleaseIndexApplied &&
                filter.Kind != QueryFilterKind.LabelRelationship
                ? filter with { RemoteApplied = false, ReleaseIndexApplied = false }
                : filter)];
        }

        var selection = knownEmpty || hasRefusedCriteria
            ? (Strategy: QueryStrategy.None, JustifyingFilter: null)
            : SelectStrategy(request.Target, filters, artistCompositions.Count > 0);
        var strategy = selection.Strategy;
        if (knownEmpty)
        {
            RecordPlanRefused("known_empty");
        }
        else if (strategy == QueryStrategy.None)
        {
            RecordPlanRefused("unbounded");
        }

        var directValue = selection.JustifyingFilter?.IdentityValues[0].Value;
        if (directValue is not null)
        {
            filters = [.. filters.Select(filter => filter with { RemoteApplied = false, VerifyLocally = true })];
        }

        if (strategy is QueryStrategy.ArtistLookup
            or QueryStrategy.ReleaseGroupLookup
            or QueryStrategy.ReleaseLookup
            or QueryStrategy.RecordingLookup
            or QueryStrategy.IsrcLookup)
        {
            residuals = [.. residuals
                .Select(residual => residual is { Kind: ResidualKind.Genre, Need: ResidualNeed.Hit }
                    ? residual with { Need = ResidualNeed.EntityGenres }
                    : residual)];
        }

        artistCompositions = [.. artistCompositions.Select(composition =>
        {
            var finalStage = composition.Constraint == ArtistCompositionConstraint.Include &&
                !filters.Any(filter => filter.RemoteApplied)
                ? ArtistCompositionFinalStage.ReturnExpandedCandidates
                : ArtistCompositionFinalStage.RunComposedSearch;
            return composition with
            {
                FinalStage = finalStage,
                ExpansionCapabilities = finalStage == ArtistCompositionFinalStage.ReturnExpandedCandidates &&
                    composition.Expansion == ArtistCompositionExpansion.BrowseArtists
                        ? ArtistExpansionCapabilities(residuals)
                        : LookupCapability.None
            };
        })];
        filters = [.. filters.Select((filter, index) => filter with { SourceIndex = index })];
        var fingerprint = Fingerprint(request.Target, filters, residuals, artistCompositions, ordinaryTerm: null);
        var state = new PlanningState
        {
            Target = request.Target,
            Filters = filters,
            Residuals = residuals,
            ArtistCompositions = artistCompositions,
            DirectValue = directValue,
            UsesReleaseIndexComposition = strategy == QueryStrategy.Lucene &&
                filters.Any(filter => filter is { RemoteApplied: true, ReleaseIndexApplied: true })
        };
        var plan = new QueryPlan
        {
            RequestedPage = Math.Max(1, request.RequestedPage),
            MaxResults = Math.Max(1, request.MaxResults),
            Settings = request.Settings,
            Warnings = [],
            Fingerprint = fingerprint
        };
        plan = plan with
        {
            Root = BuildRoot(state, strategy),
            TraceProvenance = BuildTraceProvenance(state, knownEmpty, strategy, criterionTraces)
        };
        RecordPlan(plan);
        return plan;
    }

    private PlanNode BuildRoot(PlanningState state, QueryStrategy strategy)
    {
        var nextId = 1;
        if (strategy == QueryStrategy.None)
        {
            return new UnionPlanNode
            {
                Id = nextId,
                Precondition = NoPrecondition(),
                Contributes = "planned empty result",
                Children = []
            };
        }

        if (IsPureLinkedArtistQuery(state, strategy))
        {
            return BuildLinkedBrowseRoot(state, ref nextId);
        }

        if (strategy == QueryStrategy.Lucene && state.Target == QueryTarget.Artist &&
            state.ArtistCompositions.Count > 0)
        {
            var included = new List<PlanNode>();
            var excluded = new List<PlanNode>();
            foreach (var group in state.ArtistCompositions
                .GroupBy(composition => composition.AlternativeGroup, StringComparer.Ordinal))
            {
                var alternatives = group
                    .Select(composition => (PlanNode)BuildBrowseNode(composition, ref nextId))
                    .ToArray();
                var branch = alternatives.Length == 1
                    ? alternatives[0]
                    : new UnionPlanNode
                    {
                        Id = nextId++,
                        Precondition = NoPrecondition(),
                        Contributes = "artists present in any reverse-relationship alternative",
                        Children = alternatives
                    };
                if (group.First().Constraint == ArtistCompositionConstraint.Include)
                {
                    included.Add(branch);
                }
                else
                {
                    excluded.Add(branch);
                }
            }

            var candidates = included.Count switch
            {
                0 => BuildSearchNode(state, ref nextId),
                1 => included[0],
                _ => new IntersectPlanNode
                {
                    Id = nextId++,
                    Precondition = NoPrecondition(),
                    Contributes = "artists present in every reverse-relationship set",
                    Children = included
                }
            };
            if (included.Count > 0 && state.Filters.Any(filter => filter.RemoteApplied))
            {
                candidates = BuildSearchNode(state, ref nextId) with
                {
                    Precondition = NodeNonEmpty(candidates.Id),
                    AnchorKind = AnchorKind.ReverseRelationship,
                    AnchorSource = candidates
                };
            }

            return BuildFilterNode(state, candidates, excluded, ref nextId);
        }

        var fetch = BuildFetchNode(state, strategy, ref nextId);
        return BuildFilterNode(state, fetch, [], ref nextId);
    }

    private PlanNode BuildFetchNode(PlanningState state, QueryStrategy strategy, ref int nextId)
    {
        var resolvers = new List<ResolvePlanNode>();
        for (var filterIndex = 0; filterIndex < state.Filters.Count; filterIndex++)
        {
            var filter = state.Filters[filterIndex];
            if (filter is not { Kind: QueryFilterKind.ArtistRelationship, Negate: false })
            {
                continue;
            }

            for (var valueIndex = 0; valueIndex < filter.RelationshipValues.Count; valueIndex++)
            {
                var value = filter.RelationshipValues[valueIndex];
                if (RequiresArtistRelationshipResolver(value))
                {
                    resolvers.Add(BuildArtistRelationshipResolver(value, filterIndex, valueIndex, ref nextId));
                }
            }
        }

        var mode = strategy switch
        {
            QueryStrategy.Lucene => AccessMode.Search,
            QueryStrategy.IsrcLookup => AccessMode.IsrcLookup,
            _ => AccessMode.Lookup
        };
        var anchor = strategy switch
        {
            QueryStrategy.IsrcLookup => AnchorKind.Isrc,
            QueryStrategy.ArtistLookup
                or QueryStrategy.ReleaseGroupLookup
                or QueryStrategy.ReleaseLookup
                or QueryStrategy.RecordingLookup => AnchorKind.Mbid,
            QueryStrategy.Lucene when resolvers.Count > 0 => AnchorKind.ResolvedArtist,
            QueryStrategy.Lucene when state.OrdinaryTerm is not null => AnchorKind.UnfieldedText,
            _ => AnchorKind.FieldScoped
        };
        EntityKind? entityKind = strategy switch
        {
            QueryStrategy.ArtistLookup => EntityKind.Artist,
            QueryStrategy.ReleaseGroupLookup => EntityKind.ReleaseGroup,
            QueryStrategy.ReleaseLookup => EntityKind.Release,
            QueryStrategy.RecordingLookup => EntityKind.Recording,
            _ => null
        };
        return new FetchPlanNode
        {
            Id = nextId++,
            Precondition = NoPrecondition(),
            Contributes = "candidate rows",
            Target = state.Target,
            AccessMode = mode,
            AnchorKind = anchor,
            CombineMode = CombineMode.And,
            Filters = [.. state.Filters.Where(filter => filter.RemoteApplied)],
            Resolvers = resolvers,
            DirectValue = state.DirectValue,
            OrdinaryTerm = state.OrdinaryTerm,
            EntityKind = entityKind,
            LookupCapabilities = entityKind is null
                ? LookupCapability.None
                : DirectLookupCapabilities(entityKind.Value, state.Residuals),
            UsesReleaseIndexComposition = state.UsesReleaseIndexComposition
        };
    }

    private bool IsPureLinkedArtistQuery(PlanningState state, QueryStrategy strategy)
    {
        return strategy == QueryStrategy.Lucene &&
            state.Target is QueryTarget.Album or QueryTarget.Track &&
            state.Residuals.Count == 0 &&
            state.Filters.Count > 0 &&
            state.Filters.All(filter => filter is
            {
                Kind: QueryFilterKind.ArtistRelationship,
                RemoteApplied: true,
                Negate: false,
                RelationshipValues.Count: > 0
            });
    }

    private PlanNode BuildLinkedBrowseRoot(PlanningState state, ref int nextId)
    {
        var groups = new List<PlanNode>();
        for (var filterIndex = 0; filterIndex < state.Filters.Count; filterIndex++)
        {
            var filter = state.Filters[filterIndex];
            var alternatives = new List<PlanNode>();
            for (var valueIndex = 0; valueIndex < filter.RelationshipValues.Count; valueIndex++)
            {
                var value = filter.RelationshipValues[valueIndex];
                ResolvePlanNode? resolver = null;
                if (RequiresArtistRelationshipResolver(value))
                {
                    resolver = BuildArtistRelationshipResolver(value, filterIndex, valueIndex, ref nextId);
                }

                alternatives.Add(new FetchPlanNode
                {
                    Id = nextId++,
                    Precondition = resolver is null ? NoPrecondition() : NodeNonEmpty(resolver.Id),
                    Contributes = "content linked to one artist",
                    Target = state.Target,
                    AccessMode = AccessMode.Browse,
                    AnchorKind = resolver is null ? AnchorKind.Mbid : AnchorKind.ResolvedArtist,
                    CombineMode = CombineMode.And,
                    Filters = [filter],
                    Resolvers = resolver is null ? [] : [resolver],
                    DirectValue = resolver is null ? value.ArtistMbid : null
                });
            }

            groups.Add(alternatives.Count == 1
                ? alternatives[0]
                : new UnionPlanNode
                {
                    Id = nextId++,
                    Precondition = NoPrecondition(),
                    Contributes = "content linked to any artist alternative",
                    Children = alternatives
                });
        }

        var candidates = groups.Count == 1
            ? groups[0]
            : new IntersectPlanNode
            {
                Id = nextId++,
                Precondition = NoPrecondition(),
                Contributes = "content linked to every artist criterion",
                Children = groups
            };
        return BuildFilterNode(state, candidates, [], ref nextId);
    }

    private FetchPlanNode BuildSearchNode(PlanningState state, ref int nextId)
    {
        return new()
        {
            Id = nextId++,
            Precondition = NoPrecondition(),
            Contributes = "candidate rows",
            Target = state.Target,
            AccessMode = AccessMode.Search,
            AnchorKind = state.OrdinaryTerm is not null ? AnchorKind.UnfieldedText : AnchorKind.FieldScoped,
            CombineMode = CombineMode.And,
            Filters = [.. state.Filters.Where(filter => filter.RemoteApplied)],
            OrdinaryTerm = state.OrdinaryTerm,
            UsesReleaseIndexComposition = state.UsesReleaseIndexComposition
        };
    }

    private FetchPlanNode BuildBrowseNode(ArtistComposition composition, ref int nextId)
    {
        var resolver = BuildResolveNode(composition, ref nextId);
        return new FetchPlanNode
        {
            Id = nextId++,
            Precondition = NodeNonEmpty(resolver.Id),
            Contributes = "reverse-relationship artist rows",
            Target = QueryTarget.Artist,
            AccessMode = AccessMode.Browse,
            AnchorKind = AnchorKind.ReverseRelationship,
            CombineMode = CombineMode.And,
            Filters = [],
            Resolvers = [resolver],
            BrowseComposition = composition
        };
    }

    private ResolvePlanNode BuildResolveNode(ArtistComposition composition, ref int nextId)
    {
        return new()
        {
            Id = nextId++,
            Precondition = NoPrecondition(),
            Contributes = "resolved artist ids",
            Composition = composition
        };
    }

    private bool RequiresArtistRelationshipResolver(ArtistRelationshipValue relationship)
    {
        return relationship.ArtistMbid is null && relationship.ArtistName is not null ||
            relationship.ArtistMbid is not null && relationship.ArtistType is not null;
    }

    private ResolvePlanNode BuildArtistRelationshipResolver(
        ArtistRelationshipValue relationship,
        int filterIndex,
        int valueIndex,
        ref int nextId)
    {
        return new()
        {
            Id = nextId++,
            Precondition = NoPrecondition(),
            Contributes = "resolved artist ids",
            Composition = new ArtistComposition
            {
                RelatedEntity = ArtistCompositionEntity.Artist,
                Resolution = relationship.ArtistMbid is not null && relationship.ArtistType is not null
                    ? ArtistCompositionResolution.TypedMbidLookup
                    : ArtistCompositionResolution.ExactNameSearch,
                Expansion = ArtistCompositionExpansion.BrowseArtists,
                Constraint = ArtistCompositionConstraint.Include,
                FinalStage = ArtistCompositionFinalStage.RunComposedSearch,
                ExternalIdSource = Constants.ProviderType,
                RelatedMbid = relationship.ArtistMbid,
                RelatedName = relationship.ArtistName,
                RelatedNameOperator = relationship.ArtistNameOperator,
                RelatedArtistType = relationship.ArtistType
            },
            RelationshipFilterIndex = filterIndex,
            RelationshipValueIndex = valueIndex
        };
    }

    private PlanNode BuildFilterNode(
        PlanningState state,
        PlanNode child,
        IReadOnlyList<PlanNode> exclusions,
        ref int nextId)
    {
        var filters = state.Filters.Where(filter => filter.VerifyLocally).ToList();
        var needsLocalPaging = child is FetchPlanNode { AccessMode: AccessMode.Browse }
            or UnionPlanNode
            or IntersectPlanNode;
        if (filters.Count == 0 && state.Residuals.Count == 0 && exclusions.Count == 0 && !needsLocalPaging)
        {
            return child;
        }

        return new FilterPlanNode
        {
            Id = nextId++,
            Precondition = NodeNonEmpty(child.Id),
            Contributes = "post-residual survivor rows",
            Child = child,
            Filters = filters,
            Residuals = state.Residuals,
            Exclusions = exclusions
        };
    }

    private PlanPrecondition NoPrecondition() => new() { Kind = PlanPreconditionKind.None };

    private PlanPrecondition NodeNonEmpty(int nodeId)
    {
        return new()
        {
            Kind = PlanPreconditionKind.NodeNonEmpty,
            NodeId = nodeId
        };
    }

    private LookupCapability ArtistExpansionCapabilities(IReadOnlyList<ResidualCriterion> residuals)
    {
        var capabilities = LookupCapability.None;
        foreach (var residual in residuals)
        {
            capabilities |= residual.Need switch
            {
                ResidualNeed.EntityAliases => LookupCapability.Aliases,
                ResidualNeed.EntityUrlRelations => LookupCapability.UrlRelations,
                ResidualNeed.EntityGenres => LookupCapability.Genres,
                ResidualNeed.Hit when residual.Kind == ResidualKind.Genre =>
                    LookupCapability.Genres,
                _ => LookupCapability.None
            };
        }

        return capabilities;
    }

    private LookupCapability DirectLookupCapabilities(
        EntityKind kind,
        IReadOnlyList<ResidualCriterion> residuals)
    {
        var capabilities = LookupCapability.None;
        if (kind != EntityKind.Release && residuals.Any(residual => residual.Need == ResidualNeed.EntityAliases))
        {
            capabilities |= LookupCapability.Aliases;
        }

        if (kind != EntityKind.Release && residuals.Any(residual => residual.Need == ResidualNeed.EntityUrlRelations))
        {
            capabilities |= LookupCapability.UrlRelations;
        }

        if (kind != EntityKind.Release && residuals.Any(residual => residual.Need == ResidualNeed.EntityGenres))
        {
            capabilities |= LookupCapability.Genres;
        }

        if (residuals.Any(residual => residual.Need == ResidualNeed.RepresentativeReleaseLabels))
        {
            capabilities |= kind == EntityKind.Release ? LookupCapability.Labels : LookupCapability.Releases;
        }

        if (residuals.Any(residual => residual.Need == ResidualNeed.RepresentativeReleaseLanguage))
        {
            capabilities |= LookupCapability.Releases;
        }

        if (residuals.Any(residual => residual.Need == ResidualNeed.RepresentativeReleaseDuration))
        {
            capabilities |= kind == EntityKind.Release
                ? LookupCapability.ReleaseMedia
                : LookupCapability.Releases | LookupCapability.ReleaseMediaSummary;
        }

        if (residuals.Any(residual => residual.Need == ResidualNeed.EntityExternalIds))
        {
            capabilities |= LookupCapability.ExternalIds;
        }

        return capabilities;
    }

    private IReadOnlyList<Compilation> Compile(QueryTarget target, MetadataCriteria criteria)
    {
        var compilations = new List<Compilation>();
        AddStrings(target, criteria.ProviderType, null, nameof(criteria.ProviderType), compilations);
        AddStrings(target, criteria.Source, null, nameof(criteria.Source), compilations);
        AddStrings(target, criteria.Title, MetadataField.Title, nameof(criteria.Title), compilations);
        AddOriginalTitles(target, criteria.OriginalTitle, nameof(criteria.OriginalTitle), compilations);
        if (SupportsLanguage(target))
        {
            var languageIndex = 0;
            foreach (var criterion in criteria.Language.And)
            {
                compilations.Add(AtSource(
                    CompileLanguage(target, criterion),
                    CriterionSource(nameof(criteria.Language), "And", languageIndex)));
                languageIndex++;
            }
        }
        else
        {
            AddAbsentScalar(criteria.Language, nameof(criteria.Language), compilations);
        }

        AddStrings(target, criteria.Overview, MetadataField.Overview, nameof(criteria.Overview), compilations);
        if (SupportsPublishDate(target))
        {
            AddTemporal(
                target,
                criteria.PublishDate,
                MetadataField.PublishDate,
                nameof(criteria.PublishDate),
                compilations);
        }
        else
        {
            AddAbsentScalar(criteria.PublishDate, nameof(criteria.PublishDate), compilations);
        }

        if (SupportsDuration(target))
        {
            var durationIndex = 0;
            foreach (var criterion in criteria.Duration.And)
            {
                compilations.Add(AtSource(
                    CompileDuration(target, MetadataField.Duration, criterion),
                    CriterionSource(nameof(criteria.Duration), "And", durationIndex)));
                durationIndex++;
            }
        }
        else
        {
            AddAbsentScalar(criteria.Duration, nameof(criteria.Duration), compilations);
        }

        AddAbsentScalar(criteria.ProductionStatus, nameof(criteria.ProductionStatus), compilations);
        AddTemporal(
            target,
            criteria.Established,
            MetadataField.Established,
            nameof(criteria.Established),
            compilations);
        AddTemporal(target, criteria.Dissolved, MetadataField.Dissolved, nameof(criteria.Dissolved), compilations);
        AddTemporal(target, criteria.BirthDate, MetadataField.BirthDate, nameof(criteria.BirthDate), compilations);
        AddTemporal(target, criteria.DeathDate, MetadataField.DeathDate, nameof(criteria.DeathDate), compilations);
        AddCustomMetadata(criteria.CustomMetadata, nameof(criteria.CustomMetadata), compilations);
        AddAbsentCollection(criteria.Awards, nameof(criteria.Awards), compilations);
        for (var index = 0; index < criteria.ExternalIds.Some.Count; index++)
        {
            var group = criteria.ExternalIds.Some[index];
            if (group.Count > 0)
            {
                compilations.Add(AtSource(
                    CompileExternalIds(target, group, negate: false),
                    CriterionSource(nameof(criteria.ExternalIds), "Some", index)));
            }
            else
            {
                compilations.Add(AtSource(
                    new Compilation(),
                    CriterionSource(nameof(criteria.ExternalIds), "Some", index)));
            }
        }

        for (var index = 0; index < criteria.ExternalIds.None.Count; index++)
        {
            compilations.Add(AtSource(
                CompileExternalIds(target, [criteria.ExternalIds.None[index]], negate: true),
                CriterionSource(nameof(criteria.ExternalIds), "None", index)));
        }

        for (var index = 0; index < criteria.References.Some.Count; index++)
        {
            var group = criteria.References.Some[index];
            if (group.Count > 0)
            {
                compilations.Add(AtSource(
                    CompileReferences(group, negate: false),
                    CriterionSource(nameof(criteria.References), "Some", index)));
            }
            else
            {
                compilations.Add(AtSource(
                    new Compilation(),
                    CriterionSource(nameof(criteria.References), "Some", index)));
            }
        }

        for (var index = 0; index < criteria.References.None.Count; index++)
        {
            compilations.Add(AtSource(
                CompileReferences([criteria.References.None[index]], negate: true),
                CriterionSource(nameof(criteria.References), "None", index)));
        }

        for (var index = 0; index < criteria.Relationships.Some.Count; index++)
        {
            var group = criteria.Relationships.Some[index];
            if (group.Count > 0)
            {
                var source = CriterionSource(nameof(criteria.Relationships), "Some", index);
                if (target == QueryTarget.Artist)
                {
                    var alternatives = group.Select(alternative =>
                        CompileRelationships(target, [alternative], negate: false))
                        .ToArray();
                    var refused = alternatives.FirstOrDefault(IsRefusedCompilation);
                    if (refused is not null)
                    {
                        compilations.Add(AtSource(refused, source));
                    }
                    else
                    {
                        var viable = alternatives.Where(alternative => !alternative.MatchesNothing).ToArray();
                        compilations.AddRange(
                            (viable.Length == 0 ? [KnownEmptyCompilation()] : viable)
                                .Select(alternative => AtSource(alternative, source)));
                    }
                }
                else
                {
                    compilations.Add(AtSource(CompileRelationships(target, group, negate: false), source));
                }
            }
            else
            {
                compilations.Add(AtSource(
                    new Compilation(),
                    CriterionSource(nameof(criteria.Relationships), "Some", index)));
            }
        }

        for (var index = 0; index < criteria.Relationships.None.Count; index++)
        {
            compilations.Add(AtSource(
                CompileRelationships(target, [criteria.Relationships.None[index]], negate: true),
                CriterionSource(nameof(criteria.Relationships), "None", index)));
        }

        AddImages(target, criteria.Images, nameof(criteria.Images), compilations);
        AddAbsentCollection(criteria.Previews, nameof(criteria.Previews), compilations);
        for (var index = 0; index < criteria.AlternateTexts.Some.Count; index++)
        {
            var group = criteria.AlternateTexts.Some[index];
            if (group.Count > 0)
            {
                compilations.Add(AtSource(
                    CompileAlternateTexts(group, negate: false),
                    CriterionSource(nameof(criteria.AlternateTexts), "Some", index)));
            }
            else
            {
                compilations.Add(AtSource(
                    new Compilation(),
                    CriterionSource(nameof(criteria.AlternateTexts), "Some", index)));
            }
        }

        for (var index = 0; index < criteria.AlternateTexts.None.Count; index++)
        {
            compilations.Add(AtSource(
                CompileAlternateTexts([criteria.AlternateTexts.None[index]], negate: true),
                CriterionSource(nameof(criteria.AlternateTexts), "None", index)));
        }

        AddAbsentCollection(criteria.ContentRatings, nameof(criteria.ContentRatings), compilations);
        for (var index = 0; index < criteria.Genres.Some.Count; index++)
        {
            var group = criteria.Genres.Some[index];
            if (group.Count > 0)
            {
                compilations.Add(AtSource(
                    CompileGenres(target, group, negate: false),
                    CriterionSource(nameof(criteria.Genres), "Some", index)));
            }
            else
            {
                compilations.Add(AtSource(new Compilation(), CriterionSource(nameof(criteria.Genres), "Some", index)));
            }
        }

        for (var index = 0; index < criteria.Genres.None.Count; index++)
        {
            compilations.Add(AtSource(
                CompileGenres(target, [criteria.Genres.None[index]], negate: true),
                CriterionSource(nameof(criteria.Genres), "None", index)));
        }

        AddAbsentCollection(criteria.Tags, nameof(criteria.Tags), compilations);
        return compilations;
    }

    private void AddStrings(
        QueryTarget target,
        CriteriaSet<PropertyCriterion<string>> criteria,
        MetadataField? field,
        string source,
        List<Compilation> compilations)
    {
        var index = 0;
        foreach (var criterion in criteria.And)
        {
            compilations.Add(AtSource(CompileString(target, field, criterion), CriterionSource(source, "And", index)));
            index++;
        }
    }

    private void AddOriginalTitles(
        QueryTarget target,
        CriteriaSet<PropertyCriterion<string>> criteria,
        string source,
        List<Compilation> compilations)
    {
        var index = 0;
        foreach (var criterion in criteria.And)
        {
            compilations.Add(AtSource(CompileOriginalTitle(target, criterion), CriterionSource(source, "And", index)));
            index++;
        }
    }

    private Compilation CompileOriginalTitle(QueryTarget target, PropertyCriterion<string> criterion)
    {
        var (op, negate) = InterpretOperator(criterion.Operator, criterion.Negate);
        if (!IsStringOperator(op))
        {
            return UnsupportedCompilation();
        }

        if (op is CriteriaValueOperator.IsNull or CriteriaValueOperator.IsNotNull)
        {
            var matches = op == CriteriaValueOperator.IsNotNull;
            return matches != negate ? new Compilation() : KnownEmptyCompilation();
        }

        var compilation = CompileAlternateTexts(
            [new AlternateTextCriteria
            {
                Text = new CriteriaSet<PropertyCriterion<string>>
                { And = [criterion with { Operator = op, Negate = false }] }
            }                                    ],
            negate);
        return compilation with
        {
            Filter = compilation.Filter is null
                ? null
                : compilation.Filter with
                {
                    AlternateRemoteField = StringField(target, MetadataField.Title),
                    IncludePrimaryTitle = true
                },
            Residual = compilation.Residual is null ? null : compilation.Residual with { IncludePrimaryTitle = true }
        };
    }

    private void AddTemporal(
        QueryTarget target,
        CriteriaSet<PropertyCriterion<TemporalValue>> criteria,
        MetadataField field,
        string source,
        List<Compilation> compilations)
    {
        var index = 0;
        foreach (var criterion in criteria.And)
        {
            compilations.Add(AtSource(
                CompileTemporal(target, field, criterion),
                CriterionSource(source, "And", index)));
            index++;
        }
    }

    private void AddCustomMetadata(
        CriteriaSet<DictionaryCriteria<PropertyCriterion<string>>> criteria,
        string source,
        List<Compilation> compilations)
    {
        var dictionaryIndex = 0;
        foreach (var dictionary in criteria.And)
        {
            if (dictionary.Items.Count == 0)
            {
                compilations.Add(AtSource(new Compilation(), CriterionSource(source, "And", dictionaryIndex)));
            }

            foreach (var item in dictionary.Items.OrderBy(item => item.Key, StringComparer.Ordinal))
            {
                var criterionSource = string.Concat(
                    CriterionSource(source, "And", dictionaryIndex),
                    ".Items[",
                    item.Key,
                    "]");
                var criterion = item.Value;
                var (op, negate) = InterpretOperator(criterion.Operator, criterion.Negate);
                var matchesAbsentValue = op == CriteriaValueOperator.IsNull != negate;
                compilations.Add(AtSource(
                    matchesAbsentValue ? new Compilation() : KnownEmptyCompilation(),
                    criterionSource));
            }

            dictionaryIndex++;
        }
    }

    private void AddAbsentCollection<T>(
        CollectionCriteria<T> criteria,
        string source,
        List<Compilation> compilations)
    {
        for (var index = 0; index < criteria.Some.Count; index++)
        {
            var group = criteria.Some[index];
            compilations.Add(AtSource(
                group.Count == 0 ? new Compilation() : KnownEmptyCompilation(),
                CriterionSource(source, "Some", index)));
        }

        for (var index = 0; index < criteria.None.Count; index++)
        {
            compilations.Add(AtSource(new Compilation(), CriterionSource(source, "None", index)));
        }
    }

    private void AddAbsentScalar<T>(
        CriteriaSet<PropertyCriterion<T>> criteria,
        string source,
        List<Compilation> compilations)
    {
        var index = 0;
        foreach (var criterion in criteria.And)
        {
            compilations.Add(AtSource(
                MatchesAbsent([(criterion.Operator, criterion.Negate)]) ? new Compilation() : KnownEmptyCompilation(),
                CriterionSource(source, "And", index)));
            index++;
        }
    }

    private bool MatchesAbsent(IEnumerable<(CriteriaValueOperator Operator, bool Negate)> criteria)
    {
        var hasCriterion = false;
        foreach (var criterion in criteria)
        {
            hasCriterion = true;
            var (op, negate) = InterpretOperator(criterion.Operator, criterion.Negate);
            if (op == CriteriaValueOperator.IsNull == negate)
            {
                return false;
            }
        }

        return hasCriterion;
    }

    private Compilation CompileReferences(IReadOnlyList<ReferenceMetadataCriteria> alternatives, bool negate)
    {
        var residualAlternatives = new List<ResidualAlternative>();
        foreach (var alternative in alternatives)
        {
            if (alternative.Ordinal.And.Any() || alternative.DisplayName.And.Any() || alternative.Language.And.Any())
            {
                return UnsupportedCompilation();
            }

            var predicates = new List<ResidualPredicate>();
            if (!TryCompileStringPredicates(alternative.Kind.And, ResidualField.ReferenceKind, predicates))
            {
                return UnsupportedCompilation();
            }

            foreach (var criterion in alternative.Url.And)
            {
                var (op, effectiveNegate) = InterpretOperator(criterion.Operator, criterion.Negate);
                var values = UriValues(criterion);
                if (!IsStringOperator(op) ||
                    op is not (CriteriaValueOperator.IsNull or CriteriaValueOperator.IsNotNull) && values.Length == 0)
                {
                    return UnsupportedCompilation();
                }

                predicates.Add(new ResidualPredicate
                {
                    Field = ResidualField.ReferenceUrl,
                    Operator = op,
                    Negate = effectiveNegate,
                    Values = values
                });
            }

            residualAlternatives.Add(new ResidualAlternative { Predicates = predicates });
        }

        if (residualAlternatives.Count == 0)
        {
            return negate ? new Compilation() : KnownEmptyCompilation();
        }

        return ResidualCompilation(
            ResidualKind.Reference,
            ResidualNeed.EntityUrlRelations,
            residualAlternatives,
            negate);
    }

    private void AddImages(
        QueryTarget target,
        CollectionCriteria<ImageCriteria> criteria,
        string source,
        List<Compilation> compilations)
    {
        if (target != QueryTarget.Album)
        {
            for (var index = 0; index < criteria.Some.Count; index++)
            {
                if (criteria.Some[index].Count > 0)
                {
                    compilations.Add(AtSource(KnownEmptyCompilation(), CriterionSource(source, "Some", index)));
                }
                else
                {
                    compilations.Add(AtSource(new Compilation(), CriterionSource(source, "Some", index)));
                }
            }

            for (var index = 0; index < criteria.None.Count; index++)
            {
                compilations.Add(AtSource(new Compilation(), CriterionSource(source, "None", index)));
            }

            return;
        }

        for (var index = 0; index < criteria.Some.Count; index++)
        {
            var group = criteria.Some[index];
            if (group.Count > 0)
            {
                compilations.Add(AtSource(
                    CompileImages(group, negate: false),
                    CriterionSource(source, "Some", index)));
            }
            else
            {
                compilations.Add(AtSource(new Compilation(), CriterionSource(source, "Some", index)));
            }
        }

        for (var index = 0; index < criteria.None.Count; index++)
        {
            compilations.Add(AtSource(
                CompileImages([criteria.None[index]], negate: true),
                CriterionSource(source, "None", index)));
        }
    }

    private Compilation CompileImages(IReadOnlyList<ImageCriteria> alternatives, bool negate)
    {
        var residualAlternatives = new List<ResidualAlternative>();
        foreach (var alternative in alternatives)
        {
            if (!TryCompileConceptualImage(alternative, out var matches, out var predicates))
            {
                return UnsupportedCompilation();
            }

            if (matches)
            {
                residualAlternatives.Add(new ResidualAlternative
                {
                    Predicates =
                    [
                        new ResidualPredicate
                        {
                            Field = ResidualField.ImagePresence,
                            Operator = CriteriaValueOperator.IsNotNull
                        },
                        .. predicates
                    ]
                });
            }
        }

        if (residualAlternatives.Count == 0)
        {
            return negate ? new Compilation() : KnownEmptyCompilation();
        }

        return ResidualCompilation(ResidualKind.ImagePresence, ResidualNeed.CoverArt, residualAlternatives, negate);
    }

    private bool TryCompileConceptualImage(
        ImageCriteria criteria,
        out bool matches,
        out IReadOnlyList<ResidualPredicate> predicates)
    {
        matches = true;
        var urlPredicates = new List<ResidualPredicate>();
        predicates = urlPredicates;
        return TryMatchKnownImageStringField(criteria.Type.And, Constants.CoverImageType, ref matches) &&
            TryMatchKnownImageStringField(criteria.MimeType.And, null, ref matches) &&
            TryCompileImageUrl(criteria.Url.And, urlPredicates, ref matches) &&
            TryMatchKnownImageStringField(criteria.Source.And, Constants.ProviderType, ref matches) &&
            TryMatchKnownImageStringField(criteria.Language.And, null, ref matches) &&
            TryMatchKnownImageIntegerField(criteria.Width.And, ref matches) &&
            TryMatchKnownImageIntegerField(criteria.Height.And, ref matches);
    }

    private bool TryMatchKnownImageStringField(
        IEnumerable<PropertyCriterion<string>> criteria,
        string? candidate,
        ref bool matches)
    {
        foreach (var criterion in criteria)
        {
            var (op, negate) = InterpretOperator(criterion.Operator, criterion.Negate);
            var values = TextValues(criterion);
            if (!IsStringOperator(op) ||
                op is not (CriteriaValueOperator.IsNull or CriteriaValueOperator.IsNotNull) && values.Length == 0)
            {
                return false;
            }

            var criterionMatches = op.MatchesString(values, candidate);
            matches &= negate ? !criterionMatches : criterionMatches;
        }

        return true;
    }

    private bool TryCompileImageUrl(
        IEnumerable<PropertyCriterion<string>> criteria,
        List<ResidualPredicate> predicates,
        ref bool matches)
    {
        foreach (var criterion in criteria)
        {
            var (op, negate) = InterpretOperator(criterion.Operator, criterion.Negate);
            var values = TextValues(criterion);
            if (!IsStringOperator(op) ||
                op is not (CriteriaValueOperator.IsNull or CriteriaValueOperator.IsNotNull) && values.Length == 0)
            {
                return false;
            }

            if (op == CriteriaValueOperator.IsNull)
            {
                matches &= negate;
                continue;
            }

            if (op == CriteriaValueOperator.IsNotNull)
            {
                matches &= !negate;
                continue;
            }

            predicates.Add(new ResidualPredicate
            {
                Field = ResidualField.ImageUrl,
                Operator = op,
                Negate = negate,
                Values = values
            });
        }

        return true;
    }

    private bool TryMatchKnownImageIntegerField(IEnumerable<PropertyCriterion<int>> criteria, ref bool matches)
    {
        foreach (var criterion in criteria)
        {
            var (op, negate) = InterpretOperator(criterion.Operator, criterion.Negate);
            if (!IsOrderedOperator(op))
            {
                return false;
            }

            var criterionMatches = op == CriteriaValueOperator.IsNull;
            matches &= negate ? !criterionMatches : criterionMatches;
        }

        return true;
    }

    private bool TryCompileStringPredicates(
        IEnumerable<PropertyCriterion<string>> criteria,
        ResidualField field,
        List<ResidualPredicate> predicates)
    {
        foreach (var criterion in criteria)
        {
            var (op, negate) = InterpretOperator(criterion.Operator, criterion.Negate);
            var values = TextValues(criterion);
            if (!IsStringOperator(op) ||
                op is not (CriteriaValueOperator.IsNull or CriteriaValueOperator.IsNotNull) && values.Length == 0)
            {
                return false;
            }

            predicates.Add(new ResidualPredicate { Field = field, Operator = op, Negate = negate, Values = values });
        }

        return true;
    }

    private Compilation CompileString(
        QueryTarget target,
        MetadataField? field,
        PropertyCriterion<string> criterion)
    {
        var remoteField = StringField(target, field);
        var (op, negate) = InterpretOperator(criterion.Operator, criterion.Negate);
        var locallyAvailable = target == QueryTarget.Track && field == MetadataField.Overview;
        if ((!locallyAvailable && remoteField is null) || !IsStringOperator(op))
        {
            return UnsupportedCompilation();
        }

        var values = TextValues(criterion);
        var localOnlyOperator = op is CriteriaValueOperator.ContainsString
                or CriteriaValueOperator.StartsWithString
                or CriteriaValueOperator.EndsWithString &&
            values.Any(value => value.SearchFragments().Count == 0);
        var localOnly = remoteField is null || negate || localOnlyOperator;
        var hasValue = op is not CriteriaValueOperator.IsNull and not CriteriaValueOperator.IsNotNull;
        if (hasValue && values.Length == 0)
        {
            return UnsupportedCompilation();
        }

        return new Compilation
        {
            Filter = new QueryFilter
            {
                Kind = StringKind(field),
                Operator = op,
                Negate = negate,
                RemoteField = remoteField,
                StringValues = values,
                RemoteApplied = !localOnly,
                VerifyLocally = localOnly ||
                    op is CriteriaValueOperator.Equals
                        or CriteriaValueOperator.In
                        or CriteriaValueOperator.ContainsWord
                        or CriteriaValueOperator.ContainsString
                        or CriteriaValueOperator.StartsWithString
                        or CriteriaValueOperator.EndsWithString
            },
            SelectiveSeed = !negate && hasValue && !localOnly,
            RequiresSeed = localOnly
        };
    }

    private Compilation CompileLanguage(QueryTarget target, PropertyCriterion<string> criterion)
    {
        var (op, negate) = InterpretOperator(criterion.Operator, criterion.Negate);
        var suppliedValues = TextValues(criterion);
        if (!SupportsLanguage(target))
        {
            return UnsupportedCompilation();
        }

        if (!IsStringOperator(op) ||
            op is not (CriteriaValueOperator.IsNull or CriteriaValueOperator.IsNotNull) && suppliedValues.Length == 0)
        {
            return UnsupportedCompilation();
        }

        var exactOperator = op is CriteriaValueOperator.Equals or CriteriaValueOperator.In;
        var values = suppliedValues.Select(value => value.NormalizeThreeLetterLanguageCode()
            ?? (exactOperator ? null : value.Trim().ToLowerInvariant()))
            .Where(value => value is not null)
            .Select(value => value!)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (exactOperator && values.Length == 0)
        {
            return negate ? new Compilation() : KnownEmptyCompilation();
        }

        var remoteApplied = !negate && exactOperator;
        return new Compilation
        {
            Filter = remoteApplied
                ? new QueryFilter
                {
                    Kind = QueryFilterKind.Language,
                    Operator = values.Length == 1 ? CriteriaValueOperator.Equals : CriteriaValueOperator.In,
                    Negate = false,
                    RemoteField = "lang",
                    StringValues = values,
                    RemoteApplied = true,
                    ReleaseIndexApplied = true,
                    VerifyLocally = true
                }
                : null,
            Residual = new ResidualCriterion
            {
                Kind = ResidualKind.Language,
                Need = ResidualNeed.RepresentativeReleaseLanguage,
                Alternatives =
                [
                    new ResidualAlternative
                    {
                        Predicates =
                        [
                            new ResidualPredicate
                            {
                                Field = ResidualField.Language,
                                Operator = op,
                                Negate = negate,
                                Values = values
                            }
                        ]
                    }
                ]
            },
            SelectiveSeed = remoteApplied,
            RequiresSeed = !remoteApplied
        };
    }

    private Compilation CompileAlternateTexts(IReadOnlyList<AlternateTextCriteria> alternatives, bool negate)
    {
        if (alternatives.Count == 0)
        {
            return negate ? new Compilation() : KnownEmptyCompilation();
        }

        var residualAlternatives = new List<ResidualAlternative>();
        foreach (var alternative in alternatives)
        {
            var predicates = new List<ResidualPredicate>();
            if (!TryCompileStringPredicates(alternative.Kind.And, ResidualField.AlternateTextKind, predicates) ||
                !TryCompileStringPredicates(
                    alternative.Language.And,
                    ResidualField.AlternateTextLanguage,
                    predicates) ||
                !TryCompileStringPredicates(alternative.Text.And, ResidualField.AlternateTextText, predicates))
            {
                return UnsupportedCompilation();
            }

            residualAlternatives.Add(new ResidualAlternative { Predicates = predicates });
        }

        var textPredicates = residualAlternatives
            .Select(alternative => alternative.Predicates
                .Where(predicate => predicate.Field == ResidualField.AlternateTextText)
                .ToArray())
            .ToArray();
        var canSeed = !negate &&
            residualAlternatives.All(alternative => alternative.Predicates.Count == 1) &&
            textPredicates.All(predicates => predicates.Length == 1 && !predicates[0].Negate) &&
            (textPredicates.All(predicates => predicates[0].Operator is
                CriteriaValueOperator.Equals or CriteriaValueOperator.In) ||
                textPredicates.All(predicates => predicates[0].Operator == CriteriaValueOperator.ContainsWord));
        var remoteValues = canSeed
            ? textPredicates
                .SelectMany(predicates => predicates[0].Values)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray()
            : [];
        if (canSeed && remoteValues.Length == 0)
        {
            return UnsupportedCompilation();
        }

        return new Compilation
        {
            Filter = canSeed
                ? new QueryFilter
                {
                    Kind = QueryFilterKind.AlternateText,
                    Operator = textPredicates.All(predicates => predicates[0].Operator ==
                        CriteriaValueOperator.ContainsWord)
                            ? CriteriaValueOperator.ContainsWord
                            : remoteValues.Length == 1 ? CriteriaValueOperator.Equals : CriteriaValueOperator.In,
                    Negate = false,
                    RemoteField = "alias",
                    StringValues = remoteValues,
                    RemoteApplied = true
                }
                : null,
            Residual = new ResidualCriterion
            {
                Kind = ResidualKind.AlternateText,
                Need = ResidualNeed.EntityAliases,
                Negate = negate,
                Alternatives = residualAlternatives
            },
            SelectiveSeed = canSeed,
            RequiresSeed = !canSeed
        };
    }

    private Compilation CompileTemporal(
        QueryTarget target,
        MetadataField? field,
        PropertyCriterion<TemporalValue> criterion)
    {
        var remoteField = TemporalField(target, field);
        var (op, negate) = InterpretOperator(criterion.Operator, criterion.Negate);
        var suppliedValues = TemporalValues(criterion);
        var values = suppliedValues.Where(value => value.Precision != TemporalPrecision.Instant).ToArray();
        var nullOperator = op is CriteriaValueOperator.IsNull or CriteriaValueOperator.IsNotNull;
        if (remoteField is null || !IsOrderedOperator(op) || (!nullOperator && values.Length == 0))
        {
            return UnsupportedCompilation();
        }

        var ordered = op is CriteriaValueOperator.GreaterThan
            or CriteriaValueOperator.GreaterThanOrEqual
            or CriteriaValueOperator.LessThan
            or CriteriaValueOperator.LessThanOrEqual;
        var localOnly = negate && ordered;
        return new Compilation
        {
            Filter = new QueryFilter
            {
                Kind = TemporalKind(field),
                Operator = op,
                Negate = negate,
                RemoteField = remoteField,
                TemporalValues = values,
                RemoteApplied = !localOnly,
                VerifyLocally = negate || ordered
            },
            SelectiveSeed = !negate && !nullOperator,
            RequiresSeed = localOnly
        };
    }

    private Compilation CompileDuration(
        QueryTarget target,
        MetadataField? field,
        PropertyCriterion<TimeSpan> criterion)
    {
        var (op, negate) = InterpretOperator(criterion.Operator, criterion.Negate);
        var nullOperator = op is CriteriaValueOperator.IsNull or CriteriaValueOperator.IsNotNull;
        var ordered = op is CriteriaValueOperator.GreaterThan
            or CriteriaValueOperator.GreaterThanOrEqual
            or CriteriaValueOperator.LessThan
            or CriteriaValueOperator.LessThanOrEqual;
        var rawValues = DurationValues(criterion);
        var values = rawValues
            .Where(value => ordered || value > TimeSpan.Zero)
            .Select(value => Convert.ToInt64(Math.Round(value.TotalMilliseconds, MidpointRounding.AwayFromZero)))
            .ToArray();
        if (field != MetadataField.Duration || !IsOrderedOperator(op) || (!nullOperator && values.Length == 0))
        {
            return UnsupportedCompilation();
        }

        if (!negate && ordered && DurationBoundaryMatchesNothing(op, rawValues[0]))
        {
            return KnownEmptyCompilation();
        }

        if (target == QueryTarget.Album)
        {
            return new Compilation
            {
                Residual = new ResidualCriterion
                {
                    Kind = ResidualKind.Duration,
                    Need = ResidualNeed.RepresentativeReleaseDuration,
                    Alternatives =
                    [
                        new ResidualAlternative
                        {
                            Predicates =
                            [
                                new ResidualPredicate
                                {
                                    Field = ResidualField.Duration,
                                    Operator = op,
                                    Negate = negate,
                                    DurationMilliseconds = values
                                }
                            ]
                        }
                    ]
                },
                RequiresSeed = true
            };
        }

        if (target != QueryTarget.Track)
        {
            return UnsupportedCompilation();
        }

        var localOnly = negate;
        var filterOperator = op;
        if (!localOnly && ordered && rawValues[0] < TimeSpan.Zero &&
            op is CriteriaValueOperator.GreaterThan or CriteriaValueOperator.GreaterThanOrEqual)
        {
            filterOperator = CriteriaValueOperator.GreaterThanOrEqual;
            values = [0];
        }

        return new Compilation
        {
            Filter = new QueryFilter
            {
                Kind = QueryFilterKind.Duration,
                Operator = filterOperator,
                Negate = negate,
                RemoteField = "dur",
                DurationMilliseconds = values,
                RemoteApplied = !localOnly,
                VerifyLocally = localOnly || (ordered && values.Length > 1)
            },
            SelectiveSeed = !negate && !nullOperator,
            RequiresSeed = localOnly
        };
    }

    private bool DurationBoundaryMatchesNothing(CriteriaValueOperator valueOperator, TimeSpan value)
    {
        return valueOperator == CriteriaValueOperator.LessThan && value <= TimeSpan.Zero ||
            valueOperator == CriteriaValueOperator.LessThanOrEqual && value < TimeSpan.Zero;
    }

    private Compilation CompileGenres(
        QueryTarget target,
        IReadOnlyList<GenreCriteria> alternatives,
        bool negate)
    {
        var residualAlternatives = new List<ResidualAlternative>();
        foreach (var alternative in alternatives)
        {
            var predicates = new List<ResidualPredicate>();
            if (!TryCompileStringPredicates(alternative.Name.And, ResidualField.GenreName, predicates))
            {
                return UnsupportedCompilation();
            }

            residualAlternatives.Add(new ResidualAlternative { Predicates = predicates });
        }

        if (residualAlternatives.Count == 0)
        {
            return negate ? new Compilation() : KnownEmptyCompilation();
        }

        var canSeed = !negate &&
            residualAlternatives.All(alternative => alternative.Predicates.Count == 1) &&
            residualAlternatives.SelectMany(alternative => alternative.Predicates).All(predicate =>
                !predicate.Negate && predicate.Operator is CriteriaValueOperator.Equals or CriteriaValueOperator.In);
        if (!canSeed)
        {
            return ResidualCompilation(ResidualKind.Genre, ResidualNeed.Hit, residualAlternatives, negate);
        }

        var normalized = residualAlternatives
            .SelectMany(alternative => alternative.Predicates)
            .SelectMany(predicate => predicate.Values)
            .Select(value => value.Trim())
            .Where(value => value.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (normalized.Length == 0)
        {
            return UnsupportedCompilation();
        }

        return new Compilation
        {
            Filter = new QueryFilter
            {
                Kind = QueryFilterKind.Genre,
                Operator = CriteriaValueOperator.In,
                Negate = false,
                RemoteField = "tag",
                StringValues = normalized,
                RemoteApplied = true
            },
            SelectiveSeed = true
        };
    }

    private Compilation CompileExternalIds(
        QueryTarget target,
        IReadOnlyList<ExternalIdCriteria> alternatives,
        bool negate)
    {
        var identities = new List<IdentityValue>();
        foreach (var alternative in alternatives)
        {
            if (!TryCompileExternalAlternative(target, alternative, out var alternativeIdentities))
            {
                return CompileExternalIdResiduals(alternatives, negate);
            }

            identities.AddRange(alternativeIdentities);
        }

        if (identities.Count == 0)
        {
            return negate ? new Compilation() : KnownEmptyCompilation();
        }

        identities = [.. identities
            .Distinct()
            .OrderBy(identity => identity.Source, StringComparer.Ordinal)
            .ThenBy(identity => identity.Value, StringComparer.Ordinal)];

        return new Compilation
        {
            Filter = new QueryFilter
            {
                Kind = QueryFilterKind.ExternalId,
                Operator = identities.All(identity => identity.Value.Length == 0)
                    ? CriteriaValueOperator.IsNotNull
                    : identities.Count == 1 ? CriteriaValueOperator.Equals : CriteriaValueOperator.In,
                Negate = negate,
                IdentityValues = identities,
                RemoteApplied = true
            },
            SelectiveSeed = !negate && identities.Any(identity => identity.Value.Length > 0),
            RequiresSeed = identities.All(identity => identity.Value.Length == 0)
        };
    }

    private Compilation CompileExternalIdResiduals(IReadOnlyList<ExternalIdCriteria> alternatives, bool negate)
    {
        var residualAlternatives = new List<ResidualAlternative>();
        foreach (var alternative in alternatives)
        {
            var predicates = new List<ResidualPredicate>();
            if (!TryCompileStringPredicates(alternative.Source.And, ResidualField.ExternalIdSource, predicates) ||
                !TryCompileStringPredicates(alternative.Value.And, ResidualField.ExternalIdValue, predicates))
            {
                return UnsupportedCompilation();
            }

            residualAlternatives.Add(new ResidualAlternative { Predicates = predicates });
        }

        return ResidualCompilation(
            ResidualKind.ExternalId,
            ResidualNeed.EntityExternalIds,
            residualAlternatives,
            negate);
    }

    private bool TryCompileExternalAlternative(
        QueryTarget target,
        ExternalIdCriteria alternative,
        out IReadOnlyList<IdentityValue> result)
    {
        result = [];
        var sourceCriteria = alternative.Source.And.ToArray();
        var valueCriteria = alternative.Value.And.ToArray();
        if (sourceCriteria.Any(criterion => !SimplePositive(criterion)) ||
            valueCriteria.Any(criterion => !SimplePositive(criterion)))
        {
            return false;
        }

        var allowedSources = default(HashSet<string>);
        foreach (var criterion in sourceCriteria)
        {
            var requestedSources = TextValues(criterion)
                .Select(source => source.Trim().ToLowerInvariant())
                .Where(source => source.Length > 0)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (requestedSources.Count == 0)
            {
                return false;
            }

            var sources = requestedSources
                .Where(source => IdentityField(target, source).Length > 0)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (allowedSources is null)
            {
                allowedSources = sources;
            }
            else
            {
                allowedSources.IntersectWith(sources);
            }
        }

        if (allowedSources is { Count: 0 })
        {
            return true;
        }

        if (valueCriteria.Length == 0)
        {
            if (allowedSources is null)
            {
                return false;
            }

            result = [.. allowedSources.Select(source => new IdentityValue
            {
                Source = source.ToLowerInvariant(),
                Value = string.Empty,
                RemoteField = IdentityField(target, source)
            })];
            return true;
        }

        var possible = default(HashSet<IdentityValue>);
        foreach (var criterion in valueCriteria)
        {
            var criterionValues = new HashSet<IdentityValue>();
            var criterionDroppedInvalidValues = false;
            foreach (var rawValue in TextValues(criterion))
            {
                var value = rawValue.Trim();
                if (value.Length == 0)
                {
                    criterionDroppedInvalidValues = true;
                    continue;
                }

                var embeddedSeparator = value.IndexOf(':');
                var embeddedSource = embeddedSeparator > 0
                    ? value.Substring(0, embeddedSeparator).Trim().ToLowerInvariant()
                    : null;
                var identifier = embeddedSource is null ? value : value.Substring(embeddedSeparator + 1);
                var resolvedSources = embeddedSource is not null
                    ? [embeddedSource]
                    : allowedSources is not null
                        ? allowedSources
                        : Guid.TryParse(identifier, out _) ? [Constants.ProviderType] : [];
                var compatibleSource = false;
                var normalizedValue = false;
                foreach (var source in resolvedSources)
                {
                    if (allowedSources is not null && !allowedSources.Contains(source))
                    {
                        continue;
                    }

                    compatibleSource = true;
                    if (TryNormalizeIdentity(target, source, identifier, out var normalized, out var field))
                    {
                        normalizedValue = true;
                        criterionValues.Add(new IdentityValue
                        {
                            Source = source.ToLowerInvariant(),
                            Value = normalized,
                            RemoteField = field
                        });
                    }
                }

                if ((compatibleSource && !normalizedValue) || (!compatibleSource && allowedSources is null))
                {
                    criterionDroppedInvalidValues = true;
                }
            }

            if (criterionValues.Count == 0 && criterionDroppedInvalidValues)
            {
                return false;
            }

            if (possible is null)
            {
                possible = criterionValues;
            }
            else
            {
                possible.IntersectWith(criterionValues);
            }

            if (possible.Count == 0)
            {
                break;
            }
        }

        result = possible
            ?.OrderBy(identity => identity.Source, StringComparer.Ordinal)
            .ThenBy(identity => identity.Value, StringComparer.Ordinal)
            .ToArray() ??
            [];
        return true;
    }

    private Compilation CompileRelationships(
        QueryTarget target,
        IReadOnlyList<RelationshipCriteria> alternatives,
        bool negate)
    {
        if (target == QueryTarget.Artist)
        {
            return CompileArtistTargetRelationships(alternatives, negate);
        }

        var artistValues = new List<ArtistRelationshipValue>();
        var labelValues = new List<LabelRelationshipValue>();
        foreach (var alternative in alternatives)
        {
            if (!TryCompileRelationshipAlternative(target, alternative, artistValues, labelValues))
            {
                return RefusedCompilation();
            }
        }

        if (artistValues.Count > 0 && labelValues.Count > 0)
        {
            return RefusedCompilation();
        }

        if (labelValues.Count > 0)
        {
            return CompileLabelFilter(labelValues, negate);
        }

        if (artistValues.Count == 0)
        {
            return negate ? new Compilation() : KnownEmptyCompilation();
        }

        return new Compilation
        {
            Filter = new QueryFilter
            {
                Kind = QueryFilterKind.ArtistRelationship,
                Operator = artistValues.Count == 1 ? CriteriaValueOperator.Equals : CriteriaValueOperator.In,
                Negate = negate,
                RelationshipValues = artistValues,
                RemoteApplied = !negate,
                VerifyLocally = true
            },
            SelectiveSeed = !negate,
            RequiresSeed = negate
        };
    }

    private Compilation CompileArtistTargetRelationships(
        IReadOnlyList<RelationshipCriteria> alternatives,
        bool negate)
    {
        var alternative = alternatives[0];
        if (alternative.Qualifier.And.Any() || alternative.RelatedMetadataContainerId.And.Any() ||
            alternative.ExternalIds.None.Count > 0 ||
            alternative.Kind.And.Any(criterion => !SimplePositive(criterion)) ||
            alternative.Type.And.Any(criterion => !SimplePositive(criterion)) ||
            alternative.Subject.And.Any(criterion => !SimplePositive(criterion)))
        {
            return RefusedCompilation();
        }

        if (!TryIntersectSubjectNames([.. alternative.Subject.And], out var names))
        {
            return RefusedCompilation();
        }

        if (names is { Count: 0 })
        {
            return negate ? new Compilation() : KnownEmptyCompilation();
        }

        if (names is { Count: > 1 })
        {
            return RefusedCompilation();
        }

        var hasFacets = alternative.Kind.And.Any() || alternative.Type.And.Any();
        var facetEntity = hasFacets ? ResolveArtistCompositionEntityFromFacets(alternative) : null;
        if (hasFacets &&
            (facetEntity is null ||
                facetEntity == ArtistCompositionEntity.Artist && !ArtistMembershipFacetIsExplicit(alternative)))
        {
            return negate ? new Compilation() : KnownEmptyCompilation();
        }

        var identityOutcome = TryArtistCompositionIdentity(alternative.ExternalIds, out var identity);
        if (identityOutcome == ArtistCompositionParseOutcome.Empty)
        {
            return negate ? new Compilation() : KnownEmptyCompilation();
        }

        if (identityOutcome == ArtistCompositionParseOutcome.Unsupported)
        {
            return RefusedCompilation();
        }

        var entity = facetEntity;
        if (entity is null ||
            entity == ArtistCompositionEntity.Artist &&
            (!hasFacets || !ArtistMembershipFacetIsExplicit(alternative)) ||
            !ArtistCompositionFacetsMatch(alternative, entity.Value))
        {
            return RefusedCompilation();
        }

        if (identity is null && names is null)
        {
            return RefusedCompilation();
        }

        var source = Constants.ProviderType;
        var name = names?.Single();
        return new Compilation
        {
            ArtistComposition = new ArtistComposition
            {
                RelatedEntity = entity.Value,
                Resolution = name is null
                    ? ArtistCompositionResolution.KnownMbid
                    : ArtistCompositionResolution.ExactNameSearch,
                Expansion = entity == ArtistCompositionEntity.Artist
                    ? ArtistCompositionExpansion.BandMembers
                    : ArtistCompositionExpansion.BrowseArtists,
                Constraint = negate ? ArtistCompositionConstraint.Exclude : ArtistCompositionConstraint.Include,
                FinalStage = ArtistCompositionFinalStage.RunComposedSearch,
                ExternalIdSource = source,
                RelatedMbid = identity?.Mbid,
                RelatedName = name,
                RelatedArtistType = entity == ArtistCompositionEntity.Artist &&
                    (FacetExplicitlyMatches(alternative.Kind.And, Constants.PersonSubjectKind) ||
                        FacetExplicitlyMatches(alternative.Type.And, Constants.PersonSubjectKind))
                        ? Constants.PersonArtistType
                        : null
            },
            SelectiveSeed = !negate,
            RequiresSeed = negate
        };
    }

    private ArtistCompositionParseOutcome TryArtistCompositionIdentity(
        CollectionCriteria<ExternalIdCriteria> criteria,
        out ArtistCompositionIdentity? identity)
    {
        identity = null;
        HashSet<ArtistCompositionIdentity>? possible = null;
        foreach (var group in criteria.Some)
        {
            var groupValues = new HashSet<ArtistCompositionIdentity>();
            foreach (var alternative in group)
            {
                var outcome = TryArtistCompositionIdentityAlternative(alternative, out var values);
                if (outcome == ArtistCompositionParseOutcome.Unsupported)
                {
                    return outcome;
                }

                groupValues.UnionWith(values);
            }

            if (possible is null)
            {
                possible = groupValues;
            }
            else
            {
                possible.IntersectWith(groupValues);
            }
        }

        if (possible is null)
        {
            return ArtistCompositionParseOutcome.Success;
        }

        if (possible.Count == 0)
        {
            return ArtistCompositionParseOutcome.Empty;
        }

        if (possible.Count != 1)
        {
            return ArtistCompositionParseOutcome.Unsupported;
        }

        identity = possible.Single();
        return ArtistCompositionParseOutcome.Success;
    }

    private ArtistCompositionParseOutcome TryArtistCompositionIdentityAlternative(
        ExternalIdCriteria alternative,
        out IReadOnlyList<ArtistCompositionIdentity> identities)
    {
        identities = [];
        var sourceCriteria = alternative.Source.And.ToArray();
        var valueCriteria = alternative.Value.And.ToArray();
        if (sourceCriteria.Any(criterion => !SimplePositive(criterion)) || valueCriteria.Length == 0 ||
            valueCriteria.Any(criterion => !SimplePositive(criterion)))
        {
            return ArtistCompositionParseOutcome.Unsupported;
        }

        HashSet<string>? sources = null;
        foreach (var criterion in sourceCriteria)
        {
            var values = TextValues(criterion)
                .Select(value => value.ToLowerInvariant())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (values.Count == 0 || values.Any(value => !IsProviderSource(value)))
            {
                return ArtistCompositionParseOutcome.Unsupported;
            }

            if (sources is null)
            {
                sources = values;
            }
            else
            {
                sources.IntersectWith(values);
            }
        }

        HashSet<string>? rawValues = null;
        foreach (var criterion in valueCriteria)
        {
            var values = TextValues(criterion).ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (values.Count == 0)
            {
                return ArtistCompositionParseOutcome.Unsupported;
            }

            if (rawValues is null)
            {
                rawValues = values;
            }
            else
            {
                rawValues.IntersectWith(values);
            }
        }

        if (sources is { Count: 0 } || rawValues is not { Count: > 0 })
        {
            return ArtistCompositionParseOutcome.Empty;
        }

        var result = new HashSet<ArtistCompositionIdentity>();
        foreach (var rawValue in rawValues)
        {
            var separator = rawValue.IndexOf(':', StringComparison.Ordinal);
            var prefixedSource = separator > 0 ? rawValue.Substring(0, separator).ToLowerInvariant() : null;
            var value = separator > 0 ? rawValue.Substring(separator + 1) : rawValue;
            if (prefixedSource is not null && !IsProviderSource(prefixedSource))
            {
                return ArtistCompositionParseOutcome.Unsupported;
            }

            if (!Guid.TryParse(value, out var parsed))
            {
                return ArtistCompositionParseOutcome.Unsupported;
            }

            var mbid = parsed.ToString("D", CultureInfo.InvariantCulture);
            if (prefixedSource is not null)
            {
                if (sources is null || sources.Contains(prefixedSource))
                {
                    result.Add(new ArtistCompositionIdentity(prefixedSource, mbid));
                }

                continue;
            }

            if (sources is null)
            {
                result.Add(new ArtistCompositionIdentity(null, mbid));
            }
            else
            {
                result.UnionWith(sources.Select(source => new ArtistCompositionIdentity(source, mbid)));
            }
        }

        identities = [.. result];
        return identities.Count == 0 ? ArtistCompositionParseOutcome.Empty : ArtistCompositionParseOutcome.Success;
    }

    private ArtistCompositionEntity? ResolveArtistCompositionEntityFromFacets(RelationshipCriteria criteria)
    {
        var candidates = new List<ArtistCompositionEntity>
        {
            ArtistCompositionEntity.Artist,
            ArtistCompositionEntity.ReleaseGroup,
            ArtistCompositionEntity.Recording,
            ArtistCompositionEntity.Work
        };
        var matches = candidates.Where(entity => ArtistCompositionFacetsMatch(criteria, entity)).ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }

    private bool ArtistCompositionFacetsMatch(
        RelationshipCriteria criteria,
        ArtistCompositionEntity entity)
    {
        var relationshipKinds = entity == ArtistCompositionEntity.Artist
            ? new[] { Constants.BandMembershipRelationshipKind }
            : [Constants.ArtistRelationshipKind];
        var targetKinds = ArtistCompositionTargetKinds(entity);
        return FacetsMatch(criteria.Kind.And, relationshipKinds) && FacetsMatch(criteria.Type.And, targetKinds) ||
            FacetsMatch(criteria.Kind.And, targetKinds) && FacetsMatch(criteria.Type.And, relationshipKinds);
    }

    private bool ArtistMembershipFacetIsExplicit(RelationshipCriteria criteria)
    {
        return FacetExplicitlyMatches(criteria.Kind.And, Constants.BandMembershipRelationshipKind) ||
            FacetExplicitlyMatches(criteria.Type.And, Constants.BandMembershipRelationshipKind);
    }

    private bool FacetExplicitlyMatches(
        IEnumerable<PropertyCriterion<string>> criteria,
        string candidate)
    {
        return criteria.Any(criterion => InterpretOperator(
            criterion.Operator,
            criterion.Negate).Operator.MatchesString(TextValues(criterion), candidate));
    }

    private bool FacetsMatch(
        IEnumerable<PropertyCriterion<string>> criteria,
        IReadOnlyList<string> candidates)
    {
        return criteria.All(criterion =>
            candidates.Any(candidate => InterpretOperator(
                criterion.Operator,
                criterion.Negate).Operator.MatchesString(TextValues(criterion), candidate)));
    }

    private IReadOnlyList<string> ArtistCompositionTargetKinds(ArtistCompositionEntity entity)
    {
        return entity switch
        {
            ArtistCompositionEntity.Artist =>
                [Constants.ArtistLevelSlug, Constants.PersonSubjectKind],
            ArtistCompositionEntity.ReleaseGroup => [Constants.AlbumLevelSlug],
            ArtistCompositionEntity.Release => [Constants.AlbumLevelSlug],
            ArtistCompositionEntity.Recording => [Constants.TrackLevelSlug],
            ArtistCompositionEntity.Work => [Constants.WorkSubjectKind],
            _ => []
        };
    }

    private bool IsProviderSource(string? source)
    {
        return string.Equals(
            source?.Trim(),
            Constants.ProviderType,
            StringComparison.OrdinalIgnoreCase);
    }

    private Compilation CompileLabelFilter(IReadOnlyList<LabelRelationshipValue> labelValues, bool negate)
    {
        var alternatives = LabelResidualAlternatives(labelValues);
        if (negate)
        {
            return ResidualCompilation(
                ResidualKind.LabelRelationship,
                ResidualNeed.RepresentativeReleaseLabels,
                alternatives,
                negate: true);
        }

        var values = labelValues
            .Distinct()
            .OrderBy(value => value.LabelMbid, StringComparer.Ordinal)
            .ThenBy(value => value.LabelName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return new Compilation
        {
            Filter = new QueryFilter
            {
                Kind = QueryFilterKind.LabelRelationship,
                Operator = values.Length == 1 ? CriteriaValueOperator.Equals : CriteriaValueOperator.In,
                Negate = false,
                LabelValues = values,
                RemoteApplied = true,
                ReleaseIndexApplied = true,
                VerifyLocally = true
            },
            Residual = new ResidualCriterion
            {
                Kind = ResidualKind.LabelRelationship,
                Need = ResidualNeed.RepresentativeReleaseLabels,
                Alternatives = alternatives
            },
            SelectiveSeed = true
        };
    }

    private IReadOnlyList<ResidualAlternative> LabelResidualAlternatives(
        IReadOnlyList<LabelRelationshipValue> values)
    {
        return [.. values.Select(value =>
            new ResidualAlternative
            {
                Predicates =
                [
                    new ResidualPredicate
                    {
                        Field = ResidualField.LabelKind,
                        Operator = CriteriaValueOperator.Equals,
                        Values = [Constants.LabelSubjectKind]
                    },
                    .. value.LabelName is null
                        ? []
                        : new[]
                        {
                            new ResidualPredicate
                            {
                                Field = ResidualField.LabelSubject,
                                Operator = CriteriaValueOperator.Equals,
                                Values = [value.LabelName]
                            }
                        },
                    .. value.LabelMbid is null
                        ? []
                        : new[]
                        {
                            new ResidualPredicate
                            {
                                Field = ResidualField.LabelExternalId,
                                Operator = CriteriaValueOperator.Equals,
                                Values = [value.LabelMbid]
                            }
                        }
                ]
            })];
    }

    private bool TryCompileRelationshipAlternative(
        QueryTarget target,
        RelationshipCriteria alternative,
        List<ArtistRelationshipValue> artistValues,
        List<LabelRelationshipValue> labelValues)
    {
        if (alternative.Qualifier.And.Any() || alternative.RelatedMetadataContainerId.And.Any())
        {
            return false;
        }

        var kindCriteria = alternative.Kind.And.ToArray();
        var typeCriteria = alternative.Type.And.ToArray();
        if (kindCriteria.Any(criterion => !SimplePositive(criterion)) ||
            typeCriteria.Any(criterion => !SimplePositive(criterion)))
        {
            return false;
        }

        var subjects = alternative.Subject.And.ToArray();
        if (kindCriteria.Length == 0 && typeCriteria.Length == 0)
        {
            return subjects.Length == 0 &&
                TryCompileArtistAlternative(alternative, subjects, artistValues, artistType: null);
        }

        var artistKinds = new[] { Constants.ArtistRelationshipKind };
        var personTypes = new[] { Constants.PersonSubjectKind };
        var canonicalArtist = FacetsMatch(kindCriteria, artistKinds) && FacetsMatch(typeCriteria, personTypes);
        var swappedArtist = FacetsMatch(kindCriteria, personTypes) && FacetsMatch(typeCriteria, artistKinds);
        var matchesArtist = canonicalArtist || swappedArtist;
        var requiresPersonType =
            canonicalArtist && typeCriteria.Length > 0 ||
            swappedArtist && kindCriteria.Length > 0;
        var artistType = requiresPersonType ? Constants.PersonArtistType : null;
        var labelTypes = new[] { Constants.LabelSubjectKind };
        var matchesLabel = target == QueryTarget.Album &&
            FacetsMatch(kindCriteria, labelTypes) &&
            FacetsMatch(typeCriteria, labelTypes);
        if (matchesArtist && matchesLabel)
        {
            return false;
        }

        if (!matchesArtist && !matchesLabel)
        {
            return true;
        }

        return matchesArtist
            ? TryCompileArtistAlternative(alternative, subjects, artistValues, artistType)
            : TryCompileLabelAlternative(alternative, subjects, labelValues);
    }

    private bool TryCompileArtistAlternative(
        RelationshipCriteria alternative,
        IReadOnlyList<PropertyCriterion<string>> subjects,
        List<ArtistRelationshipValue> artistValues,
        string? artistType)
    {
        if (alternative.ExternalIds.None.Count > 0 ||
            !TryCompileArtistNames(subjects, out var names, out var nameOperator))
        {
            return false;
        }

        var mbids = default(HashSet<string>);
        foreach (var group in alternative.ExternalIds.Some)
        {
            var groupMbids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var external in group)
            {
                if (!TryCompileExternalAlternative(QueryTarget.Artist, external, out var identities) ||
                    identities.Any(identity => identity.Source != Constants.ProviderType))
                {
                    return false;
                }

                groupMbids.UnionWith(identities.Select(identity => identity.Value));
            }

            if (mbids is null)
            {
                mbids = groupMbids;
            }
            else
            {
                mbids.IntersectWith(groupMbids);
            }
        }

        if (names is { Count: 0 } || mbids is { Count: 0 })
        {
            return true;
        }

        if (names is null && mbids is null)
        {
            return false;
        }

        artistValues.AddRange(names is null
            ? mbids!
                .OrderBy(value => value, StringComparer.Ordinal)
                .Select(value => new ArtistRelationshipValue { ArtistMbid = value, ArtistType = artistType })
            : mbids is null
                ? names
                    .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                    .Select(value => new ArtistRelationshipValue
                    {
                        ArtistName = value,
                        ArtistNameOperator = nameOperator,
                        ArtistType = artistType
                    })
                : names
                    .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                    .SelectMany(name => mbids
                        .OrderBy(value => value, StringComparer.Ordinal)
                        .Select(mbid => new ArtistRelationshipValue
                        {
                            ArtistMbid = mbid,
                            ArtistName = name,
                            ArtistNameOperator = nameOperator,
                            ArtistType = artistType
                        })));
        return true;
    }

    private bool TryCompileArtistNames(
        IReadOnlyList<PropertyCriterion<string>> subjects,
        out HashSet<string>? names,
        out CriteriaValueOperator nameOperator)
    {
        nameOperator = CriteriaValueOperator.Equals;
        if (subjects.Count == 1)
        {
            var interpreted = InterpretOperator(subjects[0].Operator, subjects[0].Negate);
            if (!interpreted.Negate && interpreted.Operator == CriteriaValueOperator.ContainsString)
            {
                names = TextValues(subjects[0]).ToHashSet(StringComparer.OrdinalIgnoreCase);
                nameOperator = CriteriaValueOperator.ContainsString;
                return names.Count > 0;
            }
        }

        if (subjects.Any(criterion => !SimplePositive(criterion)))
        {
            names = null;
            return false;
        }

        return TryIntersectSubjectNames(subjects, out names);
    }

    private bool TryCompileLabelAlternative(
        RelationshipCriteria alternative,
        IReadOnlyList<PropertyCriterion<string>> subjects,
        List<LabelRelationshipValue> labelValues)
    {
        if (alternative.ExternalIds.None.Count > 0 || subjects.Any(criterion => !SimplePositive(criterion)))
        {
            return false;
        }

        if (!TryIntersectSubjectNames(subjects, out var names))
        {
            return false;
        }

        var mbids = default(HashSet<string>);
        foreach (var group in alternative.ExternalIds.Some)
        {
            var groupMbids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var external in group)
            {
                if (!TryCompileLabelExternalAlternative(external, out var identities))
                {
                    return false;
                }

                groupMbids.UnionWith(identities);
            }

            if (mbids is null)
            {
                mbids = groupMbids;
            }
            else
            {
                mbids.IntersectWith(groupMbids);
            }
        }

        if (names is { Count: 0 } || mbids is { Count: 0 })
        {
            return true;
        }

        if (names is null && mbids is null)
        {
            return false;
        }

        labelValues.AddRange(names is null
            ? mbids!
                .OrderBy(value => value, StringComparer.Ordinal)
                .Select(value => new LabelRelationshipValue { LabelMbid = value })
            : mbids is null
                ? names
                    .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                    .Select(value => new LabelRelationshipValue { LabelName = value })
                : names
                    .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                    .SelectMany(name => mbids
                        .OrderBy(value => value, StringComparer.Ordinal)
                        .Select(mbid => new LabelRelationshipValue { LabelMbid = mbid, LabelName = name })));
        return true;
    }

    private bool TryIntersectSubjectNames(
        IReadOnlyList<PropertyCriterion<string>> subjects,
        out HashSet<string>? names)
    {
        names = null;
        foreach (var criterion in subjects)
        {
            var criterionNames = TextValues(criterion).ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (criterionNames.Count == 0)
            {
                return false;
            }

            if (names is null)
            {
                names = criterionNames;
            }
            else
            {
                names.IntersectWith(criterionNames);
            }
        }

        return true;
    }

    private bool TryCompileLabelExternalAlternative(
        ExternalIdCriteria alternative,
        out IReadOnlyList<string> result)
    {
        result = [];
        var sourceCriteria = alternative.Source.And.ToArray();
        var valueCriteria = alternative.Value.And.ToArray();
        if (sourceCriteria.Any(criterion => !SimplePositive(criterion)) || valueCriteria.Length == 0 ||
            valueCriteria.Any(criterion => !SimplePositive(criterion)))
        {
            return false;
        }

        foreach (var criterion in sourceCriteria)
        {
            var sources = TextValues(criterion);
            if (sources.Length == 0 || sources.Any(source => !IsLabelSource(source)))
            {
                return false;
            }
        }

        var possible = default(HashSet<string>);
        foreach (var criterion in valueCriteria)
        {
            var criterionValues = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var rawValue in TextValues(criterion))
            {
                var value = rawValue.Trim();
                var separator = value.IndexOf(':');
                if (separator > 0 && IsLabelSource(value.Substring(0, separator)))
                {
                    value = value.Substring(separator + 1).Trim();
                }

                if (!Guid.TryParse(value, out var mbid))
                {
                    return false;
                }

                criterionValues.Add(mbid.ToString("D", CultureInfo.InvariantCulture));
            }

            if (possible is null)
            {
                possible = criterionValues;
            }
            else
            {
                possible.IntersectWith(criterionValues);
            }
        }

        result = possible?.OrderBy(value => value, StringComparer.Ordinal).ToArray() ?? [];
        return true;
    }

    private bool IsLabelSource(string source)
    {
        return string.Equals(
            source.Trim(),
            Constants.ProviderType,
            StringComparison.OrdinalIgnoreCase);
    }

    private (QueryStrategy Strategy, QueryFilter? JustifyingFilter) SelectStrategy(
        QueryTarget target,
        IReadOnlyList<QueryFilter> filters,
        bool usesArtistComposition)
    {
        if (usesArtistComposition)
        {
            return (QueryStrategy.Lucene, null);
        }

        var hasSeed = filters.Any(filter =>
            filter.RemoteApplied &&
            !filter.Negate &&
            filter.Operator is not CriteriaValueOperator.IsNull and not CriteriaValueOperator.IsNotNull);
        if (!hasSeed)
        {
            return (QueryStrategy.None, null);
        }

        foreach (var identityFilter in filters.Where(filter =>
            filter is { Kind: QueryFilterKind.ExternalId, Negate: false } &&
            filter.IdentityValues.Count == 1 &&
            filter.IdentityValues[0].Value.Length > 0))
        {
            var strategy = StrategyForIdentity(target, identityFilter.IdentityValues[0]);
            if (strategy != QueryStrategy.Lucene && filters.All(filter => CanVerifyFromLookup(strategy, filter)))
            {
                return (strategy, identityFilter);
            }
        }

        return (QueryStrategy.Lucene, null);
    }

    private QueryStrategy StrategyForIdentity(
        QueryTarget target,
        IdentityValue identity)
    {
        return (target, identity.Source) switch
        {
            (QueryTarget.Artist, Constants.ProviderType) =>
                QueryStrategy.ArtistLookup,
            (QueryTarget.Album, Constants.ProviderType) =>
                QueryStrategy.ReleaseGroupLookup,
            (QueryTarget.Track, Constants.ProviderType) =>
                QueryStrategy.RecordingLookup,
            (QueryTarget.Track, Constants.IsrcSource) =>
                QueryStrategy.IsrcLookup,
            _ => QueryStrategy.Lucene
        };
    }

    private bool CanVerifyFromLookup(QueryStrategy strategy, QueryFilter filter)
    {
        if (filter.Kind == QueryFilterKind.Genre)
        {
            return false;
        }

        if (filter.Kind == QueryFilterKind.ExternalId)
        {
            return filter.IdentityValues.All(identity => LookupIncludesIdentity(strategy, identity.Source));
        }

        return strategy switch
        {
            QueryStrategy.ArtistLookup => filter.Kind is QueryFilterKind.Title
                or QueryFilterKind.SortTitle
                or QueryFilterKind.Overview
                or QueryFilterKind.Established
                or QueryFilterKind.Dissolved
                or QueryFilterKind.BirthDate
                or QueryFilterKind.DeathDate
                or QueryFilterKind.AlternateText,
            QueryStrategy.ReleaseGroupLookup => filter.Kind is QueryFilterKind.Title
                or QueryFilterKind.Overview
                or QueryFilterKind.PublishDate
                or QueryFilterKind.ArtistRelationship
                or QueryFilterKind.AlternateText,
            QueryStrategy.ReleaseLookup => false,
            QueryStrategy.RecordingLookup or QueryStrategy.IsrcLookup =>
                filter.Kind is QueryFilterKind.Title
                    or QueryFilterKind.Overview
                    or QueryFilterKind.Duration
                    or QueryFilterKind.ArtistRelationship
                    or QueryFilterKind.AlternateText,
            _ => false
        };
    }

    private bool LookupIncludesIdentity(QueryStrategy strategy, string source)
    {
        return strategy switch
        {
            QueryStrategy.ArtistLookup => source is Constants.ProviderType
                or Constants.IsniSource
                or Constants.IpiSource,
            QueryStrategy.ReleaseGroupLookup or QueryStrategy.ReleaseLookup =>
                source == Constants.ProviderType,
            QueryStrategy.RecordingLookup or QueryStrategy.IsrcLookup =>
                source is Constants.ProviderType or Constants.IsrcSource,
            _ => false
        };
    }

    private bool TryNormalizeIdentity(
        QueryTarget target,
        string source,
        string value,
        out string normalized,
        out string field)
    {
        normalized = string.Empty;
        var canonicalSource = source.Trim().ToLowerInvariant();
        field = IdentityField(target, canonicalSource);
        if (field.Length == 0)
        {
            return false;
        }

        if (canonicalSource == Constants.ProviderType)
        {
            if (!Guid.TryParse(value, out var mbid))
            {
                return false;
            }

            normalized = mbid.ToString("D", CultureInfo.InvariantCulture);
            return true;
        }

        if (canonicalSource == Constants.IsrcSource)
        {
            var isrc = new string(
                    [.. value.Where(character => character != '-' && !char.IsWhiteSpace(character))])
                .ToUpperInvariant();
            if (isrc.Length != 12 || !isrc.All(char.IsLetterOrDigit))
            {
                return false;
            }

            normalized = isrc;
            return true;
        }

        var compact = new string([.. value.Where(character => !char.IsWhiteSpace(character))]).ToUpperInvariant();
        if (compact.Length == 0 || !compact.All(character => char.IsLetterOrDigit(character)))
        {
            return false;
        }

        normalized = compact;
        return true;
    }

    private string IdentityField(QueryTarget target, string source)
    {
        return (target, source.Trim().ToLowerInvariant()) switch
        {
            (QueryTarget.Artist, Constants.ProviderType) => "arid",
            (QueryTarget.Artist, Constants.IsniSource) => "isni",
            (QueryTarget.Artist, Constants.IpiSource) => "ipi",
            (QueryTarget.Album, Constants.ProviderType) => "rgid",
            (QueryTarget.Track, Constants.ProviderType) => "rid",
            (QueryTarget.Track, Constants.IsrcSource) => "isrc",
            _ => string.Empty
        };
    }

    private bool SimplePositive(PropertyCriterion<string> criterion)
    {
        var interpreted = InterpretOperator(criterion.Operator, criterion.Negate);
        return !interpreted.Negate && interpreted.Operator is CriteriaValueOperator.Equals or CriteriaValueOperator.In;
    }

    private string[] TextValues(PropertyCriterion<string> criterion)
    {
        var values = new List<string>();
        if (!string.IsNullOrWhiteSpace(criterion.Value))
        {
            values.Add(criterion.Value.Trim());
        }

        values.AddRange(
            (criterion.Values ?? [])
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value.Trim()));
        return [.. values.Distinct(StringComparer.OrdinalIgnoreCase)];
    }

    private string[] UriValues(PropertyCriterion<Uri> criterion)
    {
        var values = new List<string>();
        if (criterion.Value is not null)
        {
            values.Add(criterion.Value.AbsoluteUri);
        }

        values.AddRange(
            (criterion.Values ?? [])
                .Where(value => value is not null)
                .Select(value => value.AbsoluteUri));
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
        var values = (criterion.Values ?? []).ToList();
        if (criterion.Value != default || values.Count == 0)
        {
            values.Insert(0, criterion.Value);
        }

        return values;
    }

    private (CriteriaValueOperator Operator, bool Negate) InterpretOperator(
        CriteriaValueOperator op,
        bool negate)
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

    private bool IsStringOperator(CriteriaValueOperator value)
    {
        return value is
            CriteriaValueOperator.Equals
            or CriteriaValueOperator.In
            or CriteriaValueOperator.ContainsString
            or CriteriaValueOperator.ContainsWord
            or CriteriaValueOperator.StartsWithString
            or CriteriaValueOperator.EndsWithString
            or CriteriaValueOperator.IsNull
            or CriteriaValueOperator.IsNotNull;
    }

    private bool IsOrderedOperator(CriteriaValueOperator value)
    {
        return value is
            CriteriaValueOperator.Equals
            or CriteriaValueOperator.In
            or CriteriaValueOperator.GreaterThan
            or CriteriaValueOperator.GreaterThanOrEqual
            or CriteriaValueOperator.LessThan
            or CriteriaValueOperator.LessThanOrEqual
            or CriteriaValueOperator.IsNull
            or CriteriaValueOperator.IsNotNull;
    }

    private string? StringField(QueryTarget target, MetadataField? field)
    {
        return (target, field) switch
        {
            (QueryTarget.Artist, MetadataField.Title) => "artistaccent",
            (QueryTarget.Artist, MetadataField.SortTitle) => "sortname",
            (QueryTarget.Artist, MetadataField.Overview) => "comment",
            (QueryTarget.Album, MetadataField.Title) => "releasegroupaccent",
            (QueryTarget.Album, MetadataField.Overview) => "comment",
            (QueryTarget.Track, MetadataField.Title) => "recordingaccent",
            _ => null
        };
    }

    private string? TemporalField(QueryTarget target, MetadataField? field)
    {
        return (target, field) switch
        {
            (QueryTarget.Artist, MetadataField.Established) => "begin",
            (QueryTarget.Artist, MetadataField.Dissolved) => "end",
            (QueryTarget.Artist, MetadataField.BirthDate) => "begin",
            (QueryTarget.Artist, MetadataField.DeathDate) => "end",
            (QueryTarget.Album, MetadataField.PublishDate) => "firstreleasedate",
            (QueryTarget.Track, MetadataField.PublishDate) => "firstreleasedate",
            _ => null
        };
    }

    private QueryFilterKind StringKind(MetadataField? field)
    {
        return field switch
        {
            MetadataField.Title => QueryFilterKind.Title,
            MetadataField.SortTitle => QueryFilterKind.SortTitle,
            MetadataField.Overview => QueryFilterKind.Overview,
            _ => throw new InvalidOperationException("field")
        };
    }

    private QueryFilterKind TemporalKind(MetadataField? field)
    {
        return field switch
        {
            MetadataField.Established => QueryFilterKind.Established,
            MetadataField.Dissolved => QueryFilterKind.Dissolved,
            MetadataField.BirthDate => QueryFilterKind.BirthDate,
            MetadataField.DeathDate => QueryFilterKind.DeathDate,
            MetadataField.PublishDate => QueryFilterKind.PublishDate,
            _ => throw new InvalidOperationException("field")
        };
    }

    private Compilation UnsupportedCompilation() => new() { Unsupported = true };

    private Compilation RefusedCompilation() => new() { Unsupported = true, RefusesTarget = true };

    private bool IsRefusedCompilation(Compilation compilation) => compilation.RefusesTarget;

    private Compilation KnownEmptyCompilation() => new() { MatchesNothing = true };

    private Compilation AtSource(Compilation compilation, string source)
    {
        return compilation with
        {
            Source = source,
            ArtistComposition = compilation.ArtistComposition is null
            ? null
            : compilation.ArtistComposition with { AlternativeGroup = source }
        };
    }

    private string CriterionSource(string property, string collection, int index)
    {
        return string.Concat(property, ".", collection, "[", index.ToString(CultureInfo.InvariantCulture), "]");
    }

    private Compilation ResidualCompilation(
        ResidualKind kind,
        ResidualNeed need,
        IReadOnlyList<ResidualAlternative> alternatives,
        bool negate)
    {
        return new()
        {
            Residual = new ResidualCriterion
            {
                Kind = kind,
                Need = need,
                Negate = negate,
                Alternatives = alternatives
            },
            RequiresSeed = true
        };
    }

    private string Fingerprint(
        QueryTarget target,
        IReadOnlyList<QueryFilter> filters,
        IReadOnlyList<ResidualCriterion> residuals,
        IReadOnlyList<ArtistComposition> artistCompositions,
        string? ordinaryTerm)
    {
        var canonical = new StringBuilder(target.ToString());
        canonical.Append('|').Append(ordinaryTerm);
        foreach (var filter in filters)
        {
            canonical
                .Append('|')
                .Append(filter.Kind)
                .Append(':')
                .Append(filter.Operator)
                .Append(':')
                .Append(filter.Negate)
                .Append(':')
                .Append(filter.RemoteField)
                .Append(':')
                .Append(filter.AlternateRemoteField)
                .Append(':')
                .Append(filter.IncludePrimaryTitle)
                .Append(':')
                .Append(filter.ReleaseIndexApplied)
                .Append(':')
                .Append(string.Join(",", filter.StringValues))
                .Append(':')
                .Append(string.Join(
                    ",",
                    filter.TemporalValues.Select(value =>
                        value.Value.ToString("O", CultureInfo.InvariantCulture) + "/" + value.Precision)))
                .Append(':')
                .Append(string.Join(",", filter.DurationMilliseconds))
                .Append(':')
                .Append(string.Join(
                    ",",
                    filter.IdentityValues.Select(value => value.Source + "=" + value.Value)))
                .Append(':')
                .Append(string.Join(
                    ",",
                    filter.RelationshipValues.Select(value =>
                        string.Concat(
                            value.ArtistMbid ?? value.ArtistName,
                            "/",
                            value.ArtistNameOperator,
                            "/",
                            value.ArtistType))))
                .Append(':')
                .Append(string.Join(
                    ",",
                    filter.LabelValues.Select(value => value.LabelMbid ?? value.LabelName)));
        }

        foreach (var residual in residuals)
        {
            canonical
                .Append("|residual:")
                .Append(residual.Kind)
                .Append(':')
                .Append(residual.Need)
                .Append(':')
                .Append(residual.Negate)
                .Append(':')
                .Append(residual.IncludePrimaryTitle);
            foreach (var alternative in residual.Alternatives)
            {
                canonical.Append("[alternative");
                foreach (var predicate in alternative.Predicates)
                {
                    canonical
                        .Append('{')
                        .Append(predicate.Field)
                        .Append(':')
                        .Append(predicate.Operator)
                        .Append(':')
                        .Append(predicate.Negate)
                        .Append(':')
                        .Append(string.Join(",", predicate.Values))
                        .Append('}');
                }

                canonical.Append(']');
            }
        }

        foreach (var composition in artistCompositions)
        {
            canonical
                .Append("|artist-composition:")
                .Append(composition.AlternativeGroup)
                .Append(':')
                .Append(composition.RelatedEntity)
                .Append(':')
                .Append(composition.Resolution)
                .Append(':')
                .Append(composition.Expansion)
                .Append(':')
                .Append(composition.Constraint)
                .Append(':')
                .Append(composition.FinalStage)
                .Append(':')
                .Append(composition.RelatedNameOperator)
                .Append(':')
                .Append(composition.RelatedArtistType)
                .Append(':')
                .Append(composition.ExpansionCapabilities)
                .Append(':')
                .Append(composition.ExternalIdSource)
                .Append(':')
                .Append(composition.RelatedMbid)
                .Append(':')
                .Append(composition.RelatedName);
        }

        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(canonical.ToString()));
        return BitConverter.ToString(hash).Replace("-", string.Empty).ToLowerInvariant();
    }

    private QueryPlan OrdinaryPlan(QueryRequest request)
    {
        var term = request.OrdinaryTerm?.Trim();
        var strategy = string.IsNullOrWhiteSpace(term) ? QueryStrategy.None : QueryStrategy.Lucene;
        if (strategy == QueryStrategy.None)
        {
            RecordPlanRefused("unbounded");
        }

        var state = new PlanningState
        {
            Target = request.Target,
            Filters = [],
            Residuals = [],
            ArtistCompositions = [],
            OrdinaryTerm = term
        };
        var plan = new QueryPlan
        {
            RequestedPage = Math.Max(1, request.RequestedPage),
            MaxResults = Math.Max(1, request.MaxResults),
            Settings = request.Settings,
            Warnings = [],
            Fingerprint = Fingerprint(request.Target, [], [], [], term)
        };
        plan = plan with { Root = BuildRoot(state, strategy) };
        RecordPlan(plan);
        return plan;
    }

    private QueryTraceProvenance BuildTraceProvenance(
        PlanningState state,
        bool knownEmpty,
        QueryStrategy strategy,
        IReadOnlyList<CriterionTrace> criterionTraces)
    {
        return new()
        {
            Criteria = [.. from trace in criterionTraces
                        let disposition = CriterionDisposition(state, knownEmpty, strategy, trace)
                        group disposition by trace.Source
                        into dispositions
                        select new QueryTraceProvenance.Criterion
                        {
                            Source = dispositions.Key,
                            FinalDisposition = dispositions.Aggregate(
                                QueryTraceProvenance.Disposition.None,
                                (combined, value) => combined | value)
                        }]
        };
    }

    private void RecordPlan(QueryPlan plan)
    {
        var fetch = plan.Root?.Flatten().OfType<FetchPlanNode>().FirstOrDefault();
        Telemetry.Plans.Add(
            1,
            Telemetry.Provider,
            Telemetry.ContentType,
            new KeyValuePair<string, object?>("anchor_kind", fetch?.AnchorKind.ToString() ?? "none"));
    }

    private void RecordPlanRefused(string reason)
    {
        Telemetry.PlansRefused.Add(
            1,
            Telemetry.Provider,
            Telemetry.ContentType,
            new KeyValuePair<string, object?>("reason", reason));
    }

    private QueryTraceProvenance.Disposition CriterionDisposition(
        PlanningState state,
        bool knownEmpty,
        QueryStrategy strategy,
        CriterionTrace trace)
    {
        var disposition = QueryTraceProvenance.Disposition.None;
        if (trace.FilterIndex is { } filterIndex)
        {
            var filter = state.Filters[filterIndex];
            if (strategy is QueryStrategy.ArtistLookup
                    or QueryStrategy.ReleaseGroupLookup
                    or QueryStrategy.ReleaseLookup
                    or QueryStrategy.RecordingLookup
                    or QueryStrategy.IsrcLookup &&
                filter.IdentityValues.Any(value => string.Equals(
                    value.Value,
                    state.DirectValue,
                    StringComparison.OrdinalIgnoreCase)))
            {
                disposition |= QueryTraceProvenance.Disposition.DirectLookup;
            }

            if (filter.RemoteApplied)
            {
                disposition |= QueryTraceProvenance.Disposition.RemoteFilter;
            }

            if (filter.ReleaseIndexApplied)
            {
                disposition |= QueryTraceProvenance.Disposition.ReleaseIndexFilter;
            }

            if (filter.VerifyLocally)
            {
                disposition |= QueryTraceProvenance.Disposition.LocalFilter;
            }
        }

        if (trace.ResidualIndex is not null)
        {
            disposition |= QueryTraceProvenance.Disposition.Residual;
        }

        if (trace.ArtistCompositionIndex is not null)
        {
            disposition |= QueryTraceProvenance.Disposition.Composition;
        }

        if (trace.PlannedEmpty)
        {
            disposition |= QueryTraceProvenance.Disposition.PlannedEmpty;
        }

        if (trace.Refused)
        {
            disposition |= QueryTraceProvenance.Disposition.Refused;
        }

        if (trace.Vacuous)
        {
            disposition |= QueryTraceProvenance.Disposition.Vacuous;
        }

        if (knownEmpty && !trace.PlannedEmpty)
        {
            disposition |= QueryTraceProvenance.Disposition.SuppressedByPlannedEmpty;
        }

        if (strategy == QueryStrategy.None && !knownEmpty && !trace.Vacuous)
        {
            disposition |= QueryTraceProvenance.Disposition.SuppressedByNoBoundedSeed;
        }

        return disposition;
    }

    private bool SupportsDuration(QueryTarget target)
    {
        return target is QueryTarget.Album or QueryTarget.Track;
    }

    private bool SupportsLanguage(QueryTarget target)
    {
        return target == QueryTarget.Album;
    }

    private bool SupportsPublishDate(QueryTarget target)
    {
        return target is QueryTarget.Album or QueryTarget.Track;
    }

    private sealed record Compilation
    {
        public string Source { get; init; } = "Unknown";

        public QueryFilter? Filter { get; init; }

        public ResidualCriterion? Residual { get; init; }

        public ArtistComposition? ArtistComposition { get; init; }

        public bool Unsupported { get; init; }

        public bool SelectiveSeed { get; init; }

        public bool RequiresSeed { get; init; }

        public bool MatchesNothing { get; init; }

        public bool RefusesTarget { get; init; }
    }

    private sealed record CriterionTrace
    {
        public required string Source { get; init; }

        public int? FilterIndex { get; init; }

        public int? ResidualIndex { get; init; }

        public int? ArtistCompositionIndex { get; init; }

        public bool PlannedEmpty { get; init; }

        public bool Refused { get; init; }

        public bool Vacuous { get; init; }
    }

    private sealed record ArtistCompositionIdentity(string? Source, string Mbid);
}
