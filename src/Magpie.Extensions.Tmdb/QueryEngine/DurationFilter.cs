using System;
using System.Collections.Generic;

using Magpie.Models.Enums;

namespace Magpie.Extensions.Tmdb.QueryEngine;

internal sealed record DurationFilter(CriteriaValueOperator Operator, bool Negate, IReadOnlyList<TimeSpan> Values);
