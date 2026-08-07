using System.Collections.Generic;

using Magpie.Models.Enums;
using Magpie.Models.Filtering;

namespace Magpie.Extensions.Tmdb.QueryEngine;

internal sealed record TemporalFilter(CriteriaValueOperator Operator, bool Negate, IReadOnlyList<TemporalValue> Values);
