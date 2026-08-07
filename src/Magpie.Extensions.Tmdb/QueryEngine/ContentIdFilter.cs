using System.Collections.Generic;

namespace Magpie.Extensions.Tmdb.QueryEngine;

internal sealed record ContentIdFilter(bool Excluded, IReadOnlyList<ContentId> Alternatives);
