using System;

using Magpie.Models.Arguments;
using Magpie.Models.Data;

namespace Magpie.Abstractions.Repositories;

/// <summary>
/// The repository contract for subjects.
/// </summary>
public interface ISubjectRepository :
    IQueryRepository<Subject, SubjectQuery>,
    ICrudRepository<Guid, Subject>,
    IPatchRepository<Guid, Subject, SubjectRowPatch>,
    IBulkDeleteRepository<Subject, SubjectQuery>;
