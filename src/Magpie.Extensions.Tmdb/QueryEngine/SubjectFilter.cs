using System.Collections.Generic;

namespace Magpie.Extensions.Tmdb.QueryEngine;

internal sealed record SubjectFilter(
    bool Excluded,
    IReadOnlyList<SubjectAlternative> Alternatives,
    bool RequiresLocalVerification = false);
