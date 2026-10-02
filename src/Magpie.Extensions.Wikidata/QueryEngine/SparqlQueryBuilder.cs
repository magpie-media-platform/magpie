using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

using Magpie.Extensions.Wikidata.Mapping;
using Magpie.Models.Enums;
using Magpie.Models.Filtering;

namespace Magpie.Extensions.Wikidata.QueryEngine;

internal static class SparqlQueryBuilder
{
    public const int PageSize = 20;

    public const int CandidateWindow = 50;

    public const int CandidateFetchLimit = CandidateWindow + 1;

    public const int HierarchyLimit = 2500;

    public const char GenreSeparator = '\u001f';

    private const string GenreSeparatorEscape = "\\u001F";

    private static readonly TimeSpan SearchTimeout = TimeSpan.FromSeconds(12);

    private static readonly TimeSpan IdentityTimeout = TimeSpan.FromSeconds(12);

    private static readonly TimeSpan HierarchyTimeout = TimeSpan.FromSeconds(30);

    public static SparqlRequest BuildSearch(QueryPlan plan, string language, int candidateOffset = 0)
    {
        var labelLanguages = LabelLanguages(language);
        var titleTerms = plan.Strategy == QueryStrategy.TitleSearch ? TitleTerms(plan) : [];
        var offset = plan.UsesLocalPaging
            ? plan.Strategy == QueryStrategy.TitleSearch ? 0 : candidateOffset
            : (plan.Page - 1) * plan.MaximumResults;
        var titleCandidateCount = Math.Min(
            CandidateFetchLimit,
            Math.Max(0, plan.TitleCandidateQids.Count - candidateOffset));
        var limit = plan.Strategy == QueryStrategy.TitleSearch
            ? CandidateFetchLimit * titleTerms.Length + titleCandidateCount
            : plan.UsesLocalPaging
                ? CandidateFetchLimit
                : plan.MaximumResults + 1;

        var builder = new StringBuilder();
        AppendPrefixes(builder);
        builder.Append("SELECT ?item ?type ?itemLabel ?itemDescription");
        if (plan.Strategy == QueryStrategy.TitleSearch)
        {
            builder.Append(" ?titleWindowExceeded");
        }

        builder.AppendLine();
        builder.AppendLine("       (MIN(?searchOrdinalValue) AS ?searchOrdinal)");
        builder.AppendLine("WHERE {");
        builder.AppendLine("  {");
        builder.AppendLine(plan.Strategy == QueryStrategy.TitleSearch
            ? "    SELECT (SAMPLE(?item) AS ?candidateItem) (SAMPLE(?typeValue) AS ?type)" +
                " (MIN(?ordinalValue) AS ?searchOrdinalValue)"
            : "    SELECT ?item (SAMPLE(?typeValue) AS ?type) (MIN(?ordinalValue) AS ?searchOrdinalValue)");
        if (plan.Strategy == QueryStrategy.TitleSearch)
        {
            builder.AppendLine("           (MAX(?titleWindowExceededValue) AS ?titleWindowExceeded)");
        }

        builder.AppendLine("    WHERE {");
        if (plan.Strategy == QueryStrategy.TitleSearch)
        {
            AppendTitleSources(builder, plan, titleTerms, candidateOffset, "      ");
            builder.AppendLine("      OPTIONAL {");
        }

        AppendTypeGate(builder, plan.EntityKind, "      ", "?typeValue");
        for (var index = 0; index < plan.Filters.Count; index++)
        {
            AppendFilter(builder, plan.Filters[index], index, labelLanguages);
        }

        AppendSortDateBinding(builder, plan.Filters);
        if (plan.Strategy == QueryStrategy.TitleSearch)
        {
            builder.AppendLine("        BIND(1 AS ?titleCandidateEligible)");
            builder.AppendLine("      }");
            builder.Append("      FILTER(?ordinalValue >= ");
            builder.Append(CandidateWindow.ToString(CultureInfo.InvariantCulture));
            builder.AppendLine(" || BOUND(?titleCandidateEligible))");
            builder.Append("      BIND(IF(?ordinalValue >= ");
            builder.Append(CandidateWindow.ToString(CultureInfo.InvariantCulture));
            builder.AppendLine(", \"overflow\", STR(?item)) AS ?titleResultGroup)");
            builder.Append("      BIND(IF(?ordinalValue >= ");
            builder.Append(CandidateWindow.ToString(CultureInfo.InvariantCulture));
            builder.AppendLine(", 1, 0) AS ?titleWindowExceededValue)");
        }

        builder.AppendLine("    }");
        builder.AppendLine(plan.Strategy == QueryStrategy.TitleSearch
            ? "    GROUP BY ?titleResultGroup"
            : "    GROUP BY ?item");
        builder.Append(plan.Strategy == QueryStrategy.TitleSearch
            ? "    ORDER BY ASC(MIN(?ordinalValue)) ASC(?titleResultGroup)\n"
            : "    ORDER BY ASC(MIN(?sortDateValue)) ASC(?item)\n");
        builder.Append("    LIMIT ");
        builder.AppendLine(limit.ToString(CultureInfo.InvariantCulture));
        builder.Append("    OFFSET ");
        builder.AppendLine(offset.ToString(CultureInfo.InvariantCulture));
        builder.AppendLine("  }");
        if (plan.Strategy == QueryStrategy.TitleSearch)
        {
            builder.AppendLine("  BIND(?candidateItem AS ?item)");
        }

        builder.AppendLine("  SERVICE wikibase:label {");
        builder.Append("    bd:serviceParam wikibase:language ");
        builder.Append(SparqlString(labelLanguages));
        builder.AppendLine(".");
        builder.AppendLine("    ?item rdfs:label ?itemLabel.");
        builder.AppendLine("    ?item schema:description ?itemDescription.");
        builder.AppendLine("  }");
        builder.AppendLine("}");
        builder.Append("GROUP BY ?item ?type ?itemLabel ?itemDescription");
        if (plan.Strategy == QueryStrategy.TitleSearch)
        {
            builder.Append(" ?titleWindowExceeded");
        }

        builder.AppendLine();
        builder.AppendLine("ORDER BY ASC(?searchOrdinal) ASC(?item)");
        return Request(builder.ToString(), SearchTimeout);
    }

