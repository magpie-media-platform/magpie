using System.Collections.Generic;

namespace Magpie.Extensions.Tmdb.QueryEngine;

internal sealed record ResolvedSubject(
    int Id,
    string Name,
    SubjectRole Role,
    bool ViaCompanySearch = false,
    IReadOnlyDictionary<string, string>? ExternalIds = null);
