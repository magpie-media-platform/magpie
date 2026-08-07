using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Magpie.Extensions.Tmdb.QueryEngine;

internal static class QueryTraceExtensions
{
    private const int SchemaVersion = 1;

    private static readonly JsonSerializerOptions Options = new()
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    internal static string SerializePlan(this QueryPlan plan)
    {
        return Serialize(new Dictionary<string, object?>
        {
            ["schemaVersion"] = SchemaVersion,
            ["provider"] = Constants.ProviderType,
            ["maximumResults"] = plan.MaximumResults,
            ["warnings"] = plan.Warnings.Select(warning => warning).ToArray(),
            ["root"] = PlanNode(plan.Root)
        });
    }

    internal static string SerializeExecution(
        this ExecutionTrace trace,
        TimeSpan elapsed,
        int rowsSurviving,
        bool hasMore)
    {
        var requests = trace.Requests();
        var nodes = trace.Nodes();
        return Serialize(new Dictionary<string, object?>
        {
            ["schemaVersion"] = SchemaVersion,
            ["provider"] = Constants.ProviderType,
            ["elapsedMilliseconds"] = elapsed.TotalMilliseconds,
            ["summary"] = new Dictionary<string, object?>
            {
                ["requests"] = requests.Count,
                ["pagesScanned"] = requests.Count(request =>
                    request.Target.Contains("page=", StringComparison.Ordinal)),
                ["payloadBytes"] = requests.Sum(request => (long)request.PayloadBytes),
                ["detailFanOut"] = trace.DetailFanOut,
                ["rowsFetched"] = requests.Sum(request => request.RowCount ?? 0),
                ["rowsSurviving"] = rowsSurviving,
                ["prunedNodes"] = nodes.Count(node => node.Status == "skipped"),
                ["hasMore"] = hasMore,
                ["bounded"] = requests.Any(request => request.Target.Contains("page=500", StringComparison.Ordinal))
            },
            ["callGraph"] = new Dictionary<string, object?>
            {
                ["nodes"] = nodes.Select(NodeOutcome).ToList(),
                ["requests"] = requests.Select(Request).ToList()
            }
        });
    }

    internal static IReadOnlyList<PlanNode> Children(this PlanNode node)
    {
        return node switch
        {
            FetchPlanNode { Resolve: { } resolve } => [resolve],
            FilterPlanNode filter => [filter.Child],
            UnionPlanNode union => union.Children,
            IntersectPlanNode intersect => intersect.Children,
            CoalescePlanNode coalesce => coalesce.Children,
            _ => []
        };
    }

    private static string Serialize(object value) => JsonSerializer.Serialize(value, Options);

    private static object NodeOutcome(PlanNodeOutcome node)
    {
        return new Dictionary<string, object?>
        {
            ["nodeId"] = node.NodeId,
            ["status"] = node.Status,
            ["reason"] = node.Reason,
            ["rowsIn"] = node.RowsIn,
            ["rowsOut"] = node.RowsOut,
            ["rejections"] = node.Rejections
        };
    }

    private static object Request(RequestTrace request)
    {
        return new Dictionary<string, object?>
        {
            ["id"] = request.Id,
            ["planNodeId"] = request.PlanNodeId,
            ["role"] = request.Role,
            ["target"] = request.Target,
            ["status"] = request.Status,
            ["rowCount"] = request.RowCount,
            ["payloadBytes"] = request.PayloadBytes,
            ["elapsedMilliseconds"] = request.ElapsedMilliseconds
        };
    }

    private static object PlanNode(PlanNode node)
    {
        var result = new Dictionary<string, object?>
        {
            ["id"] = node.Id,
            ["kind"] = NodeKind(node),
            ["precondition"] = new Dictionary<string, object?>
            {
                ["kind"] = node.Precondition.Kind,
                ["nodeId"] = node.Precondition.NodeId
            },
            ["contributes"] = node.Contributes
        };
        if (node is ResolvePlanNode resolve)
        {
            result["subjectBindings"] = resolve.SubjectBindings.Select(binding => binding.Key).ToList();
            result["resolveGenres"] = resolve.GenreFilters.Count > 0;
            result["resolveImageBase"] = resolve.ResolveImageBase;
        }
        else if (node is FetchPlanNode fetch)
        {
            result["route"] = fetch.Route;
            result["anchorKind"] = fetch.AnchorKind;
            result["endpoint"] = Endpoint(fetch);
            result["searchTerm"] = fetch.OrdinarySearchTerm;
            result["subjectBindings"] = fetch.SubjectBindings.Select(binding => binding.Key).ToList();
            result["subjectRoles"] = fetch.SubjectRoles;
            result["subjectRoleSelections"] = fetch.SubjectRoleSelections.Select(selection =>
                new Dictionary<string, object?>
                {
                    ["subjectBinding"] = selection.SubjectBindingKey,
                    ["roles"] = selection.Roles
                }).ToList();
            result["discoverParameters"] = fetch.DiscoverParameters.Select(parameter =>
                new Dictionary<string, object?>
                {
                    ["name"] = parameter.Name,
                    ["combination"] = parameter.Combination,
                    ["subjectBindings"] = parameter.SubjectBindingKeys,
                    ["genreClauses"] = parameter.GenreFilters.Count
                }).ToList();
            result["ignoreSubjectFilters"] = fetch.IgnoreSubjectFilters;
            result["pushedFilters"] = fetch.PushedFilters;
            result["requiresLogicalStream"] = fetch.RequiresLogicalStream;
        }
        else if (node is FilterPlanNode filter)
        {
            result["residualPredicates"] = filter.ResidualPredicates;
        }
        else if (node is UnionPlanNode union)
        {
            result["expandsResolvedSubjectIds"] = union.ExpandsResolvedSubjectIds;
        }

        result["children"] = Children(node).Select(PlanNode).ToList();
        return result;
    }

    private static string NodeKind(PlanNode node)
    {
        return node switch
        {
            ResolvePlanNode => "resolve",
            FetchPlanNode => "fetch",
            UnionPlanNode => "union",
            IntersectPlanNode => "intersect",
            FilterPlanNode => "filter",
            CoalescePlanNode => "coalesce",
            _ => throw new ArgumentOutOfRangeException(nameof(node))
        };
    }

    private static string Endpoint(FetchPlanNode fetch)
    {
        return fetch.AnchorKind switch
        {
            QueryAnchorKind.Search => fetch.Route == ContentRoute.Movie ? "/search/movie" : "/search/tv",
            QueryAnchorKind.Discover => fetch.Route == ContentRoute.Movie ? "/discover/movie" : "/discover/tv",
            QueryAnchorKind.PersonCredits => fetch.Route == ContentRoute.Movie
                ? "/person/{id}/movie_credits"
                : "/person/{id}/tv_credits",
            QueryAnchorKind.ContentIds => "/{route}/{id} or /find/{externalId}",
            _ => throw new ArgumentOutOfRangeException(nameof(fetch))
        };
    }
}