    public static SparqlRequest BuildCandidateProjection(
        EntityKind kind,
        IReadOnlyList<string> qids,
        string language)
    {
        if (qids.Count == 0)
        {
            throw new ArgumentException("At least one Wikidata QID is required.", nameof(qids));
        }

        foreach (var qid in qids)
        {
            ValidateQid(qid);
        }

        var labelLanguages = LabelLanguages(language);
        var projectionDateProperty = kind == EntityKind.Movie
            ? Constants.PublicationDatePropertyId
            : Constants.SeriesStartPropertyId;
        var builder = new StringBuilder();
        AppendPrefixes(builder);
        builder.AppendLine("SELECT ?item");
        builder.AppendLine("       (MIN(?publishDateProjection) AS ?publishDate)");
        builder.AppendLine("       (MIN(?languageCodeValue) AS ?languageCode)");
        builder.AppendLine("       (MIN(?posterFilenameValue) AS ?posterFilename)");
        builder.AppendLine("       (MIN(?imageFilenameValue) AS ?imageFilename)");
        builder.AppendLine("       (MIN(?durationSecondsValue) AS ?durationSeconds)");
        builder.AppendLine("       (GROUP_CONCAT(DISTINCT ?genreLabelValue; SEPARATOR=\"" +
            GenreSeparatorEscape +
            "\") AS ?genres)");
        if (kind == EntityKind.Show)
        {
            builder.AppendLine("       (MIN(?endDateProjection) AS ?endDate)");
            builder.AppendLine("       (MAX(?hasEndDateValue) AS ?hasEndDate)");
        }

        builder.AppendLine("WHERE {");
        builder.Append("  VALUES ?item { ");
        builder.Append(string.Join(" ", qids.Distinct(StringComparer.Ordinal).Select(qid => "wd:" + qid)));
        builder.AppendLine(" }");
        builder.AppendLine("  OPTIONAL {");
        AppendCandidateDateProjection(
            builder,
            "    ",
            "?item",
            projectionDateProperty,
            "Projection",
            "publishDateProjection");
        builder.AppendLine("  }");
        if (kind == EntityKind.Show)
        {
            builder.AppendLine("  OPTIONAL {");
            AppendCandidateDateProjection(
                builder,
                "    ",
                "?item",
                Constants.SeriesEndPropertyId,
                "EndProjection",
                "endDateProjection");
            builder.AppendLine("  }");
            builder.Append("  OPTIONAL { ?item wdt:");
            builder.Append(Constants.SeriesEndPropertyId);
            builder.AppendLine(" ?endDateExists. BIND(1 AS ?hasEndDateValue) }");
        }

        builder.AppendLine("  OPTIONAL {");
        AppendDurationValuePattern(builder, "    ", "Projection");
        AppendDurationSecondsBinding(builder, "    ");
        builder.AppendLine("  }");
        builder.AppendLine("  OPTIONAL {");
        builder.AppendLine("    ?item wdt:P136 ?genreItem.");
        builder.AppendLine("    ?genreItem rdfs:label ?genreLabelValue.");
        builder.Append("    FILTER(LANG(?genreLabelValue) IN (");
        builder.Append(string.Join(", ", labelLanguages.Split(',').Select(SparqlString)));
        builder.AppendLine("))");
        builder.AppendLine("  }");
        builder.AppendLine(
            "  OPTIONAL { ?item wdt:P364 ?languageItem. " +
            "?languageItem wdt:P279* ?languageAncestor. ?languageAncestor wdt:P218 ?languageCodeValue. }");
        builder.AppendLine("  OPTIONAL { ?item wdt:P3383 ?posterFilenameValue. }");
        builder.AppendLine("  OPTIONAL { ?item wdt:P18 ?imageFilenameValue. }");
        builder.AppendLine("}");
        builder.AppendLine("GROUP BY ?item");
        builder.AppendLine("ORDER BY ASC(?item)");
        return Request(builder.ToString(), SearchTimeout);
    }

    public static SparqlRequest BuildExternalIdentity(
        EntityKind kind,
        string propertyId,
        string value)
    {
        ValidatePropertyId(propertyId);
        var builder = new StringBuilder();
        AppendPrefixes(builder);
        builder.AppendLine("SELECT DISTINCT ?item");
        builder.AppendLine("WHERE {");
        builder.Append("  ?item wdt:");
        builder.Append(propertyId);
        builder.Append(' ');
        builder.Append(SparqlString(value));
        builder.AppendLine(".");
        AppendTypeGate(builder, kind, "  ", "?type");
        builder.AppendLine("}");
        builder.AppendLine("LIMIT 2");
        return Request(builder.ToString(), IdentityTimeout);
    }

    public static SparqlRequest BuildRelationshipSeed(
        EntityKind kind,
        QueryFilter filter,
        int offset)
    {
        if (filter.Kind != QueryFilterKind.Relationship || filter.Negate)
        {
            throw new InvalidOperationException("A positive relationship filter is required for a relationship seed.");
        }

        var builder = new StringBuilder();
        AppendPrefixes(builder);
        builder.AppendLine("SELECT DISTINCT ?item");
        builder.AppendLine("WHERE {");
        AppendCompositeFilter(builder, filter, 0);
        AppendTypeGate(builder, kind, "  ", "?typeValue");
        builder.AppendLine("}");
        builder.AppendLine("ORDER BY ?item");
        builder.Append("LIMIT ");
        builder.AppendLine(CandidateFetchLimit.ToString(CultureInfo.InvariantCulture));
        builder.Append("OFFSET ");
        builder.AppendLine(offset.ToString(CultureInfo.InvariantCulture));
        return Request(builder.ToString(), SearchTimeout);
    }

    public static SparqlRequest BuildCoordinateIdentity(
        EntityKind kind,
        string parentQid,
        int ordinal,
        string? showQid)
    {
        ValidateQid(parentQid);
        if (ordinal <= 0)
        {
            throw new InvalidOperationException("Wikidata hierarchy ordinals must be positive.");
        }

        var builder = new StringBuilder();
        AppendPrefixes(builder);
        builder.AppendLine("SELECT DISTINCT ?item");
        builder.AppendLine("WHERE {");
        if (kind == EntityKind.Season)
        {
            builder.AppendLine("  ?item wdt:P31 wd:Q3464665; p:P179 ?parentStatement.");
            builder.Append("  ?parentStatement a wikibase:BestRank; ps:P179 wd:");
            builder.Append(parentQid);
            builder.Append("; pq:P1545 ");
            builder.Append(SparqlString(ordinal.ToString(CultureInfo.InvariantCulture)));
            builder.AppendLine(".");
        }
        else if (kind == EntityKind.Episode)
        {
            if (showQid is null)
            {
                throw new InvalidOperationException("Wikidata episode coordinates require a show QID.");
            }

            ValidateQid(showQid);
            builder.Append("  ?item wdt:P31 wd:Q21191270; wdt:P179 wd:");
            builder.Append(showQid);
            builder.AppendLine("; p:P4908 ?parentStatement.");
            builder.Append("  ?parentStatement a wikibase:BestRank; ps:P4908 wd:");
            builder.Append(parentQid);
            builder.Append("; pq:P1545 ");
            builder.Append(SparqlString(ordinal.ToString(CultureInfo.InvariantCulture)));
            builder.AppendLine(".");
        }
        else
        {
            throw new InvalidOperationException("Wikidata coordinate identity supports seasons and episodes only.");
        }

        builder.AppendLine("}");
        builder.AppendLine("LIMIT 2");
        return Request(builder.ToString(), IdentityTimeout);
    }

