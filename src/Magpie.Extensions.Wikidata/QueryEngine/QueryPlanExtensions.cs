using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

using Magpie.Models.Enums;

namespace Magpie.Extensions.Wikidata.QueryEngine;

internal static class QueryPlanExtensions
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    internal static QueryPlan PrepareDiagnostics(this QueryPlan plan)
    {
        if (!string.IsNullOrWhiteSpace(plan.Fingerprint) &&
            !string.IsNullOrWhiteSpace(plan.Boundedness))
        {
            return plan;
        }

        var prepared = plan with
        {
            Boundedness = Boundedness(plan),
            Fingerprint = string.Empty
        };
        return prepared with { Fingerprint = Fingerprint(JsonSerializer.Serialize(prepared, JsonOptions)) };
    }

    internal static string SerializePlan(this QueryPlan plan)
    {
        var serverFilters =
            from filter in plan.Filters
            group filter by filter.Kind into filters
            orderby filters.Key
            select new
            {
                Kind = filters.Key,
                Count = filters.Count()
            };
        var residuals =
            from residual in plan.ResidualFilters
            group residual by new { residual.Kind, residual.Hydration } into filters
            orderby filters.Key.Kind, filters.Key.Hydration
            select new
            {
                filters.Key.Kind,
                filters.Key.Hydration,
                Count = filters.Count()
            };
        var dispositions =
            from disposition in plan.CriterionDispositions
            select new
            {
                disposition.Source,
                disposition.Disposition
            };
        return JsonSerializer.Serialize(
            new
            {
                plan.Fingerprint,
                plan.EntityKind,
                TranslationMode = plan.Strategy,
                plan.Boundedness,
                ServerFilterCount = plan.Filters.Count,
                ServerFilters = serverFilters,
                ResidualCount = plan.ResidualFilters.Count,
                Residuals = residuals,
                CriterionDispositionCount = plan.CriterionDispositions.Count,
                CriterionDispositions = dispositions,
                plan.Page,
                MaxResults = plan.MaximumResults,
                plan.UsesLocalPaging,
                plan.ProjectCandidateDetails
            },
            JsonOptions);
    }

    internal static int RelationshipSeedFilterIndex(this QueryPlan plan)
    {
        if (!plan.ResidualFilters.Any(value => value.Kind == ResidualFilterKind.Relationship) ||
            plan.Filters.Any(value => value.Kind != QueryFilterKind.Relationship &&
                !value.Negate &&
                value.Operator is not (CriteriaValueOperator.IsNull or CriteriaValueOperator.IsNotNull)))
        {
            return -1;
        }

        var relationships =
            from entry in plan.Filters.Select((value, index) => (value, index))
            where entry.value.Kind == QueryFilterKind.Relationship && !entry.value.Negate
            select entry.index;
        return relationships.DefaultIfEmpty(-1).First();
    }

    private static string Boundedness(QueryPlan plan)
    {
        if (plan.Strategy == QueryStrategy.Unsupported)
        {
            return "refused";
        }

        if (plan.Strategy != QueryStrategy.Empty)
        {
            return "bounded";
        }

        return plan.CriterionDispositions.Any(value =>
            value.Disposition == CriterionDispositionKind.ProvablyUnsatisfiable)
                ? "provablyEmpty"
                : "refused";
    }

    private static string Fingerprint(string canonicalValue)
    {
        using var sha256 = SHA256.Create();
        return BitConverter.ToString(sha256.ComputeHash(Encoding.UTF8.GetBytes(canonicalValue)))
            .Replace("-", string.Empty)
            .ToLowerInvariant();
    }
}
