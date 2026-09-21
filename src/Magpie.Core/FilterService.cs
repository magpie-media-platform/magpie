using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Repositories;
using Magpie.Models.Arguments;
using Magpie.Models.Data;
using Magpie.Models.Filtering;
using Magpie.QueryLanguage;
using Magpie.Sdk.Services;

namespace Magpie.Core;

internal sealed class FilterService(
    IContentFilterRepository contentFilters,
    ISubjectFilterRepository subjectFilters) : IFilterService
{
    private static bool CanView(Guid? ownerUserId, Guid callerUserId, bool callerIsAdmin)
    {
        return callerIsAdmin || ownerUserId is null || ownerUserId == callerUserId;
    }

    private static bool CanModify(Guid? ownerUserId, Guid callerUserId, bool callerIsAdmin)
    {
        return callerIsAdmin || ownerUserId == callerUserId;
    }

    private static bool CanModify(ContentFilter filter, Guid callerUserId, bool callerIsAdmin)
    {
        return filter.OwnerUserId is null
            ? callerIsAdmin
            : filter.OwnerUserId == callerUserId || (callerIsAdmin && filter.OwnerLock != true);
    }

    private static MagpieQlParseResult<T> ToParseResult<T>(ParseResult<T> result)
        where T : class
    {
        return result.IsSuccess
            ? new MagpieQlParseResult<T> { Value = result.Value }
            : new MagpieQlParseResult<T> { Error = ToParseError(result.Error!.Value) };
    }

    private static MagpieQlParseError ToParseError(ParseError error)
    {
        return new()
        {
            Message = error.Message,
            Position = error.Position,
            Expected = error.Expected is { Count: > 0 } ? string.Join(", ", error.Expected) : null,
        };
    }

    public async Task<IReadOnlyList<ContentFilter>> ListVisibleContentFiltersAsync(
        string? name,
        string? tag,
        Guid callerUserId,
        CancellationToken cancellationToken = default)
    {
        var sharedItems = await contentFilters.QueryAllAsync(
            new ContentFilterQuery { Tag = tag, SharedOnly = true },
            cancellationToken: cancellationToken).ConfigureAwait(false);
        var ownItems = await contentFilters.QueryAllAsync(
            new ContentFilterQuery { Tag = tag, OwnerUserId = callerUserId },
            cancellationToken: cancellationToken).ConfigureAwait(false);

        var combined = sharedItems.Concat(ownItems);
        if (!string.IsNullOrWhiteSpace(name))
        {
            combined = combined.Where(f => f.Name.Contains(name, StringComparison.OrdinalIgnoreCase));
        }

        return [.. combined];
    }

    public async Task<IReadOnlyList<ContentFilter>> ListOwnContentFiltersAsync(
        Guid ownerUserId,
        CancellationToken cancellationToken = default)
    {
        return await contentFilters.QueryAllAsync(
            new ContentFilterQuery { OwnerUserId = ownerUserId },
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    public async Task<ContentFilter?> GetContentFilterAsync(
        Guid contentFilterId,
        Guid callerUserId,
        bool callerIsAdmin,
        CancellationToken cancellationToken = default)
    {
        var filter = await contentFilters.GetByIdAsync(contentFilterId, cancellationToken).ConfigureAwait(false);
        if (filter is null || !CanView(filter.OwnerUserId, callerUserId, callerIsAdmin))
        {
            return null;
        }

        return filter;
    }

    public async Task<ContentFilter> CreateContentFilterAsync(
        ContentFilter filter,
        Guid ownerUserId,
        CancellationToken cancellationToken = default)
    {
        return await contentFilters
            .CreateAsync(filter with { OwnerUserId = ownerUserId }, ownerUserId, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<bool> PatchContentFilterAsync(
        Guid contentFilterId,
        ContentFilterPatch patch,
        Guid callerUserId,
        bool callerIsAdmin,
        CancellationToken cancellationToken = default)
    {
        var filter = await contentFilters.GetByIdAsync(contentFilterId, cancellationToken).ConfigureAwait(false);
        if (filter is null || !CanModify(filter, callerUserId, callerIsAdmin))
        {
            return false;
        }

        return await contentFilters
            .PatchAsync(patch with { Id = contentFilterId }, callerUserId, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<bool> DeleteContentFilterAsync(
        Guid contentFilterId,
        Guid callerUserId,
        bool callerIsAdmin,
        CancellationToken cancellationToken = default)
    {
        var filter = await contentFilters.GetByIdAsync(contentFilterId, cancellationToken).ConfigureAwait(false);
        if (filter is null || !CanModify(filter, callerUserId, callerIsAdmin))
        {
            return false;
        }

        return await contentFilters.DeleteAsync(contentFilterId, callerUserId, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<SubjectFilter>> ListVisibleSubjectFiltersAsync(
        string? name,
        string? tag,
        Guid callerUserId,
        CancellationToken cancellationToken = default)
    {
        var shared = await subjectFilters.QueryAllAsync(
            new SubjectFilterQuery { Tag = tag, SharedOnly = true },
            cancellationToken: cancellationToken).ConfigureAwait(false);
        var own = await subjectFilters.QueryAllAsync(
            new SubjectFilterQuery { Tag = tag, OwnerUserId = callerUserId },
            cancellationToken: cancellationToken).ConfigureAwait(false);

        var combined = shared.Concat(own);
        if (!string.IsNullOrWhiteSpace(name))
        {
            combined = combined.Where(f => f.Name.Contains(name, StringComparison.OrdinalIgnoreCase));
        }

        return [.. combined];
    }

    public async Task<IReadOnlyList<SubjectFilter>> ListOwnSubjectFiltersAsync(
        Guid ownerUserId,
        CancellationToken cancellationToken = default)
    {
        return await subjectFilters.QueryAllAsync(
            new SubjectFilterQuery { OwnerUserId = ownerUserId },
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    public async Task<SubjectFilter?> GetSubjectFilterAsync(
        Guid subjectFilterId,
        Guid callerUserId,
        bool callerIsAdmin,
        CancellationToken cancellationToken = default)
    {
        var filter = await subjectFilters.GetByIdAsync(subjectFilterId, cancellationToken).ConfigureAwait(false);
        if (filter is null || !CanView(filter.OwnerUserId, callerUserId, callerIsAdmin))
        {
            return null;
        }

        return filter;
    }

    public async Task<SubjectFilter> CreateSubjectFilterAsync(
        SubjectFilter filter,
        Guid ownerUserId,
        CancellationToken cancellationToken = default)
    {
        filter.OwnerUserId = ownerUserId;

        return await subjectFilters.CreateAsync(filter, ownerUserId, cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> PatchSubjectFilterAsync(
        Guid subjectFilterId,
        SubjectFilterPatch patch,
        Guid callerUserId,
        bool callerIsAdmin,
        CancellationToken cancellationToken = default)
    {
        var filter = await subjectFilters.GetByIdAsync(subjectFilterId, cancellationToken).ConfigureAwait(false);
        if (filter is null || !CanModify(filter.OwnerUserId, callerUserId, callerIsAdmin))
        {
            return false;
        }

        return await subjectFilters
            .PatchAsync(patch with { Id = subjectFilterId }, callerUserId, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<bool> DeleteSubjectFilterAsync(
        Guid subjectFilterId,
        Guid callerUserId,
        bool callerIsAdmin,
        CancellationToken cancellationToken = default)
    {
        var filter = await subjectFilters.GetByIdAsync(subjectFilterId, cancellationToken).ConfigureAwait(false);
        if (filter is null || !CanModify(filter.OwnerUserId, callerUserId, callerIsAdmin))
        {
            return false;
        }

        return await subjectFilters.DeleteAsync(subjectFilterId, callerUserId, cancellationToken)
            .ConfigureAwait(false);
    }

    public MagpieQlParseResult<ContentFilterCriteria> ParseContentCriteria(string query)
    {
        return ToParseResult(query.ParseContentFilter());
    }

    public string FormatContentCriteria(ContentFilterCriteria criteria) => criteria.FormatContentFilter();

    public MagpieQlParseResult<SubjectFilterCriteria> ParseSubjectCriteria(string query)
    {
        return ToParseResult(query.ParseSubjectFilter());
    }

    public string FormatSubjectCriteria(SubjectFilterCriteria criteria) => criteria.FormatSubjectFilter();
}