    public static SparqlRequest BuildHierarchy(
        EntityKind childKind,
        string parentQid,
        string? showQid,
        string language)
    {
        ValidateQid(parentQid);
        if (childKind == EntityKind.Episode)
        {
            if (showQid is null)
            {
                throw new InvalidOperationException("Wikidata episode hierarchy requests require a show QID.");
            }

            ValidateQid(showQid);
        }
        else if (childKind != EntityKind.Season)
        {
            throw new InvalidOperationException("Wikidata hierarchy requests support seasons and episodes only.");
        }

        var labelLanguages = LabelLanguages(language);
        var builder = new StringBuilder();
        AppendPrefixes(builder);
        builder.AppendLine("SELECT ?child ?childLabel ?childDescription ?ordinal");
        builder.AppendLine("       (MIN(?publishDateProjection) AS ?publishDate)");
        builder.AppendLine("       (MIN(?imageValue) AS ?imageFilename)");
        builder.AppendLine("       (MIN(?imdbValue) AS ?imdbId)");
        builder.AppendLine("       (MIN(?tmdbValue) AS ?tmdbId)");
        builder.AppendLine("       (MIN(?tvdbValue) AS ?tvdbId)");
        builder.AppendLine("WHERE {");
        builder.AppendLine("  {");
        builder.AppendLine("    SELECT ?child (MIN(?ordinalValue) AS ?ordinal)");
        builder.AppendLine("    WHERE {");
        if (childKind == EntityKind.Season)
        {
            builder.AppendLine("      ?child wdt:P31 wd:Q3464665; p:P179 ?parentStatement.");
            builder.Append("      ?parentStatement a wikibase:BestRank; ps:P179 wd:");
            builder.Append(parentQid);
            builder.AppendLine("; pq:P1545 ?rawOrdinal.");
        }
        else
        {
            builder.Append("      ?child wdt:P31 wd:Q21191270; wdt:P179 wd:");
            builder.Append(showQid);
            builder.AppendLine("; p:P4908 ?parentStatement.");
            builder.Append("      ?parentStatement a wikibase:BestRank; ps:P4908 wd:");
            builder.Append(parentQid);
            builder.AppendLine("; pq:P1545 ?rawOrdinal.");
        }

        builder.AppendLine("      FILTER(REGEX(STR(?rawOrdinal), \"^[1-9][0-9]*$\"))");
        builder.AppendLine("      BIND(xsd:integer(?rawOrdinal) AS ?ordinalValue)");
        builder.AppendLine("    }");
        builder.AppendLine("    GROUP BY ?child");
        builder.AppendLine("    HAVING(COUNT(DISTINCT ?ordinalValue) = 1)");
        builder.AppendLine("    ORDER BY ASC(?ordinal) ASC(?child)");
        builder.Append("    LIMIT ");
        builder.AppendLine((HierarchyLimit + 1).ToString(CultureInfo.InvariantCulture));
        builder.AppendLine("  }");
        builder.AppendLine("  OPTIONAL {");
        AppendDateValuePattern(
            builder,
            "    ",
            "?child",
            childKind == EntityKind.Season ? "P580" : "P577",
            "Projection",
            projectAs: "publishDateProjection");
        builder.AppendLine("  }");
        builder.AppendLine("  OPTIONAL { ?child wdt:P18 ?imageValue. }");
        builder.AppendLine("  OPTIONAL { ?child wdt:P345 ?imdbValue. }");
        builder.Append("  OPTIONAL { ?child wdt:");
        builder.Append(childKind == EntityKind.Season ? "P12558" : "P12559");
        builder.AppendLine(" ?tmdbValue. }");
        builder.Append("  OPTIONAL { ?child wdt:");
        builder.Append(childKind == EntityKind.Season ? "P12397" : "P7043");
        builder.AppendLine(" ?tvdbValue. }");
        builder.AppendLine("  SERVICE wikibase:label {");
        builder.Append("    bd:serviceParam wikibase:language ");
        builder.Append(SparqlString(labelLanguages));
        builder.AppendLine(".");
        builder.AppendLine("    ?child rdfs:label ?childLabel.");
        builder.AppendLine("    ?child schema:description ?childDescription.");
        builder.AppendLine("  }");
        builder.AppendLine("}");
        builder.AppendLine("GROUP BY ?child ?childLabel ?childDescription ?ordinal");
        builder.AppendLine("ORDER BY ASC(?ordinal) ASC(?child)");
        return Request(builder.ToString(), HierarchyTimeout);
    }

    private static string LabelLanguages(string language)
    {
        var normalized = language.ToPrimaryLanguage()
            ?? throw new InvalidOperationException("Invalid Wikidata language code.");
        return string.Equals(normalized, Settings.FallbackLanguage, StringComparison.Ordinal)
            ? normalized
            : normalized + "," + Settings.FallbackLanguage;
    }

    private static void AppendPrefixes(StringBuilder builder)
    {
        builder.AppendLine("PREFIX bd: <http://www.bigdata.com/rdf#>");
        builder.AppendLine("PREFIX mwapi: <https://www.mediawiki.org/ontology#API/>");
        builder.AppendLine("PREFIX p: <http://www.wikidata.org/prop/>");
        builder.AppendLine("PREFIX pq: <http://www.wikidata.org/prop/qualifier/>");
        builder.AppendLine("PREFIX ps: <http://www.wikidata.org/prop/statement/>");
        builder.AppendLine("PREFIX psv: <http://www.wikidata.org/prop/statement/value/>");
        builder.AppendLine("PREFIX rdfs: <http://www.w3.org/2000/01/rdf-schema#>");
        builder.AppendLine("PREFIX schema: <http://schema.org/>");
        builder.AppendLine("PREFIX wd: <http://www.wikidata.org/entity/>");
        builder.AppendLine("PREFIX wdt: <http://www.wikidata.org/prop/direct/>");
        builder.AppendLine("PREFIX wikibase: <http://wikiba.se/ontology#>");
        builder.AppendLine("PREFIX xsd: <http://www.w3.org/2001/XMLSchema#>");
    }

    private static void AppendTitleSources(
        StringBuilder builder,
        QueryPlan plan,
        IReadOnlyList<string> terms,
        int candidateOffset,
        string indent)
    {
        var typeExpression = "haswbstatement:" +
            string.Join(
                "|",
                AcceptedTypes(plan.EntityKind).Select(qid => "P31=" + qid));
        var titleCandidates = plan.TitleCandidateQids
            .Skip(candidateOffset)
            .Take(CandidateFetchLimit)
            .ToArray();
        if (titleCandidates.Length == 0 && terms.Count == 1)
        {
            AppendTitleServiceBranch(builder, terms[0] + " " + typeExpression, candidateOffset, indent);
            return;
        }

        var first = true;
        if (titleCandidates.Length > 0)
        {
            builder.Append(indent);
            builder.AppendLine("{");
            AppendTitleCandidateValues(builder, titleCandidates, indent + "  ");
            first = false;
        }

        for (var index = 0; index < terms.Count; index++)
        {
            builder.Append(indent);
            builder.AppendLine(first ? "{" : "} UNION {");
            AppendTitleServiceBranch(
                builder,
                terms[index] + " " + typeExpression,
                candidateOffset,
                indent + "  ");
            first = false;
        }

        builder.Append(indent);
        builder.AppendLine("}");
    }

    private static void AppendTitleCandidateValues(
        StringBuilder builder,
        IReadOnlyList<string> qids,
        string indent)
    {
        builder.Append(indent);
        builder.AppendLine("VALUES (?item ?ordinalValue) {");
        for (var index = 0; index < qids.Count; index++)
        {
            ValidateQid(qids[index]);
            builder.Append(indent);
            builder.Append("  (wd:");
            builder.Append(qids[index]);
            builder.Append(' ');
            builder.Append(index.ToString(CultureInfo.InvariantCulture));
            builder.AppendLine(")");
        }

        builder.Append(indent);
        builder.AppendLine("}");
    }

