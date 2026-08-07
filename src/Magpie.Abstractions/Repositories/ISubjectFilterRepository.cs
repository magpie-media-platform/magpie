using System;

using Magpie.Models.Arguments;
using Magpie.Models.Data;

namespace Magpie.Abstractions.Repositories;

/// <summary>
/// The repository contract for subject filters.
/// </summary>
public interface ISubjectFilterRepository :
    IQueryRepository<SubjectFilter, SubjectFilterQuery>,
    ICrudRepository<Guid, SubjectFilter>,
    IPatchRepository<Guid, SubjectFilter, SubjectFilterPatch>;
