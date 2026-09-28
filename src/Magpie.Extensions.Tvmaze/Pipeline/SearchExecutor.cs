using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Extensions.Tvmaze.Mapping;
using Magpie.Models;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Filtering;
using Magpie.Models.MetadataSearch;
using Magpie.Sdk.Content;

using Microsoft.Extensions.Logging;

using CriterionGrammar = Magpie.Extensions.Tvmaze.Pipeline.CriterionGrammarExtensions;

namespace Magpie.Extensions.Tvmaze.Pipeline;

internal sealed class SearchExecutor(IClient client, ILogger<SearchExecutor> logger) : ISearchExecutor
{
    private enum NodeExecutionStatus
    {
        Success,
        Empty,
        Failed,
        Unavailable,
        Skipped
    }

    private sealed record NodeExecutionResult(
        NodeExecutionStatus Status,
        IReadOnlyList<ShowDto> Candidates,
        int RowsIn = 0,
        int Rejections = 0)
    {
        internal static NodeExecutionResult Empty { get; } = new(NodeExecutionStatus.Empty, []);

        internal static NodeExecutionResult Failed { get; } = new(NodeExecutionStatus.Failed, []);

        internal static NodeExecutionResult Unavailable { get; } = new(NodeExecutionStatus.Unavailable, []);

        internal static NodeExecutionResult Skipped { get; } = new(NodeExecutionStatus.Skipped, []);

        internal static NodeExecutionResult From(IReadOnlyList<ShowDto> candidates, int? rowsIn = null)
        {
            var inputCount = rowsIn ?? candidates.Count;
            return new NodeExecutionResult(
                candidates.Count == 0 ? NodeExecutionStatus.Empty : NodeExecutionStatus.Success,
                candidates,
                inputCount,
                Math.Max(0, inputCount - candidates.Count));
        }
    }

    private sealed record ResolvedPerson(PersonDto Person, IReadOnlyList<SearchSeed.PersonAlternative> Alternatives);

    private sealed record ResolveExecutionResult(NodeExecutionStatus Status, IReadOnlyList<ResolvedPerson> People)
    {
        internal static ResolveExecutionResult Empty { get; } = new(NodeExecutionStatus.Empty, []);

        internal static ResolveExecutionResult Failed { get; } = new(NodeExecutionStatus.Failed, []);
    }

    private sealed class PlanExecutionContext(
        int requiredCount,
        int maximumResults)
    {
        private readonly Dictionary<int, int> _branchIndexes = [];

        internal QueryProgress? Progress { get; init; }

        internal bool Incomplete { get; set; }

        internal int RequiredCount { get; } = requiredCount;

        internal List<string> Warnings { get; } = [];

        internal QueryExecutionOutcome Outcome { get; } = new() { MaximumResults = maximumResults };

        internal int FilterRowsIn { get; set; }

        internal HashSet<string> Embeds { get; } = new(StringComparer.Ordinal);

        internal int TotalRejections => Outcome.Rejections.Values.Sum();

        internal Dictionary<int, NodeExecutionResult> NodeResults { get; } = [];

        internal Dictionary<int, NodeExecutionResult> VerifiedResults { get; } = [];

        internal Dictionary<int, ResolveExecutionResult> ResolveResults { get; } = [];

        internal Dictionary<(int ShowId, string Embeds), ShowDto?> Details { get; } = [];

        internal void RecordBranch(NodeExecutionOutcome outcome)
        {
            if (_branchIndexes.TryGetValue(outcome.Id, out var index))
            {
                Outcome.Branches[index] = outcome;
                return;
            }

            _branchIndexes.Add(outcome.Id, Outcome.Branches.Count);
            Outcome.Branches.Add(outcome);
        }

        internal bool HasBranch(int nodeId) => _branchIndexes.ContainsKey(nodeId);

        internal int RowsOut(int nodeId)
        {
            return _branchIndexes.TryGetValue(nodeId, out var index) ? Outcome.Branches[index].RowsOut : 0;
        }

        internal void Reject(string reason)
        {
            Outcome.Rejections[reason] = Outcome.Rejections.GetValueOrDefault(reason) + 1;
        }
    }

    [SuppressMessage("Ordering", "SA1201", Justification = "Nested types come first in this codebase.")]
    private const int MaximumBrowsePages = 4;

    private static readonly JsonSerializerOptions ExecutionJsonOptions = new()
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public Task<MetadataSearchPage> ExecuteAsync(
        QueryPlan plan,
        CancellationToken cancellationToken) => ExecutePlanAsync(plan, cancellationToken);

    private void AddShow(ShowDto? show, List<ShowDto> shows, HashSet<int> showIds)
    {
        if (show is not null && show.Id > 0 && showIds.Add(show.Id))
        {
            shows.Add(show);
        }
    }

    private ShowDto? WithCastSeedReference(ShowDto? show, PersonDto person, CastCreditDto? credit)
    {
        if (show is null || credit is null)
        {
            return show;
        }

        var embedded = show.Embedded ?? new ShowEmbeddedDto();
        return show with
        {
            Embedded = embedded with
            {
                Cast =
                [.. embedded.Cast ?? [], new CastEntryDto { Person = person, Self = credit.Self, Voice = credit.Voice }]
            }
        };
    }

    private ShowDto? WithCrewSeedReference(ShowDto? show, PersonDto person, CrewCreditDto? credit)
    {
        if (show is null || credit is null)
        {
            return show;
        }

        var embedded = show.Embedded ?? new ShowEmbeddedDto();
        return show with
        {
            Embedded = embedded with
            {
                Crew = [.. embedded.Crew ?? [], new CrewEntryDto { Person = person, Type = credit.Type }]
            }
        };
    }

    private bool Matches(ShowDto show, IReadOnlyList<AkaDto> akas, IReadOnlyList<LocalFilter> filters)
    {
        return filters.All(filter => Matches(show, akas, filter));
    }

    private (bool Matches, bool RequiresCredits) EvaluateRelationships(
        ShowDto show,
        IReadOnlyList<RelationshipCriteriaResidual> residuals)
    {
        var facts = show.ToNetworkReferences();
        var requiresCredits = false;
        foreach (var residual in residuals)
        {
            var networkMatch = residual.Alternatives.Any(alternative =>
                alternative.PassThrough ||
                (alternative.MatchesNetwork && facts.Any(fact => Matches(alternative.Criteria, fact))));
            if (residual.Negate)
            {
                if (networkMatch)
                {
                    return (false, false);
                }

                requiresCredits |= residual.Alternatives.Any(alternative => alternative.MatchesPerson);
                continue;
            }

            if (networkMatch)
            {
                continue;
            }

            if (!residual.Alternatives.Any(alternative => alternative.MatchesPerson))
            {
                return (false, false);
            }

            requiresCredits = true;
        }

        return (true, requiresCredits);
    }

    private bool MatchesRelationships(ShowDto show, IReadOnlyList<RelationshipCriteriaResidual> residuals)
    {
        var facts = show.ToSearchSubjectReferences();
        return residuals.All(residual =>
        {
            var matches = residual.Alternatives.Any(alternative =>
                alternative.PassThrough || facts.Any(fact => Matches(alternative.Criteria, fact)));
            return residual.Negate ? !matches : matches;
        });
    }

