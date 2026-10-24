using System.Collections.Generic;

using Magpie.Models.Data;

namespace Magpie.Extensions.Tmdb.QueryEngine;

internal sealed record SubjectAlternative(
    IReadOnlyList<SubjectRole> Roles,
    IReadOnlyList<IReadOnlyList<SubjectIdentity>> IdentityClauses,
    RelationshipCriteria? Criteria = null);
