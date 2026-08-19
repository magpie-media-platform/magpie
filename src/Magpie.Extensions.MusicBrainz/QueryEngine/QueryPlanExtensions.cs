using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Magpie.Extensions.MusicBrainz.QueryEngine;

internal static class QueryPlanExtensions
{
    private const int SchemaVersion = 1;

    private static readonly JsonSerializerOptions Options = new()
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    internal static string SerializePlan(this QueryPlan plan)
    {
        return Serialize(new Dictionary<string, object?>
        {
            ["schemaVersion"] = SchemaVersion,
            ["provider"] = Constants.ProviderType,
            ["fingerprint"] = plan.Fingerprint,
            ["requestedPage"] = plan.RequestedPage,
            ["maxResults"] = plan.MaxResults,
            ["warnings"] = plan.Warnings.Select(warning => warning).ToArray(),
            ["criteria"] = plan.TraceProvenance.Criteria.Select(criterion => new Dictionary<string, object?>
            {
                ["source"] = criterion.Source,
                ["disposition"] = criterion.FinalDisposition
            }).ToList(),
            ["root"] = plan.Root is null ? null : PlanNode(plan.Root)
        });
    }

    internal static string NodeKind(this PlanNode node)
    {
        return node switch
        {
            ResolvePlanNode => "resolve",
            FetchPlanNode => "fetch",
            UnionPlanNode => "union",
            IntersectPlanNode => "intersect",
            FilterPlanNode => "filter",
            _ => throw new ArgumentOutOfRangeException(nameof(node))
        };
    }

    private static string Serialize(object value) => JsonSerializer.Serialize(value, Options);

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
            result["entity"] = resolve.Composition.RelatedEntity;
            result["resolution"] = resolve.Composition.Resolution;
            result["constraint"] = resolve.Composition.Constraint;
            result["hasRelatedMbid"] = resolve.Composition.RelatedMbid is not null;
            result["hasRelatedName"] = resolve.Composition.RelatedName is not null;
        }
        else if (node is FetchPlanNode fetch)
        {
            result["target"] = fetch.Target;
            result["accessMode"] = fetch.AccessMode;
            result["anchorKind"] = fetch.AnchorKind;
            result["combine"] = fetch.CombineMode;
            result["hasDirectValue"] = fetch.DirectValue is not null;
            result["hasOrdinaryTerm"] = fetch.OrdinaryTerm is not null;
            result["entityKind"] = fetch.EntityKind;
            result["filters"] = fetch.Filters.Select(Filter).ToList();
            result["lookupCapabilities"] = fetch.LookupCapabilities;
            result["releaseIndexComposition"] = fetch.UsesReleaseIndexComposition;
        }
        else if (node is FilterPlanNode filter)
        {
            result["filters"] = filter.Filters.Select(Filter).ToList();
            result["residuals"] = filter.Residuals.Select(residual => new Dictionary<string, object?>
            {
                ["kind"] = residual.Kind,
                ["need"] = residual.Need,
                ["negate"] = residual.Negate,
                ["alternativeCount"] = residual.Alternatives.Count
            }).ToList();
        }

        result["children"] = node.Children().Select(PlanNode).ToList();
        return result;
    }

    private static object Filter(QueryFilter filter)
    {
        return new Dictionary<string, object?>
        {
            ["kind"] = filter.Kind,
            ["field"] = filter.RemoteField,
            ["operator"] = filter.Operator,
            ["negate"] = filter.Negate,
            ["combine"] = filter.CombineMode,
            ["valueCount"] = filter.StringValues.Count + filter.TemporalValues.Count +
                filter.DurationMilliseconds.Count +
                filter.IdentityValues.Count +
                filter.RelationshipValues.Count +
                filter.LabelValues.Count
        };
    }
}