    private (bool Matches, bool RequiresImages) EvaluateImages(
        ShowDto show,
        IReadOnlyList<ImageCriteriaResidual> residuals)
    {
        var hasPrimary = show.Image.ToPoster(ordinal: 0) is not null;
        var requiresImages = false;
        foreach (var residual in residuals)
        {
            var passThrough = residual.Alternatives.Any(alternative =>
                alternative.Kind == ImageCriteriaResidual.EvaluationKind.PassThrough);
            if (passThrough)
            {
                continue;
            }

            var primaryMatches = hasPrimary &&
                residual.Alternatives.Any(alternative =>
                    alternative.Kind == ImageCriteriaResidual.EvaluationKind.Presence);
            if (residual.Negate)
            {
                if (primaryMatches)
                {
                    return (false, false);
                }

                requiresImages |= residual.Alternatives.Count > 0;
                continue;
            }

            if (!primaryMatches)
            {
                requiresImages |= residual.Alternatives.Count > 0;
            }
        }

        return (true, requiresImages);
    }

    private bool MatchesImages(
        ShowDto show,
        IReadOnlyList<ImageDto> images,
        IReadOnlyList<ImageCriteriaResidual> residuals)
    {
        var mappedImages = images.ToShowImages(show.Image);
        return residuals.All(residual =>
        {
            if (residual.Negate &&
                residual.Alternatives.Any(alternative =>
                    alternative.Kind == ImageCriteriaResidual.EvaluationKind.PassThrough))
            {
                return true;
            }

            var matches = residual.Alternatives.Any(alternative =>
                alternative.Kind == ImageCriteriaResidual.EvaluationKind.PassThrough ||
                mappedImages.Any(image => Matches(alternative.Criteria, image)));
            return residual.Negate ? !matches : matches;
        });
    }

    private bool Matches(ShowDto show, IReadOnlyList<AkaDto> akas, LocalFilter filter)
    {
        var matches = filter.Kind switch
        {
            LocalFilterKind.Title => CriterionGrammar.MatchesText(filter.Operator, show.Name, filter.StringValues),
            LocalFilterKind.OriginalTitle => MatchesAnyText(filter, TitleNames(show, akas)),
            LocalFilterKind.Language => CriterionGrammar.MatchesText(
                filter.Operator,
                show.Language,
                filter.StringValues),
            LocalFilterKind.Overview => CriterionGrammar.MatchesText(
                filter.Operator,
                show.Summary.NormalizeHtmlToText(),
                filter.StringValues),
            LocalFilterKind.ProductionStatus => CriterionGrammar.MatchesText(
                filter.Operator,
                show.Status,
                filter.StringValues),
            LocalFilterKind.Source => CriterionGrammar.MatchesText(
                filter.Operator,
                show.ToSource(),
                filter.StringValues),
            LocalFilterKind.PublishDate or LocalFilterKind.Established =>
                MatchesTemporal(filter, ParseTemporal(show.Premiered)),
            LocalFilterKind.Dissolved => MatchesTemporal(filter, ParseTemporal(show.Ended)),
            LocalFilterKind.Duration => MatchesDuration(filter, show.ToRuntime()),
            LocalFilterKind.Genre => MatchesGenres(filter, show.Genres ?? []),
            LocalFilterKind.ExternalId => MatchesExternalIds(filter, ExternalIdEntries(show)),
            LocalFilterKind.AlternateText => MatchesAlternateTexts(filter, show.ToAlternateTexts(akas)),
            LocalFilterKind.Reference => MatchesReferences(filter, show.ToReferences()),
            _ => true
        };
        return filter.Negate ? !matches : matches;
    }

    private bool MatchesAnyText(LocalFilter filter, IReadOnlyList<string> candidates)
    {
        if (filter.Operator == CriteriaValueOperator.IsNull)
        {
            return candidates.Count == 0;
        }

        if (filter.Operator == CriteriaValueOperator.IsNotNull)
        {
            return candidates.Count > 0;
        }

        return candidates.Any(candidate =>
            CriterionGrammar.MatchesText(filter.Operator, candidate, filter.StringValues));
    }

    private IReadOnlyList<string> TitleNames(ShowDto show, IReadOnlyList<AkaDto> akas)
    {
        var names = new List<string>();
        if (!string.IsNullOrWhiteSpace(show.Name))
        {
            names.Add(show.Name.Trim());
        }

        names.AddRange(akas.Where(aka => !string.IsNullOrWhiteSpace(aka?.Name)).Select(aka => aka.Name!.Trim()));
        return [.. names.Distinct(StringComparer.OrdinalIgnoreCase)];
    }

    private bool MatchesTemporal(LocalFilter filter, TemporalValue? candidate)
    {
        if (filter.Operator == CriteriaValueOperator.IsNull)
        {
            return candidate is null;
        }

        if (filter.Operator == CriteriaValueOperator.IsNotNull)
        {
            return candidate is not null;
        }

        return candidate is not null && candidate.Compare(filter.Operator, filter.TemporalValues);
    }

    private TemporalValue? ParseTemporal(string? value)
    {
        var parsed = value.ToDateOnly();
        return parsed is null
            ? null
            : new TemporalValue { Value = parsed.Value, Precision = TemporalPrecision.Day };
    }

    private bool MatchesDuration(LocalFilter filter, TimeSpan? candidate)
    {
        if (filter.Operator == CriteriaValueOperator.IsNull)
        {
            return candidate is null;
        }

        if (filter.Operator == CriteriaValueOperator.IsNotNull)
        {
            return candidate is not null;
        }

        if (candidate is null || filter.DurationValues.Count == 0)
        {
            return false;
        }

        return filter.Operator switch
        {
            CriteriaValueOperator.Equals or CriteriaValueOperator.In =>
                filter.DurationValues.Contains(candidate.Value),
            CriteriaValueOperator.GreaterThan => candidate.Value > filter.DurationValues[0],
            CriteriaValueOperator.GreaterThanOrEqual => candidate.Value >= filter.DurationValues[0],
            CriteriaValueOperator.LessThan => candidate.Value < filter.DurationValues[0],
            CriteriaValueOperator.LessThanOrEqual => candidate.Value <= filter.DurationValues[0],
            _ => false
        };
    }

    private bool MatchesGenres(LocalFilter filter, IReadOnlyList<string> genres)
    {
        var entries = genres
            .Where(genre => !string.IsNullOrWhiteSpace(genre))
            .Select(genre => genre.Trim())
            .ToArray();
        return filter.GenreAlternatives.Any(alternative => entries.Any(genre =>
            alternative.Name.And.All(criterion => CriterionGrammar.Matches(criterion, genre))));
    }

    private bool MatchesExternalIds(LocalFilter filter, IReadOnlyList<(string Source, string Value)> entries)
    {
        return filter.ExternalIdAlternatives.Any(alternative => entries.Any(entry =>
            alternative.Source.And.All(criterion => CriterionGrammar.Matches(criterion, entry.Source)) &&
            alternative.Value.And.All(criterion => CriterionGrammar.Matches(criterion, entry.Value))));
    }