    private static void AppendTitleServiceBranch(
        StringBuilder builder,
        string searchText,
        int offset,
        string indent)
    {
        builder.Append(indent);
        builder.AppendLine("SERVICE wikibase:mwapi {");
        builder.Append(indent);
        builder.AppendLine("  bd:serviceParam wikibase:endpoint \"www.wikidata.org\";");
        builder.Append(indent);
        builder.AppendLine("                  wikibase:api \"Search\";");
        builder.Append(indent);
        builder.Append("                  mwapi:srsearch ");
        builder.Append(SparqlString(searchText));
        builder.AppendLine(";");
        builder.Append(indent);
        builder.AppendLine("                  mwapi:srwhat \"text\";");
        builder.Append(indent);
        builder.Append("                  mwapi:srlimit \"");
        builder.Append(CandidateFetchLimit.ToString(CultureInfo.InvariantCulture));
        builder.AppendLine("\";");
        if (offset > 0)
        {
            builder.Append(indent);
            builder.Append("                  mwapi:sroffset \"");
            builder.Append(offset.ToString(CultureInfo.InvariantCulture));
            builder.AppendLine("\";");
        }

        builder.Append(indent);
        builder.Append("                  wikibase:limit ");
        builder.Append(CandidateFetchLimit.ToString(CultureInfo.InvariantCulture));
        builder.AppendLine(" .");
        builder.Append(indent);
        builder.AppendLine("  ?item wikibase:apiOutputItem mwapi:title.");
        builder.Append(indent);
        builder.AppendLine("  ?ordinalValue wikibase:apiOrdinal true.");
        builder.Append(indent);
        builder.AppendLine("}");
    }

    private static string[] TitleTerms(QueryPlan plan)
    {
        if (plan.OrdinaryTerm is not null)
        {
            return ["inlabel:\"" + EscapeCirrus(plan.OrdinaryTerm) + "\""];
        }

        var titleFilter = plan.Filters.First(filter =>
            filter.Kind == QueryFilterKind.Title && !filter.Negate);
        return [.. titleFilter.Values.Select(value => "inlabel:\"" + EscapeCirrus(value) + "\"")];
    }

    private static void AppendTypeGate(
        StringBuilder builder,
        EntityKind kind,
        string indent,
        string variable)
    {
        var accepted = AcceptedTypes(kind);
        if (accepted.Count == 0)
        {
            return;
        }

        builder.Append(indent);
        builder.Append("VALUES ");
        builder.Append(variable);
        builder.Append(" { ");
        builder.Append(string.Join(" ", accepted.Select(qid => "wd:" + qid)));
        builder.AppendLine(" }");
        builder.Append(indent);
        builder.Append("?item wdt:P31 ");
        builder.Append(variable);
        builder.AppendLine(".");
    }

    private static void AppendFilter(
        StringBuilder builder,
        QueryFilter filter,
        int index,
        string labelLanguages)
    {
        switch (filter.Kind)
        {
            case QueryFilterKind.Title:
                AppendTitleFilter(builder, filter, index, labelLanguages);
                break;
            case QueryFilterKind.OriginalTitle:
                AppendOriginalTitleFilter(builder, filter, index);
                break;
            case QueryFilterKind.Overview:
                AppendOverviewFilter(builder, filter, index, labelLanguages);
                break;
            case QueryFilterKind.PublishDate:
                AppendDateFilter(builder, filter, index, PlannedPropertyId(filter));
                break;
            case QueryFilterKind.Duration:
                AppendDurationFilter(builder, filter, index);
                break;
            case QueryFilterKind.ProductionStatus:
                AppendProductionStatusFilter(builder, filter, index);
                break;
            case QueryFilterKind.Established:
            case QueryFilterKind.Dissolved:
            case QueryFilterKind.BirthDate:
            case QueryFilterKind.DeathDate:
                AppendDateFilter(builder, filter, index, PlannedPropertyId(filter));
                break;
            case QueryFilterKind.Language:
                AppendLanguageFilter(builder, filter, index);
                break;
            case QueryFilterKind.Genre:
            case QueryFilterKind.ExternalId:
            case QueryFilterKind.Relationship:
            case QueryFilterKind.ContentRating:
            case QueryFilterKind.Award:
            case QueryFilterKind.Image:
            case QueryFilterKind.Reference:
                AppendCompositeFilter(builder, filter, index);
                break;
        }
    }

    private static void AppendTitleFilter(
        StringBuilder builder,
        QueryFilter filter,
        int index,
        string labelLanguages)
    {
        if (filter.Operator == CriteriaValueOperator.ContainsWord)
        {
            return;
        }

        var variable = "?titleFilter" + index.ToString(CultureInfo.InvariantCulture);
        if (filter.Negate)
        {
            builder.AppendLine("      FILTER NOT EXISTS {");
            builder.Append("        ?item rdfs:label ");
            builder.Append(variable);
            builder.AppendLine(".");
            builder.Append("        FILTER(LCASE(LANG(");
            builder.Append(variable);
            builder.Append(")) IN (");
            builder.Append(string.Join(", ", labelLanguages.Split(',').Select(SparqlString)));
            builder.AppendLine("))");
            builder.Append("        FILTER(");
            builder.Append(TextExpression("LCASE(STR(" + variable + "))", filter));
            builder.AppendLine(")");
            builder.AppendLine("      }");
            return;
        }

        builder.Append("      ?item rdfs:label ");
        builder.Append(variable);
        builder.AppendLine(".");
        builder.Append("      FILTER(LCASE(LANG(");
        builder.Append(variable);
        builder.Append(")) IN (");
        builder.Append(string.Join(", ", labelLanguages.Split(',').Select(SparqlString)));
        builder.AppendLine("))");
        builder.Append("      FILTER(");
        builder.Append(TextExpression("LCASE(STR(" + variable + "))", filter));
        builder.AppendLine(")");
    }

    private static void AppendOriginalTitleFilter(StringBuilder builder, QueryFilter filter, int index)
    {
        var variable = "?originalTitle" + index.ToString(CultureInfo.InvariantCulture);
        var pattern = "?item wdt:" + Constants.OriginalTitlePropertyId + " " + variable + ".";
        if (filter.Operator is CriteriaValueOperator.IsNull or CriteriaValueOperator.IsNotNull)
        {
            builder.Append("      FILTER ");
            if (filter.Operator == CriteriaValueOperator.IsNull)
            {
                builder.Append("NOT ");
            }

            builder.Append("EXISTS { ");
            builder.Append(pattern);
            builder.AppendLine(" }");
            return;
        }

        if (filter.Negate)
        {
            builder.AppendLine("      FILTER NOT EXISTS {");
            builder.Append("        ");
            builder.AppendLine(pattern);
            builder.Append("        FILTER(");
            builder.Append(TextExpression("LCASE(STR(" + variable + "))", filter));
            builder.AppendLine(")");
            builder.AppendLine("      }");
            return;
        }

        builder.Append("      ");
        builder.AppendLine(pattern);
        builder.Append("      FILTER(");
        builder.Append(TextExpression("LCASE(STR(" + variable + "))", filter));
        builder.AppendLine(")");
    }

