using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Models.Arguments;
using Magpie.Models.Data;
using Magpie.Models.Filtering;

namespace Magpie.Sdk.Services;

/// <summary>
/// The service contract for saved content filters, saved subject filters, and MagpieQL criteria.
/// </summary>
public interface IFilterService
{
    /// <summary>
    /// Lists the shared content filters and the content filters of the caller.
    /// </summary>
    /// <param name="name">The substring that the name must contain, or <see langword="null"/> for all</param>
    /// <param name="tag">The tag that the filter must carry, or <see langword="null"/> for all</param>
    /// <param name="callerUserId">The id of the user that makes the request</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<IReadOnlyList<ContentFilter>> ListVisibleContentFiltersAsync(
        string? name,
        string? tag,
        Guid callerUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the content filters that one user owns.
    /// </summary>
    /// <param name="ownerUserId">The id of the user that owns the filters</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<IReadOnlyList<ContentFilter>> ListOwnContentFiltersAsync(
        Guid ownerUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// A content filter by id when the caller can see it.
    /// </summary>
    /// <param name="contentFilterId">The id of the content filter to return</param>
    /// <param name="callerUserId">The id of the user that makes the request</param>
    /// <param name="callerIsAdmin">Whether the caller is an administrator</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<ContentFilter?> GetContentFilterAsync(
        Guid contentFilterId,
        Guid callerUserId,
        bool callerIsAdmin,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a content filter for an owner.
    /// </summary>
    /// <param name="filter">The values for the new content filter</param>
    /// <param name="ownerUserId">The id of the user that owns the new filter</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<ContentFilter> CreateContentFilterAsync(
        ContentFilter filter,
        Guid ownerUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Patches a content filter and enforces the owner lock against the caller.
    /// </summary>
    /// <param name="contentFilterId">The id of the content filter to patch</param>
    /// <param name="patch">The fields to change</param>
    /// <param name="callerUserId">The id of the user that makes the request</param>
    /// <param name="callerIsAdmin">Whether the caller is an administrator</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<bool> PatchContentFilterAsync(
        Guid contentFilterId,
        ContentFilterPatch patch,
        Guid callerUserId,
        bool callerIsAdmin,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a content filter and enforces the owner lock against the caller.
    /// </summary>
    /// <param name="contentFilterId">The id of the content filter to delete</param>
    /// <param name="callerUserId">The id of the user that makes the request</param>
    /// <param name="callerIsAdmin">Whether the caller is an administrator</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<bool> DeleteContentFilterAsync(
        Guid contentFilterId,
        Guid callerUserId,
        bool callerIsAdmin,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the shared subject filters and the subject filters of the caller.
    /// </summary>
    /// <param name="name">The substring that the name must contain, or <see langword="null"/> for all</param>
    /// <param name="tag">The tag that the filter must carry, or <see langword="null"/> for all</param>
    /// <param name="callerUserId">The id of the user that makes the request</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<IReadOnlyList<SubjectFilter>> ListVisibleSubjectFiltersAsync(
        string? name,
        string? tag,
        Guid callerUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the subject filters that one user owns.
    /// </summary>
    /// <param name="ownerUserId">The id of the user that owns the filters</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<IReadOnlyList<SubjectFilter>> ListOwnSubjectFiltersAsync(
        Guid ownerUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// A subject filter by id when the caller can see it.
    /// </summary>
    /// <param name="subjectFilterId">The id of the subject filter to return</param>
    /// <param name="callerUserId">The id of the user that makes the request</param>
    /// <param name="callerIsAdmin">Whether the caller is an administrator</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<SubjectFilter?> GetSubjectFilterAsync(
        Guid subjectFilterId,
        Guid callerUserId,
        bool callerIsAdmin,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a subject filter for an owner.
    /// </summary>
    /// <param name="filter">The values for the new subject filter</param>
    /// <param name="ownerUserId">The id of the user that owns the new filter</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<SubjectFilter> CreateSubjectFilterAsync(
        SubjectFilter filter,
        Guid ownerUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Patches a subject filter and enforces the owner lock against the caller.
    /// </summary>
    /// <param name="subjectFilterId">The id of the subject filter to patch</param>
    /// <param name="patch">The fields to change</param>
    /// <param name="callerUserId">The id of the user that makes the request</param>
    /// <param name="callerIsAdmin">Whether the caller is an administrator</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<bool> PatchSubjectFilterAsync(
        Guid subjectFilterId,
        SubjectFilterPatch patch,
        Guid callerUserId,
        bool callerIsAdmin,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a subject filter and enforces the owner lock against the caller.
    /// </summary>
    /// <param name="subjectFilterId">The id of the subject filter to delete</param>
    /// <param name="callerUserId">The id of the user that makes the request</param>
    /// <param name="callerIsAdmin">Whether the caller is an administrator</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<bool> DeleteSubjectFilterAsync(
        Guid subjectFilterId,
        Guid callerUserId,
        bool callerIsAdmin,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Parses a MagpieQL query into content filter criteria.
    /// </summary>
    /// <param name="query">The MagpieQL text to parse</param>
    MagpieQlParseResult<ContentFilterCriteria> ParseContentCriteria(string query);

    /// <summary>
    /// Formats content filter criteria as a MagpieQL query.
    /// </summary>
    /// <param name="criteria">The criteria to write as MagpieQL</param>
    string FormatContentCriteria(ContentFilterCriteria criteria);

    /// <summary>
    /// Parses a MagpieQL query into subject filter criteria.
    /// </summary>
    /// <param name="query">The MagpieQL text to parse</param>
    MagpieQlParseResult<SubjectFilterCriteria> ParseSubjectCriteria(string query);

    /// <summary>
    /// Formats subject filter criteria as a MagpieQL query.
    /// </summary>
    /// <param name="criteria">The criteria to write as MagpieQL</param>
    string FormatSubjectCriteria(SubjectFilterCriteria criteria);
}