    private IReadOnlyList<(string Source, string Value)> ExternalIdEntries(ShowDto show)
    {
        var entries = new List<(string Source, string Value)>
        {
            (Constants.ProviderType, show.Id.ToString(CultureInfo.InvariantCulture))
        };
        if (!string.IsNullOrWhiteSpace(show.Externals?.Imdb))
        {
            entries.Add((Constants.ImdbSource, show.Externals!.Imdb!.Trim()));
        }

        if (show.Externals?.TheTvdb is { } theTvdb && theTvdb > 0)
        {
            entries.Add((Constants.TheTvdbSource, theTvdb.ToString(CultureInfo.InvariantCulture)));
        }

        if (show.Externals?.TvRage is { } tvRage && tvRage > 0)
        {
            entries.Add((Constants.TvRageSource, tvRage.ToString(CultureInfo.InvariantCulture)));
        }

        return entries;
    }

    private bool MatchesAlternateTexts(LocalFilter filter, IReadOnlyList<AlternateText> entries)
    {
        return filter.AlternateTextAlternatives.Any(alternative => entries.Any(entry =>
            alternative.Kind.And.All(criterion => CriterionGrammar.Matches(criterion, entry.Kind)) &&
            alternative.Language.And.All(criterion => CriterionGrammar.Matches(criterion, entry.Language)) &&
            alternative.Text.And.All(criterion => CriterionGrammar.Matches(criterion, entry.Text))));
    }

    private bool MatchesReferences(LocalFilter filter, IReadOnlyList<Reference> references)
    {
        return filter.ReferenceAlternatives.Any(alternative => references.Any(reference =>
            alternative.Ordinal.And.All(criterion => CriterionGrammar.Matches(criterion, reference.Ordinal)) &&
            alternative.Kind.And.All(criterion => CriterionGrammar.Matches(criterion, reference.Kind)) &&
            alternative.Url.And.All(criterion => CriterionGrammar.Matches(criterion, reference.Url)) &&
            alternative.DisplayName.And.All(criterion => CriterionGrammar.Matches(criterion, reference.DisplayName)) &&
            alternative.Language.And.All(criterion => CriterionGrammar.Matches(criterion, reference.Language))));
    }

    private bool Matches(ImageCriteria criteria, Image image)
    {
        return criteria.Type.And.All(criterion => CriterionGrammar.Matches(criterion, image.Type)) &&
            criteria.MimeType.And.All(criterion => CriterionGrammar.Matches(criterion, image.MimeType)) &&
            criteria.Url.And.All(criterion => CriterionGrammar.Matches(criterion, image.Url.ToString())) &&
            criteria.Source.And.All(criterion => CriterionGrammar.Matches(criterion, image.Source)) &&
            criteria.Language.And.All(criterion => CriterionGrammar.Matches(criterion, image.Language)) &&
            criteria.Width.And.All(criterion => CriterionGrammar.Matches(criterion, image.Width)) &&
            criteria.Height.And.All(criterion => CriterionGrammar.Matches(criterion, image.Height));
    }

    private bool Matches(RelationshipCriteria criteria, ProviderMetadataSearchSubjectReference fact)
    {
        return MatchesSetAny(criteria.Kind, fact.SubjectKind, fact.RelationshipKind) &&
            MatchesSetAny(criteria.Type, fact.RelationshipKind, fact.SubjectKind) &&
            criteria.Qualifier.And.All(criterion => CriterionGrammar.Matches(criterion, fact.RelationshipQualifier)) &&
            criteria.Subject.And.All(criterion => CriterionGrammar.Matches(criterion, fact.DisplayName)) &&
            MatchesExternalIds(criteria.ExternalIds, fact.ExternalIds);
    }

    private bool MatchesSetAny(CriteriaSet<PropertyCriterion<string>> criteria, params string?[] candidates)
    {
        var constraints = criteria.And.ToArray();
        return constraints.Length == 0 ||
            candidates.Any(candidate => constraints.All(criterion => CriterionGrammar.Matches(criterion, candidate)));
    }

    private bool MatchesExternalIds(
        CollectionCriteria<ExternalIdCriteria> criteria,
        IReadOnlyDictionary<string, string> externalIds)
    {
        return criteria.Some.All(group => group.Any(alternative => externalIds.Any(pair =>
                alternative.Source.And.All(criterion => CriterionGrammar.Matches(criterion, pair.Key)) &&
                alternative.Value.And.All(criterion => CriterionGrammar.Matches(criterion, pair.Value))))) &&
            criteria.None.All(excluded => !externalIds.Any(pair =>
                excluded.Source.And.All(criterion => CriterionGrammar.Matches(criterion, pair.Key)) &&
                excluded.Value.And.All(criterion => CriterionGrammar.Matches(criterion, pair.Value))));
    }

    private void AddResolvedPerson(
        PersonDto? person,
        IReadOnlyList<SearchSeed.PersonAlternative> alternatives,
        List<ResolvedPerson> people,
        HashSet<long> personIds)
    {
        if (person is null || person.Id <= 0 || !personIds.Add(person.Id))
        {
            return;
        }

        var matchingAlternatives = MatchingAlternatives(person, alternatives);
        if (matchingAlternatives.Count == 0)
        {
            personIds.Remove(person.Id);
            return;
        }

        people.Add(new ResolvedPerson(person, matchingAlternatives));
    }

    private void AddShows(
        IReadOnlyList<ShowDto> source,
        List<ShowDto> destination,
        HashSet<int> showIds,
        int maximumCount)
    {
        foreach (var show in source)
        {
            if (destination.Count >= maximumCount)
            {
                return;
            }

            AddShow(show, destination, showIds);
        }
    }

    private NodeExecutionStatus MergeStatus(NodeExecutionStatus current, NodeExecutionStatus next)
    {
        if (current == NodeExecutionStatus.Skipped)
        {
            return next;
        }

        if (next == NodeExecutionStatus.Skipped)
        {
            return current;
        }

        if (current == NodeExecutionStatus.Success || next == NodeExecutionStatus.Success)
        {
            return NodeExecutionStatus.Success;
        }

        if (current == NodeExecutionStatus.Failed || next == NodeExecutionStatus.Failed)
        {
            return NodeExecutionStatus.Failed;
        }

        if (current == NodeExecutionStatus.Unavailable || next == NodeExecutionStatus.Unavailable)
        {
            return NodeExecutionStatus.Unavailable;
        }

        return NodeExecutionStatus.Empty;
    }

    private NodeExecutionStatus MergeVerificationStatus(NodeExecutionStatus current, NodeExecutionStatus next)
    {
        if (current == NodeExecutionStatus.Skipped)
        {
            return next;
        }

        if (next == NodeExecutionStatus.Skipped)
        {
            return current;
        }

        if (current == NodeExecutionStatus.Failed || next == NodeExecutionStatus.Failed)
        {
            return NodeExecutionStatus.Failed;
        }

        if (current == NodeExecutionStatus.Unavailable || next == NodeExecutionStatus.Unavailable)
        {
            return NodeExecutionStatus.Unavailable;
        }

        if (current == NodeExecutionStatus.Success || next == NodeExecutionStatus.Success)
        {
            return NodeExecutionStatus.Success;
        }

        return NodeExecutionStatus.Empty;
    }

