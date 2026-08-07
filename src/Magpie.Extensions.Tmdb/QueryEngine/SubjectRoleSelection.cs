using System.Collections.Generic;

namespace Magpie.Extensions.Tmdb.QueryEngine;

internal sealed record SubjectRoleSelection(string SubjectBindingKey, IReadOnlyList<SubjectRole> Roles);
