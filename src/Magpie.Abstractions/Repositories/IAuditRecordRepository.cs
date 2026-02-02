using System;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Models.Arguments;
using Magpie.Models.Data;

namespace Magpie.Abstractions.Repositories;

/// <summary>
/// Repository for recording and querying audit history records.
/// Audit records are immutable and cannot be updated or deleted.
/// </summary>
public interface IAuditRecordRepository :
    IRepository<Guid, AuditRecord>,
    IQueryRepository<AuditRecord, AuditRecordQuery>
{
    /// <summary>
    /// Create an audit record.
    /// </summary>
    /// <param name="auditRecord">The audit record to create.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task<AuditRecord> CreateAsync(AuditRecord auditRecord, CancellationToken cancellationToken = default);
}
