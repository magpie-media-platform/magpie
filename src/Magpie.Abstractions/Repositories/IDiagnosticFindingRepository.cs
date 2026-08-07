using System;

using Magpie.Models.Arguments;
using Magpie.Models.Data;

namespace Magpie.Abstractions.Repositories;

/// <summary>
/// The repository contract for diagnostic findings.
/// </summary>
public interface IDiagnosticFindingRepository :
    ICrudRepository<Guid, DiagnosticFinding>,
    IQueryRepository<DiagnosticFinding, DiagnosticFindingQuery>;
