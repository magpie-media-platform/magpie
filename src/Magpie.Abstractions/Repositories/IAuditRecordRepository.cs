using System;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Models.Arguments;
using Magpie.Models.Data;

namespace Magpie.Abstractions.Repositories;

/// <summary>
/// The repository contract for audit records.
/// </summary>
public interface IAuditRecordRepository :
    IKeyedRepository<Guid, AuditRecord>,
    IQueryRepository<AuditRecord, AuditRecordQuery>,
    IBulkDeleteRepository<AuditRecord, AuditRecordQuery>
{
    /// <summary>
    /// Creates a new audit record.
    /// </summary>
    /// <param name="auditRecord">The audit record to create</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    /// <returns>The new audit record</returns>
    Task<AuditRecord> CreateAsync(AuditRecord auditRecord, CancellationToken cancellationToken = default);
}
