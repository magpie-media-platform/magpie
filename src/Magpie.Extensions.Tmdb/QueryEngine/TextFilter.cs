using System.Collections.Generic;

using Magpie.Models.Enums;

namespace Magpie.Extensions.Tmdb.QueryEngine;

internal sealed record TextFilter(
    TextField Field,
    CriteriaValueOperator Operator,
    bool Negate,
    IReadOnlyList<string> Values);
