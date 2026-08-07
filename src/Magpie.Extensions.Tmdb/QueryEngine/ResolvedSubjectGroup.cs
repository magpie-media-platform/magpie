using System.Collections.Generic;

namespace Magpie.Extensions.Tmdb.QueryEngine;

internal sealed record ResolvedSubjectGroup
{
    internal string BindingKey { get; init; } = string.Empty;

    internal required bool Excluded { get; init; }

    internal required IReadOnlyList<ResolvedSubject> Subjects { get; init; }

    internal IReadOnlyList<SubjectAlternative> Alternatives { get; init; } = [];

    internal bool Incomplete { get; init; }

    internal IReadOnlyCollection<SubjectRole> IncompleteRoles { get; init; } = new HashSet<SubjectRole>();
}
