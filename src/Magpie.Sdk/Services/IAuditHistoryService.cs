using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Models;
using Magpie.Models.Arguments;
using Magpie.Models.Data;
using Magpie.Models.Paging;

namespace Magpie.Sdk.Services;

/// <summary>
/// The service contract for the merged audit-history feed.
/// </summary>
public interface IAuditHistoryService
{
    /// <summary>
    /// Queries the merged audit-history feed.
    /// </summary>
    /// <param name="query">The filter criteria, page number, and page size</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<PagedList<AuditHistoryEntry>> QueryAsync(
        AuditHistoryQuery query,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Purges the audit history.
    /// </summary>
    /// <param name="olderThanDays">The age in days</param>
    /// <param name="callerUserId">The id of the user that purges the audit history</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<AuditHistoryPurgeCounts> PurgeAsync(
        int olderThanDays,
        Guid callerUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes an audit record.
    /// </summary>
    /// <param name="auditRecord">The audit record to write</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    /// <returns>The written record</returns>
    Task<AuditRecord> WriteAsync(AuditRecord auditRecord, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the audit records of a content item.
    /// </summary>
    /// <param name="contentId">The id of the content item</param>
    /// <param name="category">The category of the audit records</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    /// <returns>The records, newest first</returns>
    Task<IReadOnlyList<AuditRecord>> ListByContentAsync(
        Guid contentId,
        string category,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the audit records that a job execution wrote.
    /// </summary>
    /// <param name="jobExecutionId">The id of the job execution</param>
    /// <param name="category">The category of the audit records</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    /// <returns>The records, newest first</returns>
    Task<IReadOnlyList<AuditRecord>> ListByJobExecutionAsync(
        Guid jobExecutionId,
        string category,
        CancellationToken cancellationToken = default);
}