    private static void AppendOverviewFilter(
        StringBuilder builder,
        QueryFilter filter,
        int index,
        string labelLanguages)
    {
        var variable = "?overview" + index.ToString(CultureInfo.InvariantCulture);
        var languageFilter = "FILTER(LCASE(LANG(" +
            variable +
            ")) IN (" +
            string.Join(", ", labelLanguages.Split(',').Select(SparqlString)) +
            "))";
        if (filter.Operator is CriteriaValueOperator.IsNull or CriteriaValueOperator.IsNotNull)
        {
            builder.Append("      FILTER ");
            if (filter.Operator == CriteriaValueOperator.IsNull)
            {
                builder.Append("NOT ");
            }

            builder.AppendLine("EXISTS {");
            builder.Append("        ?item schema:description ");
            builder.Append(variable);
            builder.AppendLine(".");
            builder.Append("        ");
            builder.AppendLine(languageFilter);
            builder.AppendLine("      }");
            return;
        }

        if (filter.Negate)
        {
            builder.AppendLine("      FILTER NOT EXISTS {");
            builder.Append("        ?item schema:description ");
            builder.Append(variable);
            builder.AppendLine(".");
            builder.Append("        ");
            builder.AppendLine(languageFilter);
            builder.Append("        FILTER(");
            builder.Append(TextExpression("LCASE(STR(" + variable + "))", filter));
            builder.AppendLine(")");
            builder.AppendLine("      }");
            return;
        }

        builder.Append("      ?item schema:description ");
        builder.Append(variable);
        builder.AppendLine(".");
        builder.Append("      ");
        builder.AppendLine(languageFilter);
        builder.Append("      FILTER(");
        builder.Append(TextExpression("LCASE(STR(" + variable + "))", filter));
        builder.AppendLine(")");
    }

    private static void AppendDurationFilter(StringBuilder builder, QueryFilter filter, int index)
    {
        var suffix = index.ToString(CultureInfo.InvariantCulture);
        if (filter.Operator is CriteriaValueOperator.IsNull or CriteriaValueOperator.IsNotNull)
        {
            builder.Append("      FILTER ");
            if (filter.Operator == CriteriaValueOperator.IsNull)
            {
                builder.Append("NOT ");
            }

            builder.AppendLine("EXISTS {");
            AppendDurationValuePattern(builder, "        ", suffix);
            builder.AppendLine("      }");
            return;
        }

        if (filter.Negate)
        {
            builder.AppendLine("      FILTER NOT EXISTS {");
            AppendDurationValuePattern(builder, "        ", suffix);
            builder.Append("        FILTER(");
            builder.Append(DurationExpression(suffix, filter));
            builder.AppendLine(")");
            builder.AppendLine("      }");
            return;
        }

        AppendDurationValuePattern(builder, "      ", suffix);
        builder.Append("      FILTER(");
        builder.Append(DurationExpression(suffix, filter));
        builder.AppendLine(")");
    }

    private static void AppendDurationValuePattern(StringBuilder builder, string indent, string suffix)
    {
        var statement = "?durationStatement" + suffix;
        var node = "?durationNode" + suffix;
        builder.Append(indent);
        builder.Append("?item p:");
        builder.Append(Constants.DurationPropertyId);
        builder.Append(' ');
        builder.Append(statement);
        builder.AppendLine(".");
        builder.Append(indent);
        builder.Append(statement);
        builder.Append(" a wikibase:BestRank; psv:");
        builder.Append(Constants.DurationPropertyId);
        builder.Append(' ');
        builder.Append(node);
        builder.AppendLine(".");
        builder.Append(indent);
        builder.Append(node);
        builder.Append(" wikibase:quantityAmount ?durationAmount");
        builder.Append(suffix);
        builder.Append("; wikibase:quantityUnit ?durationUnit");
        builder.Append(suffix);
        builder.AppendLine(".");
    }

    private static void AppendDurationSecondsBinding(StringBuilder builder, string indent)
    {
        builder.Append(indent);
        builder.Append("BIND(IF(?durationUnitProjection = wd:");
        builder.Append(Constants.SecondUnitQid);
        builder.Append(", ?durationAmountProjection, IF(?durationUnitProjection = wd:");
        builder.Append(Constants.MinuteUnitQid);
        builder.AppendLine(", ?durationAmountProjection * 60,");
        builder.Append(indent);
        builder.Append("  IF(?durationUnitProjection = wd:");
        builder.Append(Constants.HourUnitQid);
        builder.AppendLine(", ?durationAmountProjection * 3600, 0))) AS ?durationSecondsValue)");
        builder.Append(indent);
        builder.AppendLine("FILTER(?durationSecondsValue > 0)");
    }

    private static string DurationExpression(string suffix, QueryFilter filter)
    {
        var amount = "?durationAmount" + suffix;
        var unit = "?durationUnit" + suffix;
        if (filter.Operator is CriteriaValueOperator.Equals or CriteriaValueOperator.In)
        {
            return string.Join(
                " || ",
                filter.DurationValues.Select(value =>
                    "(" + DurationComparison(amount, unit, "=", value) + ")"));
        }

        var comparison = filter.Operator switch
        {
            CriteriaValueOperator.GreaterThan => ">",
            CriteriaValueOperator.GreaterThanOrEqual => ">=",
            CriteriaValueOperator.LessThan => "<",
            CriteriaValueOperator.LessThanOrEqual => "<=",
            _ => "="
        };
        return DurationComparison(amount, unit, comparison, filter.DurationValues[0]);
    }

    private static string DurationComparison(string amount, string unit, string comparison, TimeSpan value) =>
        "(" +
        unit +
        " = wd:" +
        Constants.SecondUnitQid +
        " && " +
        amount +
        " " +
        comparison +
        " " +
        DurationLiteral(value, TimeSpan.TicksPerSecond) +
        ")" +
        " || (" +
        unit +
        " = wd:" +
        Constants.MinuteUnitQid +
        " && " +
        amount +
        " " +
        comparison +
        " " +
        DurationLiteral(value, TimeSpan.TicksPerMinute) +
        ")" +
        " || (" +
        unit +
        " = wd:" +
        Constants.HourUnitQid +
        " && " +
        amount +
        " " +
        comparison +
        " " +
        DurationLiteral(value, TimeSpan.TicksPerHour) +
        ")";

    private static string DurationLiteral(TimeSpan value, long ticksPerUnit) =>
        ((decimal)value.Ticks / ticksPerUnit).ToString(CultureInfo.InvariantCulture);

    private static void AppendProductionStatusFilter(StringBuilder builder, QueryFilter filter, int index)
    {
        var pattern = "?item wdt:" +
            PlannedPropertyId(filter) +
            " ?productionStatus" +
            index.ToString(CultureInfo.InvariantCulture) +
            ".";
        var ended = filter.Operator switch
        {
            CriteriaValueOperator.IsNotNull => true,
            CriteriaValueOperator.IsNull => false,
            _ => !filter.Negate
        };
        builder.Append(ended ? "      FILTER EXISTS { " : "      FILTER NOT EXISTS { ");
        builder.Append(pattern);
        builder.AppendLine(" }");
    }