    private NodeExecutionResult StatusResult(NodeExecutionStatus status)
    {
        return status switch
        {
            NodeExecutionStatus.Failed => NodeExecutionResult.Failed,
            NodeExecutionStatus.Unavailable => NodeExecutionResult.Unavailable,
            NodeExecutionStatus.Skipped => NodeExecutionResult.Skipped,
            _ => NodeExecutionResult.Empty
        };
    }

    private string StatusName(NodeExecutionStatus status) => status.ToString().ToLowerInvariant();

    private string? StatusReason(NodeExecutionStatus status)
    {
        return status switch
        {
            NodeExecutionStatus.Empty => "the branch produced no matching candidates",
            NodeExecutionStatus.Failed => "a required upstream request failed",
            NodeExecutionStatus.Unavailable => "required verification data was unavailable",
            NodeExecutionStatus.Skipped => "the branch could not be applied",
            _ => null
        };
    }

    private IReadOnlyList<PlanNode> Children(PlanNode node)
    {
        return node switch
        {
            FilterPlanNode filter => [filter.Child],
            UnionPlanNode union => union.Children,
            IntersectPlanNode intersect => intersect.Children,
            CoalescePlanNode coalesce => coalesce.Children,
            FetchPlanNode { Resolve: { } resolve } => [resolve],
            _ => []
        };
    }

    private IReadOnlyList<string> Queries(PlanNode node)
    {
        return node switch
        {
            FetchPlanNode { Seed: SearchSeed.FuzzyShow fuzzy } => fuzzy.Terms,
            ResolvePlanNode resolve =>
            [
                .. resolve.Request.Alternatives
                    .SelectMany(alternative => alternative.Names)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
            ],
            _ => []
        };
    }

    private IReadOnlyList<string> Identities(PlanNode node)
    {
        return node switch
        {
            FetchPlanNode { Seed: SearchSeed.IdLookup lookup } =>
            [
                .. lookup.Lookups.Select(value => value.ShowId is { } showId
                    ? showId.ToString(CultureInfo.InvariantCulture)
                    : string.Concat(value.Source, ":", value.Value))
            ],
            ResolvePlanNode resolve =>
            [
                .. resolve.Request.Alternatives
                    .SelectMany(alternative => alternative.PersonIds)
                    .Distinct()
                    .Select(value => value.ToString(CultureInfo.InvariantCulture))
            ],
            _ => []
        };
    }

    private void RecordNode(
        PlanExecutionContext context,
        PlanNode node,
        NodeExecutionResult result,
        int rowsIn,
        int pages = 0,
        string? reason = null,
        int? rejected = null)
    {
        context.RecordBranch(new NodeExecutionOutcome
        {
            Id = node.Id,
            Kind = node switch
            {
                ResolvePlanNode => "resolve",
                FetchPlanNode => "fetch",
                UnionPlanNode => "union",
                FilterPlanNode => "filter",
                CoalescePlanNode => "coalesce",
                IntersectPlanNode => "intersect",
                _ => "unknown"
            },
            Status = StatusName(result.Status),
            Reason = reason ?? StatusReason(result.Status),
            Children = [.. Children(node).Select(child => child.Id)],
            Queries = Queries(node),
            Identities = Identities(node),
            RowsIn = rowsIn,
            RowsOut = result.Candidates.Count,
            Rejected = rejected ?? Math.Max(0, rowsIn - result.Candidates.Count),
            Pages = pages
        });
    }

    private void RecordSkipped(PlanExecutionContext context, PlanNode node, string reason)
    {
        if (!context.HasBranch(node.Id))
        {
            context.RecordBranch(new NodeExecutionOutcome
            {
                Id = node.Id,
                Kind = node switch
                {
                    ResolvePlanNode => "resolve",
                    FetchPlanNode => "fetch",
                    UnionPlanNode => "union",
                    FilterPlanNode => "filter",
                    CoalescePlanNode => "coalesce",
                    IntersectPlanNode => "intersect",
                    _ => "unknown"
                },
                Status = "skipped",
                Reason = reason,
                Children = [.. Children(node).Select(child => child.Id)],
                Queries = Queries(node),
                Identities = Identities(node)
            });
        }

        foreach (var child in Children(node))
        {
            RecordSkipped(context, child, reason);
        }
    }

    private void RecordUnobserved(PlanExecutionContext context, PlanNode node, string reason)
    {
        if (!context.HasBranch(node.Id))
        {
            RecordSkipped(context, node, reason);
            return;
        }

        foreach (var child in Children(node))
        {
            RecordUnobserved(context, child, reason);
        }
    }

    private int RowsOutForChildren(PlanExecutionContext context, PlanNode node)
    {
        return Children(node).Select(child => context.RowsOut(child.Id)).Sum();
    }

    private void AddWarning(PlanExecutionContext context, string warning)
    {
        if (!context.Warnings.Contains(warning))
        {
            context.Warnings.Add(warning);
        }
    }

    private bool IsExecutionFailure(Exception exception)
    {
        return exception is HttpRequestException or OperationCanceledException;
    }

    private void AddVerificationWarnings(
        PlanExecutionContext context,
        bool requiresAkas,
        bool requiresCredits,
        bool requiresImages)
    {
        if (requiresAkas)
        {
            AddWarning(context, SearchWarnings.AkasUnavailable);
        }

        if (requiresCredits)
        {
            AddWarning(context, SearchWarnings.CreditVerificationUnavailable);
        }

        if (requiresImages)
        {
            AddWarning(context, SearchWarnings.ImageVerificationUnavailable);
        }
    }

    private IReadOnlyList<SearchSeed.PersonAlternative> MatchingAlternatives(
        PersonDto person,
        IReadOnlyList<SearchSeed.PersonAlternative> alternatives)
    {
        return
        [
            .. from alternative in alternatives
            where alternative.PersonIds.Count == 0 || alternative.PersonIds.Contains(person.Id)
            where alternative.SubjectCriteria.All(criterion => CriterionGrammar.Matches(criterion, person.Name))
            select alternative
        ];
    }

