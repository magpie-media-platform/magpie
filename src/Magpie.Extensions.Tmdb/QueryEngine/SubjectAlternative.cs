using System.Collections.Generic;

namespace Magpie.Extensions.Tmdb.QueryEngine;

internal sealed record SubjectAlternative(
    IReadOnlyList<SubjectRole> Roles,
    IReadOnlyList<IReadOnlyList<SubjectIdentity>> IdentityClauses,
    RelationshipCriteria? Criteria = null);
