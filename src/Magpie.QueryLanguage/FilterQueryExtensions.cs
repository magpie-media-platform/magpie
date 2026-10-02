using System;

using Magpie.Models.Data;

namespace Magpie.QueryLanguage;

/// <summary>
/// The parse and format extensions for filter queries.
/// </summary>
public static class FilterQueryExtensions
{
    /// <summary>
    /// The error message for a content filter that sets the removed top-level media-file criteria.
    /// </summary>
    public const string LegacyMediaFilesMessage =
        "The filter uses the removed top-level media file criteria.";

    /// <summary>
    /// Parses a query string into content filter criteria.
    /// </summary>
    /// <param name="query">The query string to parse</param>
    public static ParseResult<ContentFilterCriteria> ParseContentFilter(this string query)
    {
        var parseResult = query.ParseFilterQuery();
        if (parseResult.Error is { } error)
        {
            return ParseResult<ContentFilterCriteria>.Failure(error);
        }

        return parseResult.Value!.ToContentFilterCriteria();
    }

    /// <summary>
    /// Parses a query string into subject filter criteria.
    /// </summary>
    /// <param name="query">The query string to parse</param>
    public static ParseResult<SubjectFilterCriteria> ParseSubjectFilter(this string query)
    {
        var parseResult = query.ParseFilterQuery();
        if (parseResult.Error is { } error)
        {
            return ParseResult<SubjectFilterCriteria>.Failure(error);
        }

        return parseResult.Value!.ToSubjectFilterCriteria();
    }

    /// <summary>
    /// Formats content filter criteria as a query string.
    /// </summary>
    /// <param name="criteria">The criteria to format</param>
    public static string FormatContentFilter(this ContentFilterCriteria criteria)
    {
        return criteria.FormatFilterQueryOrDefault()
            ?? throw new ArgumentException(LegacyMediaFilesMessage);
    }

    /// <summary>
    /// Formats content filter criteria as a query string, or returns null when the criteria are not expressible.
    /// </summary>
    /// <param name="criteria">The criteria to format</param>
    /// <returns>The query string, or <see langword="null"/> when the criteria are not expressible</returns>
    public static string? FormatContentFilterOrDefault(this ContentFilterCriteria criteria)
    {
        return criteria.FormatFilterQueryOrDefault();
    }

    /// <summary>
    /// Formats subject filter criteria as a query string.
    /// </summary>
    /// <param name="criteria">The criteria to format</param>
    public static string FormatSubjectFilter(this SubjectFilterCriteria criteria)
    {
        return criteria.FormatFilterQuery();
    }
}