    private async Task<MetadataSearchPage> ExecutePlanAsync(
        QueryPlan plan,
        CancellationToken cancellationToken)
    {
        var started = Stopwatch.StartNew();
        var requiredCount = plan.MaximumResults == int.MaxValue
            ? int.MaxValue
            : plan.MaximumResults + 1;
        var context = new PlanExecutionContext(requiredCount, plan.MaximumResults) { Progress = plan.Progress };
        if (plan.Progress is { } progress)
        {
            var nodes = new Stack<PlanNode>();
            nodes.Push(plan.Root);
            while (nodes.Count > 0)
            {
                var node = nodes.Pop();
                if (node is ResolvePlanNode resolve && progress.ResolvedSources.ContainsKey(node.Id))
                {
                    await ExecuteResolveAsync(resolve, context, cancellationToken).ConfigureAwait(false);
                }

                foreach (var child in Children(node))
                {
                    nodes.Push(child);
                }
            }

            if (progress.ValidateOnly)
            {
                return new MetadataSearchPage
                {
                    Supported = true,
                    Matches = [],
                    Warnings = context.Warnings,
                    PageInfo = new ProviderSearchPageInfo { RequestedPage = 1, HasMore = true }
                };
            }
        }

        var execution = plan.Root is FilterPlanNode filter
            ? await ExecuteVerifiedNodeAsync(
                filter.Child,
                filter,
                context,
                requiredCount,
                excludedShowIds: null,
                cancellationToken).ConfigureAwait(false)
            : await ExecuteRawNodeAsync(plan.Root, context, cancellationToken).ConfigureAwait(false);

        if (plan.Root is FilterPlanNode rootFilter)
        {
            context.RecordBranch(new NodeExecutionOutcome
            {
                Id = rootFilter.Id,
                Kind = "filter",
                Status = StatusName(execution.Status),
                Reason = StatusReason(execution.Status),
                Children = [rootFilter.Child.Id],
                Embeds = [.. context.Embeds.OrderBy(value => value, StringComparer.Ordinal)],
                RowsIn = context.FilterRowsIn,
                RowsOut = execution.Candidates.Count,
                Rejected = context.Outcome.Rejections.Values.Sum(),
                DetailFanOut = context.Outcome.DetailFanOut
            });
        }

        var matches = new List<MetadataSearchCandidate>();
        foreach (var show in execution.Candidates.Where(show => plan.Progress?.Delivered.Contains(show.Id) != true)
            .Take(plan.MaximumResults))
        {
            var candidate = show.ToMetadataSearchCandidate();
            if (candidate is not null)
            {
                matches.Add(candidate);
            }
        }

        var result = new MetadataSearchPage
        {
            Supported = true,
            Matches = matches,
            Warnings = [.. context.Warnings.Distinct()],
            PageInfo = new ProviderSearchPageInfo
            {
                RequestedPage = 1,
                HasMore = execution.Candidates.Count > plan.MaximumResults ||
                    plan.Progress is not null && (context.Incomplete || context.Warnings.Count > 0)
            }
        };
        RecordUnobserved(context, plan.Root, "branch was not required");
        started.Stop();
        context.Outcome.Status = StatusName(execution.Status);
        context.Outcome.RowsOut = result.Matches.Count;
        context.Outcome.HasMore = result.PageInfo.HasMore;
        context.Outcome.Warnings = result.Warnings;
        context.Outcome.ElapsedMilliseconds = started.Elapsed.TotalMilliseconds;
        if (logger.IsEnabled(LogLevel.Trace))
        {
            logger.LogTrace(
                "EXEC TVMaze {ExecutionJson}",
                JsonSerializer.Serialize(context.Outcome, ExecutionJsonOptions));
        }

        if (execution.Status == NodeExecutionStatus.Failed && result.Matches.Count == 0)
        {
            throw new InvalidOperationException("TVMaze search failed with no completed execution node.");
        }

        return result;
    }

    private async Task<NodeExecutionResult> ExecuteVerifiedNodeAsync(
        PlanNode node,
        FilterPlanNode filter,
        PlanExecutionContext context,
        int requiredCount,
        ISet<int>? excludedShowIds,
        CancellationToken cancellationToken)
    {
        return node switch
        {
            UnionPlanNode union => await ExecuteVerifiedUnionAsync(
                union,
                filter,
                context,
                requiredCount,
                excludedShowIds,
                cancellationToken).ConfigureAwait(false),
            CoalescePlanNode coalesce => await ExecuteVerifiedCoalesceAsync(
                coalesce,
                filter,
                context,
                requiredCount,
                excludedShowIds,
                cancellationToken).ConfigureAwait(false),
            FetchPlanNode { AnchorKind: SearchAnchorKind.Browse } browse => await ExecuteBrowseAsync(
                browse,
                filter,
                context,
                requiredCount,
                excludedShowIds,
                cancellationToken).ConfigureAwait(false),
            _ => await VerifyAsync(
                await ExecuteRawNodeAsync(node, context, cancellationToken).ConfigureAwait(false),
                filter,
                context,
                requiredCount,
                excludedShowIds,
                cancellationToken).ConfigureAwait(false)
        };
    }

    private async Task<NodeExecutionResult> ExecuteVerifiedUnionAsync(
        UnionPlanNode node,
        FilterPlanNode filter,
        PlanExecutionContext context,
        int requiredCount,
        ISet<int>? excludedShowIds,
        CancellationToken cancellationToken)
    {
        var cacheable = excludedShowIds is null || excludedShowIds.Count == 0;
        if (cacheable && context.VerifiedResults.TryGetValue(node.Id, out var cached))
        {
            return cached;
        }

        if (node.Children.Count == 0)
        {
            return NodeExecutionResult.Empty;
        }

        var shows = new List<ShowDto>();
        var showIds = excludedShowIds?.ToHashSet() ?? [];
        var status = NodeExecutionStatus.Skipped;
        for (var childIndex = 0; childIndex < node.Children.Count; childIndex++)
        {
            var child = node.Children[childIndex];
            var result = await ExecuteVerifiedNodeAsync(
                child,
                filter,
                context,
                requiredCount - shows.Count,
                showIds,
                cancellationToken).ConfigureAwait(false);
            status = MergeStatus(status, result.Status);
            AddShows(result.Candidates, shows, showIds, requiredCount);
            if (shows.Count >= requiredCount)
            {
                foreach (var skipped in node.Children.Skip(childIndex + 1))
                {
                    RecordSkipped(context, skipped, "the requested survivor count was reached");
                }

                break;
            }
        }

        var execution = shows.Count > 0 ? NodeExecutionResult.From(shows) : StatusResult(status);
        RecordNode(context, node, execution, RowsOutForChildren(context, node));
        if (cacheable)
        {
            context.VerifiedResults[node.Id] = execution;
        }

        return execution;
    }

    private async Task<NodeExecutionResult> ExecuteVerifiedCoalesceAsync(
        CoalescePlanNode node,
        FilterPlanNode filter,
        PlanExecutionContext context,
        int requiredCount,
        ISet<int>? excludedShowIds,
        CancellationToken cancellationToken)
    {
        var cacheable = excludedShowIds is null || excludedShowIds.Count == 0;
        if (cacheable && context.VerifiedResults.TryGetValue(node.Id, out var cached))
        {
            return cached;
        }

        for (var childIndex = 0; childIndex < node.Children.Count; childIndex++)
        {
            var child = node.Children[childIndex];
            var verified = await ExecuteVerifiedNodeAsync(
                child,
                filter,
                context,
                requiredCount,
                excludedShowIds,
                cancellationToken).ConfigureAwait(false);
            if (verified.Status == NodeExecutionStatus.Skipped)
            {
                continue;
            }

            if (cacheable)
            {
                context.VerifiedResults[node.Id] = verified;
            }

            foreach (var skipped in node.Children.Skip(childIndex + 1))
            {
                RecordSkipped(context, skipped, "an applicable coalesce branch completed");
            }

            RecordNode(context, node, verified, RowsOutForChildren(context, node));
            return verified;
        }

        if (cacheable)
        {
            context.VerifiedResults[node.Id] = NodeExecutionResult.Skipped;
        }

        RecordNode(context, node, NodeExecutionResult.Skipped, RowsOutForChildren(context, node));
        return NodeExecutionResult.Skipped;
    }