    private static void AppendDateFilter(
        StringBuilder builder,
        QueryFilter filter,
        int index,
        string propertyId)
    {
        var suffix = index.ToString(CultureInfo.InvariantCulture);
        if (filter.Operator is CriteriaValueOperator.IsNull or CriteriaValueOperator.IsNotNull)
        {
            builder.Append("      FILTER ");
            if (filter.Operator == CriteriaValueOperator.IsNull)
            {
                builder.Append("NOT ");
            }

            builder.AppendLine("EXISTS {");
            AppendDateValuePattern(builder, "        ", "?item", propertyId, suffix, projectAs: null);
            builder.AppendLine("      }");
            return;
        }

        var variable = "?dateValue" + suffix;
        var precisionVariable = "?datePrecision" + suffix;
        if (filter.Negate)
        {
            builder.AppendLine("      FILTER NOT EXISTS {");
            AppendDateValuePattern(builder, "        ", "?item", propertyId, suffix, projectAs: null);
            builder.Append("        FILTER(");
            builder.Append(TemporalExpression(variable, precisionVariable, filter));
            builder.AppendLine(")");
            builder.AppendLine("      }");
        }
        else
        {
            AppendDateValuePattern(builder, "      ", "?item", propertyId, suffix, projectAs: null);
            builder.Append("      FILTER(");
            builder.Append(TemporalExpression(variable, precisionVariable, filter));
            builder.AppendLine(")");
        }
    }

    private static void AppendSortDateBinding(
        StringBuilder builder,
        IReadOnlyList<QueryFilter> filters)
    {
        var variables = (from entry in filters.Select((filter, index) => (filter, index))
                         where entry.filter.Kind == QueryFilterKind.PublishDate &&
                             !entry.filter.Negate &&
                             entry.filter.Operator is not (
                                 CriteriaValueOperator.IsNull or CriteriaValueOperator.IsNotNull)
                         select "?dateValue" + entry.index.ToString(CultureInfo.InvariantCulture))
            .ToArray();
        if (variables.Length == 0)
        {
            return;
        }

        builder.Append("      BIND(");
        if (variables.Length > 1)
        {
            builder.Append("COALESCE(");
        }

        builder.Append(string.Join(", ", variables));
        if (variables.Length > 1)
        {
            builder.Append(')');
        }

        builder.AppendLine(" AS ?sortDateValue)");
    }

    private static void AppendLanguageFilter(StringBuilder builder, QueryFilter filter, int index)
    {
        var suffix = index.ToString(CultureInfo.InvariantCulture);
        if (filter.Operator is CriteriaValueOperator.IsNull or CriteriaValueOperator.IsNotNull)
        {
            builder.Append("      FILTER ");
            if (filter.Operator == CriteriaValueOperator.IsNull)
            {
                builder.Append("NOT ");
            }

            builder.Append("EXISTS { ?item wdt:P364 ?languageItem");
            builder.Append(suffix);
            builder.Append(". ?languageItem");
            builder.Append(suffix);
            builder.Append(" wdt:P279* ?languageCodeAncestor");
            builder.Append(suffix);
            builder.Append(". ?languageCodeAncestor");
            builder.Append(suffix);
            builder.Append(" wdt:P218 ?languageCode");
            builder.Append(suffix);
            builder.AppendLine(". }");
            return;
        }

        var qids = filter.LanguageQids;
        var codes = filter.Values;
        foreach (var qid in qids)
        {
            ValidateQid(qid);
        }

        builder.Append(filter.Negate ? "      FILTER NOT EXISTS {\n" : "      {\n");
        builder.AppendLine("        {");
        var branches = new List<string>();
        if (qids.Count > 0)
        {
            branches.Add("          VALUES ?languageItem" +
                index.ToString(CultureInfo.InvariantCulture) +
                " { " +
                string.Join(" ", qids.Select(qid => "wd:" + qid)) +
                " }\n" +
                "          ?item wdt:P364 ?languageItem" +
                index.ToString(CultureInfo.InvariantCulture) +
                ".");
        }

        if (codes.Count > 0)
        {
            branches.Add("          ?item wdt:P364 ?languageCodeItem" +
                suffix +
                ".\n" +
                "          ?languageCodeItem" +
                suffix +
                " wdt:P279* ?languageCodeAncestor" +
                suffix +
                ".\n" +
                "          ?languageCodeAncestor" +
                suffix +
                " wdt:P218 ?languageCode" +
                suffix +
                ".\n" +
                "          FILTER(LCASE(STR(?languageCode" +
                suffix +
                ")) IN (" +
                string.Join(", ", codes.Select(SparqlString)) +
                "))");
        }

        builder.Append(string.Join("\n        } UNION {\n", branches));
        builder.AppendLine();
        builder.AppendLine("        }");
        builder.AppendLine("      }");
    }

    private static string PlannedPropertyId(QueryFilter filter)
    {
        if (filter.PropertyId is null)
        {
            throw new InvalidOperationException("A planned Wikidata property id is required for this filter.");
        }

        ValidatePropertyId(filter.PropertyId);
        return filter.PropertyId;
    }

    private static void AppendCompositeFilter(StringBuilder builder, QueryFilter filter, int index)
    {
        builder.Append(filter.Negate ? "      FILTER NOT EXISTS {\n" : "      {\n");
        for (var alternativeIndex = 0; alternativeIndex < filter.Alternatives.Count; alternativeIndex++)
        {
            if (alternativeIndex > 0)
            {
                builder.AppendLine("        } UNION {");
            }
            else
            {
                builder.AppendLine("        {");
            }

            AppendAlternative(builder, filter.Kind, filter.Alternatives[alternativeIndex], index, alternativeIndex);
        }

        builder.AppendLine("        }");
        builder.AppendLine("      }");
    }

