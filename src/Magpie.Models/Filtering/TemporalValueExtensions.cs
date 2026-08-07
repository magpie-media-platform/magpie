using System;
using System.Collections.Generic;
using System.Linq;

using Magpie.Models.Enums;

namespace Magpie.Models.Filtering;

/// <summary>
/// The half-open period comparisons for <see cref="TemporalValue"/>.
/// </summary>
public static class TemporalValueExtensions
{
    /// <summary>
    /// Returns the inclusive start instant of the period of a value.
    /// </summary>
    /// <param name="value">The value that gives the period</param>
    public static DateTime PeriodStart(this TemporalValue value) => value.Value;

    /// <summary>
    /// Returns the exclusive end instant of the period of a value.
    /// </summary>
    /// <param name="value">The value that gives the period</param>
    public static DateTime PeriodEndExclusive(this TemporalValue value) => value.Precision switch
    {
        TemporalPrecision.Year => value.Value.AddYears(1),
        TemporalPrecision.Month => value.Value.AddMonths(1),
        TemporalPrecision.Day => value.Value.AddDays(1),
        _ => value.Value.AddTicks(1)
    };

    /// <summary>
    /// Compares a candidate value to one requested value with an operator.
    /// </summary>
    /// <param name="op">The operator to apply between the two periods</param>
    /// <param name="candidate">The value under comparison</param>
    /// <param name="requested">The value from the filter criterion</param>
    public static bool Compare(
        this TemporalValue candidate,
        CriteriaValueOperator op,
        TemporalValue requested)
    {
        var cs = candidate.PeriodStart();
        var ce = candidate.PeriodEndExclusive();
        var rs = requested.PeriodStart();
        var re = requested.PeriodEndExclusive();
        return op switch
        {
            CriteriaValueOperator.Equals => cs < re && rs < ce,
            CriteriaValueOperator.NotEquals => !(cs < re && rs < ce),
            CriteriaValueOperator.GreaterThan => cs >= re,
            CriteriaValueOperator.GreaterThanOrEqual => cs >= rs,
            CriteriaValueOperator.LessThan => ce <= rs,
            CriteriaValueOperator.LessThanOrEqual => cs < re,
            _ => true
        };
    }

    /// <summary>
    /// Compares a candidate value to a set of requested values with an operator.
    /// </summary>
    /// <param name="op">The operator to apply between the two periods</param>
    /// <param name="candidate">The value under comparison</param>
    /// <param name="requested">The values from the filter criterion</param>
    public static bool Compare(
        this TemporalValue candidate,
        CriteriaValueOperator op,
        IReadOnlyList<TemporalValue> requested)
    {
        switch (op)
        {
            case CriteriaValueOperator.In:
                return requested.Any(r => candidate.Compare(CriteriaValueOperator.Equals, r));
            case CriteriaValueOperator.NotIn:
                return requested.All(r => candidate.Compare(CriteriaValueOperator.NotEquals, r));
            default:
                return requested.Count > 0 && candidate.Compare(op, requested[0]);
        }
    }
}
