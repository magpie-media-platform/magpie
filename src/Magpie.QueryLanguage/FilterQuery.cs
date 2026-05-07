using Magpie.Models.Data;

namespace Magpie.QueryLanguage;

/// <summary>
/// Public facade for parsing and formatting filter queries.
/// </summary>
public static class FilterQuery
{
    /// <summary>
    /// Parses a query string into content filter criteria.
    /// </summary>
    /// <param name="query">The query string to parse.</param>
    public static ParseResult<ContentFilterCriteria> ParseContentFilter(string query)
    {
        var parseResult = FilterQueryParser.Parse(query);
        if (!parseResult.IsSuccess)
        {
            return ParseResult<ContentFilterCriteria>.Failure(parseResult.Error!.Value);
        }

        return FilterQueryTransform.ToContentFilterCriteria(parseResult.Value!);
    }

    /// <summary>
    /// Parses a query string into subject filter criteria.
    /// </summary>
    /// <param name="query">The query string to parse.</param>
    public static ParseResult<SubjectFilterCriteria> ParseSubjectFilter(string query)
    {
        var parseResult = FilterQueryParser.Parse(query);
        if (!parseResult.IsSuccess)
        {
            return ParseResult<SubjectFilterCriteria>.Failure(parseResult.Error!.Value);
        }

        return FilterQueryTransform.ToSubjectFilterCriteria(parseResult.Value!);
    }

    /// <summary>
    /// Formats content filter criteria as a query string.
    /// </summary>
    /// <param name="criteria">The criteria to format.</param>
    public static string FormatContentFilter(ContentFilterCriteria criteria)
    {
        return FilterQueryFormatter.Format(criteria);
    }

    /// <summary>
    /// Formats subject filter criteria as a query string.
    /// </summary>
    /// <param name="criteria">The criteria to format.</param>
    public static string FormatSubjectFilter(SubjectFilterCriteria criteria)
    {
        return FilterQueryFormatter.Format(criteria);
    }
}
