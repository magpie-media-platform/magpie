using System.Collections.Generic;

namespace Magpie.Extensions.Tmdb.QueryEngine;

internal sealed record GenreFilter(bool Excluded, IReadOnlyList<IReadOnlyList<GenrePredicate>> Alternatives);