    private static void AppendAlternative(
        StringBuilder builder,
        QueryFilterKind kind,
        SubjectFilter alternative,
        int filterIndex,
        int alternativeIndex)
    {
        ValidateAlternative(alternative);
        var suffix = filterIndex.ToString(CultureInfo.InvariantCulture) +
            "_" +
            alternativeIndex.ToString(CultureInfo.InvariantCulture);
        if (kind == QueryFilterKind.ExternalId)
        {
            var itemBranches = new List<string>();
            if (alternative.Qids.Count > 0)
            {
                itemBranches.Add("          VALUES ?item { " +
                    string.Join(" ", alternative.Qids.Select(qid => "wd:" + qid)) +
                    " }");
            }

            itemBranches.AddRange(alternative.ExternalIds.Select(external =>
                "          ?item wdt:" + external.PropertyId + " " + SparqlString(external.Value) + "."));
            builder.Append(string.Join("\n        } UNION {\n", itemBranches));
            builder.AppendLine();
            return;
        }

        var subject = kind == QueryFilterKind.Relationship ? "?subject" + suffix : "?value" + suffix;
        if (kind == QueryFilterKind.Relationship)
        {
            builder.Append("          VALUES ?relationshipProperty");
            builder.Append(suffix);
            builder.Append(" { ");
            builder.Append(string.Join(" ", alternative.PropertyIds.Select(property => "wdt:" + property)));
            builder.AppendLine(" }");
            builder.Append("          ?item ?relationshipProperty");
            builder.Append(suffix);
            builder.Append(' ');
            builder.Append(subject);
            builder.AppendLine(".");
        }
        else if (kind == QueryFilterKind.Genre)
        {
            builder.Append("          ?item wdt:P136 ");
            builder.Append(subject);
            builder.AppendLine(".");
        }
        else if (kind is QueryFilterKind.ContentRating
            or QueryFilterKind.Award
            or QueryFilterKind.Image
            or QueryFilterKind.Reference)
        {
            var propertyIds = CollectionPropertyIds(kind, alternative);
            foreach (var propertyId in propertyIds)
            {
                ValidatePropertyId(propertyId);
            }

            builder.Append("          VALUES ?collectionProperty");
            builder.Append(suffix);
            builder.Append(" { ");
            builder.Append(string.Join(" ", propertyIds.Select(property => "wdt:" + property)));
            builder.AppendLine(" }");
            builder.Append("          ?item ?collectionProperty");
            builder.Append(suffix);
            builder.Append(' ');
            builder.Append(subject);
            builder.AppendLine(".");
        }

        var selectorBranches = new List<string>();
        if (alternative.Qids.Count > 0)
        {
            selectorBranches.Add("          VALUES " +
                subject +
                " { " +
                string.Join(" ", alternative.Qids.Select(qid => "wd:" + qid)) +
                " }");
        }

        if (alternative.Names.Count > 0)
        {
            if (kind is QueryFilterKind.Relationship
                or QueryFilterKind.Genre
                or QueryFilterKind.ContentRating
                or QueryFilterKind.Award)
            {
                foreach (var name in alternative.Names)
                {
                    selectorBranches.Add(EntityNameService(subject, name));
                }
            }
            else
            {
                selectorBranches.Add("          " +
                    subject +
                    " rdfs:label ?name" +
                    suffix +
                    ".\n" +
                    "          FILTER(LCASE(STR(?name" +
                    suffix +
                    ")) IN (" +
                    string.Join(", ", alternative.Names.Select(name => SparqlString(name.ToLowerInvariant()))) +
                    "))");
            }
        }

        if (alternative.UriSchemes.Count > 0)
        {
            var uriExpression = "LCASE(STR(" + subject + "))";
            builder.Append("          FILTER(");
            builder.Append(string.Join(
                " || ",
                alternative.UriSchemes.Select(scheme =>
                    "STRSTARTS(" + uriExpression + ", " + SparqlString(scheme.ToLowerInvariant() + ":") + ")")));
            builder.AppendLine(")");
        }

        if (alternative.UriValues.Count > 0)
        {
            builder.Append("          FILTER(LCASE(STR(");
            builder.Append(subject);
            builder.Append(")) IN (");
            builder.Append(string.Join(
                ", ",
                alternative.UriValues.Select(value =>
                    SparqlString(value.ToLowerInvariant()))));
            builder.AppendLine("))");
        }

        if (selectorBranches.Count > 0)
        {
            builder.AppendLine("          {");
            builder.Append(string.Join("\n          } UNION {\n", selectorBranches));
            builder.AppendLine();
            builder.AppendLine("          }");
        }

        if (alternative.ExternalIds.Count > 0)
        {
            var branches = alternative.ExternalIds.Select(external =>
                "            " +
                (kind == QueryFilterKind.ExternalId ? "?item" : subject) +
                " wdt:" +
                external.PropertyId +
                " " +
                SparqlString(external.Value) +
                ".").ToArray();
            builder.AppendLine("          {");
            builder.Append(string.Join("\n          } UNION {\n", branches));
            builder.AppendLine();
            builder.AppendLine("          }");
        }

        foreach (var group in alternative.ExternalIdGroups)
        {
            var branches = new List<string>();
            if (group.Qids.Count > 0)
            {
                branches.Add("            VALUES " +
                    subject +
                    " { " +
                    string.Join(" ", group.Qids.Select(qid => "wd:" + qid)) +
                    " }");
            }

            branches.AddRange(group.ExternalIds.Select(external =>
                "            " +
                subject +
                " wdt:" +
                external.PropertyId +
                " " +
                SparqlString(external.Value) +
                "."));
            builder.AppendLine("          {");
            builder.Append(string.Join("\n          } UNION {\n", branches));
            builder.AppendLine();
            builder.AppendLine("          }");
        }
    }

    private static IReadOnlyList<string> CollectionPropertyIds(
        QueryFilterKind kind,
        SubjectFilter alternative)
    {
        if (alternative.PropertyIds.Count > 0)
        {
            return alternative.PropertyIds;
        }

        return kind switch
        {
            QueryFilterKind.ContentRating =>
                [.. Constants.RatingSystems.Select(system => system.PropertyId)],
            _ => throw new InvalidOperationException("A planned Wikidata property id is required for this filter.")
        };
    }

    private static void ValidateAlternative(SubjectFilter alternative)
    {
        foreach (var propertyId in alternative.PropertyIds)
        {
            ValidatePropertyId(propertyId);
        }

        foreach (var qid in alternative.Qids)
        {
            ValidateQid(qid);
        }

        foreach (var external in alternative.ExternalIds)
        {
            ValidatePropertyId(external.PropertyId);
        }

        foreach (var group in alternative.ExternalIdGroups)
        {
            foreach (var qid in group.Qids)
            {
                ValidateQid(qid);
            }

            foreach (var external in group.ExternalIds)
            {
                ValidatePropertyId(external.PropertyId);
            }
        }
    }

    private static string EntityNameService(string subject, string name) =>
        "          SERVICE wikibase:mwapi {\n" +
        "            bd:serviceParam wikibase:endpoint \"www.wikidata.org\";\n" +
        "                            wikibase:api \"Search\";\n" +
        "                            mwapi:srsearch " +
        SparqlString("inlabel:\"" + EscapeCirrus(name) + "\"") +
        ";\n" +
        "                            mwapi:srwhat \"text\";\n" +
        "                            mwapi:srlimit \"5\";\n" +
        "                            wikibase:limit 5 .\n" +
        "            " +
        subject +
        " wikibase:apiOutputItem mwapi:title.\n" +
        "          }";

    private static string TextExpression(string expression, QueryFilter filter)
    {
        var values = filter.Values.Select(value => value.ToLowerInvariant()).ToArray();
        return filter.Operator switch
        {
            CriteriaValueOperator.Equals when values.Length == 1 => expression + " = " + SparqlString(values[0]),
            CriteriaValueOperator.Equals or CriteriaValueOperator.In =>
                expression + " IN (" + string.Join(", ", values.Select(SparqlString)) + ")",
            CriteriaValueOperator.ContainsString => AnyTextExpression(
                values,
                value => "CONTAINS(" + expression + ", " + SparqlString(value) + ")"),
            CriteriaValueOperator.StartsWithString => AnyTextExpression(
                values,
                value => "STRSTARTS(" + expression + ", " + SparqlString(value) + ")"),
            CriteriaValueOperator.EndsWithString => AnyTextExpression(
                values,
                value => "STRENDS(" + expression + ", " + SparqlString(value) + ")"),
            _ => "true"
        };
    }

    private static string AnyTextExpression(
        IReadOnlyList<string> values,
        Func<string, string> expression)
    {
        var alternatives = values.Select(expression).ToArray();
        return alternatives.Length == 1
            ? alternatives[0]
            : "(" + string.Join(" || ", alternatives) + ")";
    }

