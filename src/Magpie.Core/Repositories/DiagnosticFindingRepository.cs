using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Repositories;
using Magpie.Data;
using Magpie.Models.Arguments;
using Magpie.Models.Data;
using Magpie.Models.Paging;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Magpie.Core.Repositories;

internal sealed class DiagnosticFindingRepository(
    MagpieDbContext context,
    ILogger<DiagnosticFindingRepository> logger) : IDiagnosticFindingRepository
{
    public async Task<IEnumerable<DiagnosticFinding>> ListAllAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await context.DiagnosticFindings.AsNoTracking().ToListAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The diagnostic finding repository failed to list findings.");
            throw;
        }
    }

    public async Task<DiagnosticFinding?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        try
        {
            return await context.DiagnosticFindings.AsNoTracking()
                .FirstOrDefaultAsync(finding => finding.Id == id, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The diagnostic finding repository failed to get finding {FindingId}.", id);
            throw;
        }
    }

    public async Task<PagedList<DiagnosticFinding>> QueryAsync(
        DiagnosticFindingQuery query,
        PagingParameter paging,
        SortParameter? sort,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var findings = context.DiagnosticFindings.AsNoTracking().AsQueryable();
            if (query.DiagnosticCheckId is { } diagnosticCheckId)
            {
                findings = findings.Where(finding => finding.DiagnosticCheckId == diagnosticCheckId);
            }

            findings = findings.OrderBy(finding => finding.Id);
            var totalItems = await findings.CountAsync(cancellationToken).ConfigureAwait(false);
            findings = findings.Skip((paging.PageNumber - 1) * paging.PageSize).Take(paging.PageSize);

            return new()
            {
                Items = await findings.ToListAsync(cancellationToken).ConfigureAwait(false),
                TotalItems = totalItems,
                PageNumber = paging.PageNumber,
                PageSize = paging.PageSize
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The diagnostic finding repository failed to query findings.");
            throw;
        }
    }

    public async Task<DiagnosticFinding> CreateAsync(
        DiagnosticFinding model,
        Guid? createdByUserId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            context.DiagnosticFindings.Add(model);
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return model;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The diagnostic finding repository failed to create a finding.");
            throw;
        }
    }

    public async Task<bool> UpdateAsync(
        DiagnosticFinding model,
        Guid? modifiedByUserId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            context.DiagnosticFindings.Update(model);
            return await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false) > 0;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The diagnostic finding repository failed to update finding {FindingId}.", model.Id);
            throw;
        }
    }

    public async Task<bool> DeleteAsync(Guid id, Guid? deletedByUserId, CancellationToken cancellationToken = default)
    {
        try
        {
            return await context.DiagnosticFindings.Where(finding => finding.Id == id)
                .ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false) > 0;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The diagnostic finding repository failed to delete finding {FindingId}.", id);
            throw;
        }
    }
}