    private async Task<NodeExecutionResult> ExecuteRawNodeAsync(
        PlanNode node,
        PlanExecutionContext context,
        CancellationToken cancellationToken)
    {
        if (context.NodeResults.TryGetValue(node.Id, out var cached))
        {
            return cached;
        }

        NodeExecutionResult result;
        try
        {
            result = node switch
            {
                FetchPlanNode fetch => await ExecuteFetchAsync(fetch, context, cancellationToken)
                    .ConfigureAwait(false),
                UnionPlanNode union => await ExecuteRawUnionAsync(union, context, cancellationToken)
                    .ConfigureAwait(false),
                IntersectPlanNode intersect => await ExecuteIntersectAsync(intersect, context, cancellationToken)
                    .ConfigureAwait(false),
                CoalescePlanNode coalesce => await ExecuteRawCoalesceAsync(coalesce, context, cancellationToken)
                    .ConfigureAwait(false),
                FilterPlanNode filter => await ExecuteVerifiedNodeAsync(
                    filter.Child,
                    filter,
                    context,
                    context.RequiredCount,
                    excludedShowIds: null,
                    cancellationToken).ConfigureAwait(false),
                _ => NodeExecutionResult.Skipped
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException exception)
        {
            logger.LogWarning(exception, "The TVmaze plan node {NodeId} timed out.", node.Id);
            AddWarning(context, SearchWarnings.ExecutionFailed);
            result = NodeExecutionResult.Failed;
        }
        catch (HttpRequestException exception)
        {
            logger.LogWarning(exception, "The TVmaze plan node {NodeId} failed.", node.Id);
            AddWarning(context, SearchWarnings.ExecutionFailed);
            result = NodeExecutionResult.Failed;
        }

        var rowsIn = node is FetchPlanNode
            ? result.RowsIn
            : RowsOutForChildren(context, node);
        RecordNode(context, node, result, rowsIn, rejected: node is FetchPlanNode ? result.Rejections : null);
        context.NodeResults[node.Id] = result;
        return result;
    }

    private async Task<NodeExecutionResult> ExecuteFetchAsync(
        FetchPlanNode node,
        PlanExecutionContext context,
        CancellationToken cancellationToken)
    {
        return node.AnchorKind switch
        {
            SearchAnchorKind.NativeId or SearchAnchorKind.ExternalId => await ExecuteLookupFetchAsync(
                (SearchSeed.IdLookup)node.Seed,
                cancellationToken).ConfigureAwait(false),
            SearchAnchorKind.Title => await ExecuteTitleFetchAsync(
                (SearchSeed.FuzzyShow)node.Seed,
                cancellationToken).ConfigureAwait(false),
            SearchAnchorKind.Person => await ExecutePersonFetchAsync(node, context, cancellationToken)
                .ConfigureAwait(false),
            SearchAnchorKind.Browse => NodeExecutionResult.Skipped,
            _ => NodeExecutionResult.Skipped
        };
    }

    private async Task<NodeExecutionResult> ExecuteLookupFetchAsync(
        SearchSeed.IdLookup seed,
        CancellationToken cancellationToken)
    {
        var shows = new List<ShowDto>();
        var showIds = new HashSet<int>();
        var rowsIn = 0;
        foreach (var lookup in seed.Lookups)
        {
            var show = lookup.ShowId is { } showId
                ? await client.GetShowWithEmbedsAsync(showId, [], cancellationToken).ConfigureAwait(false)
                : await client.LookupShowAsync(lookup.Source, lookup.Value, cancellationToken)
                    .ConfigureAwait(false);
            if (show is not null)
            {
                rowsIn++;
            }

            AddShow(show, shows, showIds);
        }

        return NodeExecutionResult.From(shows, rowsIn);
    }

    private async Task<NodeExecutionResult> ExecuteTitleFetchAsync(
        SearchSeed.FuzzyShow seed,
        CancellationToken cancellationToken)
    {
        var shows = new List<ShowDto>();
        var showIds = new HashSet<int>();
        var rowsIn = 0;
        foreach (var term in seed.Terms)
        {
            var hits = await client.SearchShowsAsync(term, cancellationToken).ConfigureAwait(false);
            rowsIn += hits.Count;
            foreach (var hit in hits)
            {
                AddShow(hit?.Show, shows, showIds);
            }
        }

        return NodeExecutionResult.From(shows, rowsIn);
    }

    private async Task<NodeExecutionResult> ExecutePersonFetchAsync(
        FetchPlanNode node,
        PlanExecutionContext context,
        CancellationToken cancellationToken)
    {
        var resolved = node.Resolve is { } resolve
            ? await ExecuteResolveAsync(resolve, context, cancellationToken).ConfigureAwait(false)
            : ResolveExecutionResult.Empty;
        if (resolved.Status != NodeExecutionStatus.Success)
        {
            return StatusResult(resolved.Status);
        }

        var shows = new List<ShowDto>();
        var showIds = new HashSet<int>();
        var failed = false;
        var rowsIn = 0;
        foreach (var identity in resolved.People)
        {
            var alternatives = MatchingAlternatives(identity.Person, ((SearchSeed.PersonChain)node.Seed).Alternatives);
            if (alternatives.Any(alternative => alternative.IncludeCast))
            {
                try
                {
                    var credits = await client.GetPersonCastCreditsAsync(identity.Person.Id, cancellationToken)
                        .ConfigureAwait(false);
                    rowsIn += credits.Count;
                    foreach (var credit in credits)
                    {
                        AddShow(WithCastSeedReference(credit?.Embedded?.Show, identity.Person, credit), shows, showIds);
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception) when (IsExecutionFailure(exception))
                {
                    logger.LogWarning(
                        exception,
                        "The TVmaze cast-credits request failed for person {PersonId}.",
                        identity.Person.Id);
                    AddWarning(context, SearchWarnings.ExecutionFailed);
                    failed = true;
                }
            }

            var crewAlternatives = alternatives
                .Where(alternative => alternative.IncludeCrew)
                .ToArray();
            if (crewAlternatives.Length == 0)
            {
                continue;
            }

            var unrestrictedCrew = crewAlternatives.Any(alternative => alternative.CrewKinds.Count == 0);
            var crewKinds = crewAlternatives
                .SelectMany(alternative => alternative.CrewKinds)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            try
            {
                var crewCredits = await client.GetPersonCrewCreditsAsync(identity.Person.Id, cancellationToken)
                    .ConfigureAwait(false);
                rowsIn += crewCredits.Count;
                foreach (var credit in crewCredits)
                {
                    var relationshipKind = (credit?.Type).ToRelationshipKind();
                    if (!unrestrictedCrew && !crewKinds.Contains(relationshipKind))
                    {
                        continue;
                    }

                    AddShow(WithCrewSeedReference(credit?.Embedded?.Show, identity.Person, credit), shows, showIds);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception) when (IsExecutionFailure(exception))
            {
                logger.LogWarning(
                    exception,
                    "The TVmaze crew-credits request failed for person {PersonId}.",
                    identity.Person.Id);
                AddWarning(context, SearchWarnings.ExecutionFailed);
                failed = true;
            }
        }

        if (shows.Count > 0)
        {
            return NodeExecutionResult.From(shows, rowsIn);
        }

        return new NodeExecutionResult(
            failed ? NodeExecutionStatus.Failed : NodeExecutionStatus.Empty,
            [],
            rowsIn,
            rowsIn);
    }

    private async Task<ResolveExecutionResult> ExecuteResolveAsync(
        ResolvePlanNode node,
        PlanExecutionContext context,
        CancellationToken cancellationToken)
    {
        if (context.ResolveResults.TryGetValue(node.Id, out var cached))
        {
            return cached;
        }

        var people = new List<ResolvedPerson>();
        var personIds = new HashSet<long>();
        var failed = false;
        var rowsIn = 0;
        foreach (var personId in node.Request.Alternatives.SelectMany(alternative => alternative.PersonIds).Distinct())
        {
            try
            {
                var person = await client.GetPersonByIdAsync(personId, cancellationToken).ConfigureAwait(false);
                if (person is not null)
                {
                    rowsIn++;
                }

                AddResolvedPerson(person, node.Request.Alternatives, people, personIds);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception) when (IsExecutionFailure(exception))
            {
                logger.LogWarning(exception, "The TVmaze person lookup failed for person {PersonId}.", personId);
                AddWarning(context, SearchWarnings.ExecutionFailed);
                failed = true;
            }
        }

        foreach (var term in node.Request.Alternatives
            .SelectMany(alternative => alternative.Names)
            .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var hits = await client.SearchPeopleAsync(term, cancellationToken).ConfigureAwait(false);
                rowsIn += hits.Count;
                foreach (var hit in hits)
                {
                    AddResolvedPerson(hit?.Person, node.Request.Alternatives, people, personIds);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception) when (IsExecutionFailure(exception))
            {
                logger.LogWarning(exception, "The TVmaze person search failed for term {Term}.", term);
                AddWarning(context, SearchWarnings.ExecutionFailed);
                failed = true;
            }
        }

        var result = people.Count == 0
            ? failed ? ResolveExecutionResult.Failed : ResolveExecutionResult.Empty
            : new ResolveExecutionResult(NodeExecutionStatus.Success, people);
        if (context.Progress is { } progress && !failed)
        {
            var signature = JsonSerializer.Serialize(people.Select(person => person.Person.Id));
            if (progress.ResolvedSources.TryGetValue(node.Id, out var previous) && previous != signature)
            {
                throw new MetadataSearchContinuationException("TVmaze resolved people changed. Start a new search.");
            }

            progress.ResolvedSources[node.Id] = signature;
        }

        context.RecordBranch(new NodeExecutionOutcome
        {
            Id = node.Id,
            Kind = "resolve",
            Status = StatusName(result.Status),
            Reason = StatusReason(result.Status),
            Children = [.. Children(node).Select(child => child.Id)],
            Queries = Queries(node),
            Identities = Identities(node),
            RowsIn = rowsIn,
            RowsOut = people.Count,
            Rejected = Math.Max(0, rowsIn - people.Count)
        });
        context.ResolveResults[node.Id] = result;
        return result;
    }

    private async Task<NodeExecutionResult> ExecuteRawUnionAsync(
        UnionPlanNode node,
        PlanExecutionContext context,
        CancellationToken cancellationToken)
    {
        if (node.Children.Count == 0)
        {
            return NodeExecutionResult.Empty;
        }

        var shows = new List<ShowDto>();
        var showIds = new HashSet<int>();
        var status = NodeExecutionStatus.Skipped;
        foreach (var child in node.Children)
        {
            var result = await ExecuteRawNodeAsync(child, context, cancellationToken).ConfigureAwait(false);
            status = MergeStatus(status, result.Status);
            AddShows(result.Candidates, shows, showIds, int.MaxValue);
        }

        return shows.Count > 0 ? NodeExecutionResult.From(shows) : StatusResult(status);
    }

    private async Task<NodeExecutionResult> ExecuteIntersectAsync(
        IntersectPlanNode node,
        PlanExecutionContext context,
        CancellationToken cancellationToken)
    {
        if (node.Children.Count == 0)
        {
            return NodeExecutionResult.Empty;
        }

        var results = new List<NodeExecutionResult>(node.Children.Count);
        foreach (var child in node.Children)
        {
            var result = await ExecuteRawNodeAsync(child, context, cancellationToken).ConfigureAwait(false);
            if (result.Status != NodeExecutionStatus.Success)
            {
                return result;
            }

            results.Add(result);
        }

        var sharedIds = results[0].Candidates.Select(show => show.Id).ToHashSet();
        foreach (var result in results.Skip(1))
        {
            sharedIds.IntersectWith(result.Candidates.Select(show => show.Id));
        }

        var shows = results[0].Candidates.Where(show => sharedIds.Contains(show.Id)).ToArray();
        return NodeExecutionResult.From(shows);
    }

    private async Task<NodeExecutionResult> ExecuteRawCoalesceAsync(
        CoalescePlanNode node,
        PlanExecutionContext context,
        CancellationToken cancellationToken)
    {
        foreach (var child in node.Children)
        {
            var result = await ExecuteRawNodeAsync(child, context, cancellationToken).ConfigureAwait(false);
            if (result.Status != NodeExecutionStatus.Skipped)
            {
                return result;
            }
        }

        return NodeExecutionResult.Skipped;
    }

    private async Task<NodeExecutionResult> ExecuteBrowseAsync(
        FetchPlanNode node,
        FilterPlanNode filter,
        PlanExecutionContext context,
        int requiredCount,
        ISet<int>? excludedShowIds,
        CancellationToken cancellationToken)
    {
        var cacheable = excludedShowIds is null || excludedShowIds.Count == 0;
        if (cacheable && context.VerifiedResults.TryGetValue(node.Id, out var cached))
        {
            return cached;
        }

        var survivors = new List<ShowDto>();
        var showIds = excludedShowIds?.ToHashSet() ?? [];
        var status = NodeExecutionStatus.Empty;
        var pageRequests = 0;
        var rowsIn = 0;
        var rejectionsBefore = context.TotalRejections;
        var firstPage = context.Progress?.BrowsePages.GetValueOrDefault(node.Id) ?? 0;
        if (context.Progress?.ExhaustedBrowse.Contains(node.Id) == true)
        {
            return NodeExecutionResult.Empty;
        }

        var exhausted = false;
        int? retryPage = null;
        for (var page = firstPage; page < firstPage + MaximumBrowsePages && survivors.Count < requiredCount; page++)
        {
            IReadOnlyList<ShowDto>? shows;
            try
            {
                pageRequests++;
                shows = await client.GetShowsPageAsync(page, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException exception)
            {
                logger.LogWarning(exception, "The request for TVmaze show-index page {Page} timed out.", page);
                AddWarning(context, SearchWarnings.ExecutionFailed);
                status = MergeVerificationStatus(status, NodeExecutionStatus.Failed);
                break;
            }
            catch (HttpRequestException exception)
            {
                logger.LogWarning(exception, "The request for TVmaze show-index page {Page} failed.", page);
                AddWarning(context, SearchWarnings.ExecutionFailed);
                status = MergeVerificationStatus(status, NodeExecutionStatus.Failed);
                break;
            }

            if (shows is null)
            {
                exhausted = retryPage is null;
                if (exhausted)
                {
                    context.Progress?.ExhaustedBrowse.Add(node.Id);
                }

                break;
            }

            var verified = await VerifyCandidatesAsync(
                shows,
                filter,
                context,
                requiredCount - survivors.Count,
                showIds,
                cancellationToken).ConfigureAwait(false);
            rowsIn += verified.RowsIn;
            status = MergeVerificationStatus(status, verified.Status);
            AddShows(verified.Candidates, survivors, showIds, requiredCount);
            if (verified.Status is NodeExecutionStatus.Failed or NodeExecutionStatus.Unavailable)
            {
                retryPage ??= page;
            }

            if (context.Progress is { } progress)
            {
                progress.BrowsePages[node.Id] = retryPage ?? page;
            }
        }

        context.Incomplete |= !exhausted;

        var result = survivors.Count > 0
            ? new NodeExecutionResult(
                status == NodeExecutionStatus.Empty ? NodeExecutionStatus.Success : status,
                survivors,
                rowsIn,
                context.TotalRejections - rejectionsBefore)
            : StatusResult(status) with { RowsIn = rowsIn, Rejections = context.TotalRejections - rejectionsBefore };
        context.Outcome.Pages += pageRequests;
        RecordNode(context, node, result, rowsIn, pageRequests, rejected: result.Rejections);
        if (cacheable)
        {
            context.VerifiedResults[node.Id] = result;
        }

        return result;
    }

    private async Task<NodeExecutionResult> VerifyAsync(
        NodeExecutionResult source,
        FilterPlanNode filter,
        PlanExecutionContext context,
        int requiredCount,
        ISet<int>? excludedShowIds,
        CancellationToken cancellationToken)
    {
        if (source.Status != NodeExecutionStatus.Success)
        {
            return source;
        }

        return await VerifyCandidatesAsync(
            source.Candidates,
            filter,
            context,
            requiredCount,
            excludedShowIds,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<NodeExecutionResult> VerifyCandidatesAsync(
        IReadOnlyList<ShowDto> candidates,
        FilterPlanNode filter,
        PlanExecutionContext context,
        int requiredCount,
        ISet<int>? excludedShowIds,
        CancellationToken cancellationToken)
    {
        var survivors = new List<ShowDto>();
        var seenShowIds = excludedShowIds?.ToHashSet() ?? [];
        var unavailable = false;
        var failed = false;
        var rowsIn = 0;
        var rejectionsBefore = context.TotalRejections;
        foreach (var candidate in candidates)
        {
            if (context.Progress?.Delivered.Contains(candidate.Id) == true)
            {
                continue;
            }

            if (survivors.Count >= requiredCount)
            {
                break;
            }

            rowsIn++;
            context.FilterRowsIn++;
            context.Outcome.RowsIn++;
            if (candidate.Id <= 0)
            {
                context.Reject("invalid-identity");
                continue;
            }

            if (!seenShowIds.Add(candidate.Id))
            {
                context.Reject("duplicate");
                continue;
            }

            if (!Matches(candidate, akas: [], filter.HitFilters))
            {
                context.Reject("list-row");
                continue;
            }

            var relationshipEvaluation = EvaluateRelationships(candidate, filter.RelationshipResiduals);
            if (!relationshipEvaluation.Matches)
            {
                context.Reject("relationship-row");
                continue;
            }

            var imageEvaluation = EvaluateImages(candidate, filter.ImageResiduals);
            if (!imageEvaluation.Matches)
            {
                context.Reject("image-row");
                continue;
            }

            var embeds = new List<string>(4);
            if (filter.AkaFilters.Count > 0)
            {
                embeds.Add("akas");
            }

            if (relationshipEvaluation.RequiresCredits)
            {
                embeds.Add("cast");
                embeds.Add("crew");
            }

            if (imageEvaluation.RequiresImages)
            {
                embeds.Add("images");
            }

            var verified = candidate;
            if (embeds.Count > 0)
            {
                foreach (var embed in embeds)
                {
                    context.Embeds.Add(embed);
                }

                var embedKey = string.Join(",", embeds);
                try
                {
                    if (!context.Details.TryGetValue((candidate.Id, embedKey), out var hydrated))
                    {
                        context.Outcome.DetailFanOut++;
                        hydrated = await client.GetShowWithEmbedsAsync(
                            candidate.Id,
                            embeds,
                            cancellationToken).ConfigureAwait(false);
                        context.Details[(candidate.Id, embedKey)] = hydrated;
                    }

                    if (hydrated is null)
                    {
                        AddVerificationWarnings(
                            context,
                            filter.AkaFilters.Count > 0,
                            relationshipEvaluation.RequiresCredits,
                            imageEvaluation.RequiresImages);
                        unavailable = true;
                        context.Reject("detail-unavailable");
                        continue;
                    }

                    if (filter.AkaFilters.Count > 0)
                    {
                        var akas = hydrated.Embedded?.Akas;
                        if (akas is null)
                        {
                            AddWarning(context, SearchWarnings.AkasUnavailable);
                            unavailable = true;
                            context.Reject("aka-unavailable");
                            continue;
                        }

                        if (!Matches(hydrated, akas, filter.AkaFilters))
                        {
                            context.Reject("aka");
                            continue;
                        }
                    }

                    if (relationshipEvaluation.RequiresCredits)
                    {
                        if (hydrated.Embedded?.Cast is null || hydrated.Embedded.Crew is null)
                        {
                            AddWarning(context, SearchWarnings.CreditVerificationUnavailable);
                            unavailable = true;
                            context.Reject("credits-unavailable");
                            continue;
                        }

                        if (!MatchesRelationships(hydrated, filter.RelationshipResiduals))
                        {
                            context.Reject("relationship-detail");
                            continue;
                        }
                    }

                    if (imageEvaluation.RequiresImages)
                    {
                        var images = hydrated.Embedded?.Images;
                        if (images is null)
                        {
                            AddWarning(context, SearchWarnings.ImageVerificationUnavailable);
                            unavailable = true;
                            context.Reject("images-unavailable");
                            continue;
                        }

                        if (!MatchesImages(hydrated, images, filter.ImageResiduals))
                        {
                            context.Reject("image-detail");
                            continue;
                        }
                    }

                    verified = hydrated;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception) when (IsExecutionFailure(exception))
                {
                    logger.LogWarning(
                        exception,
                        "The TVmaze residual verification failed for show {ShowId}.",
                        candidate.Id);
                    AddVerificationWarnings(
                        context,
                        filter.AkaFilters.Count > 0,
                        relationshipEvaluation.RequiresCredits,
                        imageEvaluation.RequiresImages);
                    failed = true;
                    context.Reject("detail-failed");
                    continue;
                }
            }

            survivors.Add(verified);
        }

        if (survivors.Count > 0)
        {
            var status = failed
                ? NodeExecutionStatus.Failed
                : unavailable ? NodeExecutionStatus.Unavailable : NodeExecutionStatus.Success;
            return new NodeExecutionResult(status, survivors, rowsIn, context.TotalRejections - rejectionsBefore);
        }

        if (failed)
        {
            return NodeExecutionResult.Failed with
            {
                RowsIn = rowsIn,
                Rejections = context.TotalRejections - rejectionsBefore
            };
        }

        return (unavailable ? NodeExecutionResult.Unavailable : NodeExecutionResult.Empty) with
        {
            RowsIn = rowsIn,
            Rejections = context.TotalRejections - rejectionsBefore
        };
    }
}