    private static string TemporalExpression(
        string variable,
        string precisionVariable,
        QueryFilter filter)
    {
        var requested = filter.TemporalValues;
        if (filter.Operator is CriteriaValueOperator.Equals or CriteriaValueOperator.In)
        {
            return string.Join(
                " || ",
                requested.Select(value =>
                    "(" +
                    variable +
                    " < " +
                    SparqlDateTime(value.PeriodEndExclusive()) +
                    " && !" +
                    TemporalPeriodEndsBefore(variable, precisionVariable, value.PeriodStart()) +
                    ")"));
        }

        var first = requested[0];
        return filter.Operator switch
        {
            CriteriaValueOperator.GreaterThan => variable + " >= " + SparqlDateTime(first.PeriodEndExclusive()),
            CriteriaValueOperator.GreaterThanOrEqual => variable + " >= " + SparqlDateTime(first.PeriodStart()),
            CriteriaValueOperator.LessThan => TemporalPeriodEndsBefore(
                variable,
                precisionVariable,
                first.PeriodStart()),
            CriteriaValueOperator.LessThanOrEqual => variable + " < " + SparqlDateTime(first.PeriodEndExclusive()),
            _ => "true"
        };
    }

    private static string TemporalPeriodEndsBefore(
        string variable,
        string precisionVariable,
        DateTime requestedStart)
    {
        var year = requestedStart.Year.ToString(CultureInfo.InvariantCulture);
        var month = requestedStart.Month.ToString(CultureInfo.InvariantCulture);
        var day = requestedStart.Day.ToString(CultureInfo.InvariantCulture);
        var earlierYear = "YEAR(" + variable + ") < " + year;
        var earlierMonth = "(" +
            earlierYear +
            " || (YEAR(" +
            variable +
            ") = " +
            year +
            " && MONTH(" +
            variable +
            ") < " +
            month +
            "))";
        var earlierDay = "(" +
            earlierMonth +
            " || (YEAR(" +
            variable +
            ") = " +
            year +
            " && MONTH(" +
            variable +
            ") = " +
            month +
            " && DAY(" +
            variable +
            ") < " +
            day +
            "))";
        return "((" +
            precisionVariable +
            " = 9 && " +
            earlierYear +
            ") || (" +
            precisionVariable +
            " = 10 && " +
            earlierMonth +
            ") || (" +
            precisionVariable +
            " = 11 && " +
            earlierDay +
            ") || (" +
            precisionVariable +
            " > 11 && " +
            variable +
            " < " +
            SparqlDateTime(requestedStart) +
            "))";
    }

    private static void AppendDateValuePattern(
        StringBuilder builder,
        string indent,
        string subject,
        string propertyId,
        string suffix,
        string? projectAs)
    {
        var statement = "?dateStatement" + suffix;
        var node = "?dateNode" + suffix;
        var value = "?dateValue" + suffix;
        var precision = "?datePrecision" + suffix;
        var timezone = "?dateTimezone" + suffix;
        var calendar = "?dateCalendar" + suffix;
        builder.Append(indent);
        builder.Append(subject);
        builder.Append(" p:");
        builder.Append(propertyId);
        builder.Append(' ');
        builder.Append(statement);
        builder.AppendLine(".");
        builder.Append(indent);
        builder.Append(statement);
        builder.Append(" a wikibase:BestRank; psv:");
        builder.Append(propertyId);
        builder.Append(' ');
        builder.Append(node);
        builder.AppendLine(".");
        builder.Append(indent);
        builder.Append(node);
        builder.Append(" wikibase:timeValue ");
        builder.Append(value);
        builder.Append("; wikibase:timePrecision ");
        builder.Append(precision);
        builder.Append("; wikibase:timeTimezone ");
        builder.Append(timezone);
        builder.Append("; wikibase:timeCalendarModel ");
        builder.Append(calendar);
        builder.AppendLine(".");
        builder.Append(indent);
        builder.Append("FILTER(");
        builder.Append(precision);
        builder.Append(" >= 9 && ");
        builder.Append(timezone);
        builder.Append(" = 0 && ");
        builder.Append(calendar);
        builder.AppendLine($" = wd:{EntityMappingExtensions.GregorianCalendarQid})");
        if (projectAs is null)
        {
            return;
        }

        builder.Append(indent);
        builder.Append("BIND(CONCAT(STR(");
        builder.Append(value);
        builder.Append("), \"|\", STR(");
        builder.Append(precision);
        builder.Append("), \"|\", STR(");
        builder.Append(calendar);
        builder.Append("), \"|\", STR(");
        builder.Append(timezone);
        builder.Append(")) AS ?");
        builder.Append(projectAs);
        builder.AppendLine(")");
    }

    private static void AppendCandidateDateProjection(
        StringBuilder builder,
        string indent,
        string subject,
        string propertyId,
        string suffix,
        string projectAs)
    {
        AppendDateValuePattern(builder, indent, subject, propertyId, suffix, projectAs);
    }

    internal static IReadOnlyList<string> AcceptedTypes(EntityKind kind) => kind switch
    {
        EntityKind.Movie =>
        [
            Constants.MovieQid,
            Constants.ShortFilmQid,
            Constants.TelevisionFilmQid
        ],
        EntityKind.Show =>
        [
            Constants.TelevisionSeriesQid,
            Constants.AnimatedSeriesQid
        ],
        EntityKind.Season => [Constants.SeasonQid],
        EntityKind.Episode => [Constants.EpisodeQid],
        EntityKind.Person => [Constants.HumanQid],
        _ => []
    };

    private static SparqlRequest Request(string query, TimeSpan timeout) => new()
    {
        QueryText = query,
        CanonicalCacheKey = NormalizeQuery(query),
        Timeout = timeout
    };

    private static string NormalizeQuery(string query) => query.Replace("\r\n", "\n").Trim();

    private static string SparqlString(string value) => "\"" + EscapeLiteral(value) + "\"";

    private static string SparqlDateTime(DateTime value)
    {
        var utc = value.Kind == DateTimeKind.Local ? value.ToUniversalTime() : value;
        return "\"" +
            utc.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture) +
            "\"^^xsd:dateTime";
    }

    private static string EscapeLiteral(string value) => value
        .Replace("\\", "\\\\")
        .Replace("\"", "\\\"")
        .Replace("\r", "\\r")
        .Replace("\n", "\\n")
        .Replace("\t", "\\t");

    private static string EscapeCirrus(string value) => EscapeLiteral(value.Replace("\"", string.Empty))
        .Replace(":", "\\:");

    private static void ValidateQid(string qid)
    {
        if (!qid.TryNormalizeQid(out var normalized) ||
            !string.Equals(qid, normalized, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Invalid Wikidata QID.");
        }
    }

    private static void ValidatePropertyId(string propertyId)
    {
        if (propertyId.Length < 2 ||
            propertyId[0] != 'P' ||
            propertyId[1] == '0' ||
            !propertyId.Substring(1).All(IsAsciiDigit))
        {
            throw new InvalidOperationException("Invalid Wikidata property id.");
        }
    }

    private static bool IsAsciiDigit(char value) => value is >= '0' and <= '9';
}
