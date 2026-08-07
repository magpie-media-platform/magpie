using System;
using System.Linq;
using System.Linq.Expressions;

using Magpie.Models.Enums;
using Magpie.Models.Filtering;

using Microsoft.EntityFrameworkCore;

namespace Magpie.Data;

/// <summary>
/// The filter extensions for entity queries.
/// </summary>
public static class FilteringQueryableExtensions
{
    /// <summary>
    /// Applies equality property criteria to a query.
    /// </summary>
    /// <typeparam name="TEntity">
    /// The entity type of the query.
    /// </typeparam>
    /// <typeparam name="TProperty">
    /// The type of the property to compare.
    /// </typeparam>
    /// <param name="query">
    /// The query to filter.
    /// </param>
    /// <param name="criteria">
    /// The criteria to apply to the property.
    /// </param>
    /// <param name="property">
    /// The name of the property to filter on.
    /// </param>
    public static IQueryable<TEntity> ApplyCriteriaSet<TEntity, TProperty>(
        this IQueryable<TEntity> query,
        CriteriaSet<PropertyCriterion<TProperty>>? criteria,
        string property)
            where TEntity : class
            where TProperty : IEquatable<TProperty>
    {
        if (criteria?.And is null || !criteria.And.Any())
        {
            return query;
        }

        foreach (var c in criteria.And)
        {
            query = query.Where(BuildPredicate<TEntity, TProperty>(c, property));
        }

        return query;
    }

    /// <summary>
    /// Applies string property criteria to a query.
    /// </summary>
    /// <typeparam name="TEntity">
    /// The entity type of the query.
    /// </typeparam>
    /// <param name="query">
    /// The query to filter.
    /// </param>
    /// <param name="criteria">
    /// The criteria to apply to the property.
    /// </param>
    /// <param name="property">
    /// The name of the property to filter on.
    /// </param>
    public static IQueryable<TEntity> ApplyCriteriaSet<TEntity>(
        this IQueryable<TEntity> query,
        CriteriaSet<PropertyCriterion<string>>? criteria,
        string property) where TEntity : class
    {
        if (criteria?.And is null || !criteria.And.Any())
        {
            return query;
        }

        foreach (var c in criteria.And)
        {
            query = query.Where(BuildPredicate<TEntity>(c, property));
        }

        return query;
    }

    /// <summary>
    /// Applies integer property criteria to a query.
    /// </summary>
    /// <typeparam name="TEntity">
    /// The entity type of the query.
    /// </typeparam>
    /// <param name="query">
    /// The query to filter.
    /// </param>
    /// <param name="criteria">
    /// The criteria to apply to the property.
    /// </param>
    /// <param name="property">
    /// The name of the property to filter on.
    /// </param>
    public static IQueryable<TEntity> ApplyCriteriaSet<TEntity>(
        this IQueryable<TEntity> query,
        CriteriaSet<PropertyCriterion<int>>? criteria,
        string property) where TEntity : class
    {
        if (criteria?.And is null || !criteria.And.Any())
        {
            return query;
        }

        foreach (var c in criteria.And)
        {
            query = query.Where(BuildPredicate<TEntity>(c, property));
        }

        return query;
    }

    /// <summary>
    /// Applies date and time property criteria to a query.
    /// </summary>
    /// <typeparam name="TEntity">
    /// The entity type of the query.
    /// </typeparam>
    /// <param name="query">
    /// The query to filter.
    /// </param>
    /// <param name="criteria">
    /// The criteria to apply to the property.
    /// </param>
    /// <param name="property">
    /// The name of the property to filter on.
    /// </param>
    public static IQueryable<TEntity> ApplyCriteriaSet<TEntity>(
        this IQueryable<TEntity> query,
        CriteriaSet<PropertyCriterion<DateTime>>? criteria,
        string property) where TEntity : class
    {
        if (criteria?.And is null || !criteria.And.Any())
        {
            return query;
        }

        foreach (var c in criteria.And)
        {
            query = query.Where(BuildPredicate<TEntity>(c, property));
        }

        return query;
    }

    /// <summary>
    /// Applies duration property criteria to a query.
    /// </summary>
    /// <typeparam name="TEntity">
    /// The entity type of the query.
    /// </typeparam>
    /// <param name="query">
    /// The query to filter.
    /// </param>
    /// <param name="criteria">
    /// The criteria to apply to the property.
    /// </param>
    /// <param name="property">
    /// The name of the property to filter on.
    /// </param>
    public static IQueryable<TEntity> ApplyCriteriaSet<TEntity>(
        this IQueryable<TEntity> query,
        CriteriaSet<PropertyCriterion<TimeSpan>>? criteria,
        string property) where TEntity : class
    {
        if (criteria?.And is null || !criteria.And.Any())
        {
            return query;
        }

        foreach (var c in criteria.And)
        {
            query = query.Where(BuildPredicate<TEntity>(c, property));
        }

        return query;
    }

    /// <summary>
    /// Applies temporal property criteria to a query.
    /// </summary>
    /// <typeparam name="TEntity">
    /// The entity type of the query.
    /// </typeparam>
    /// <param name="query">
    /// The query to filter.
    /// </param>
    /// <param name="criteria">
    /// The criteria to apply to the property.
    /// </param>
    /// <param name="property">
    /// The name of the property to filter on.
    /// </param>
    public static IQueryable<TEntity> ApplyCriteriaSet<TEntity>(
        this IQueryable<TEntity> query,
        CriteriaSet<PropertyCriterion<TemporalValue>>? criteria,
        string property) where TEntity : class
    {
        if (criteria?.And is null || !criteria.And.Any())
        {
            return query;
        }

        foreach (var c in criteria.And)
        {
            var parameter = Expression.Parameter(typeof(TEntity), "e");
            var value = Expression.Call(
                typeof(EF),
                nameof(EF.Property),
                [typeof(DateTime?)],
                parameter,
                Expression.Constant(property));
            query = query.Where(
                Expression.Lambda<Func<TEntity, bool>>(BuildTemporalPredicateBody(value, c), parameter));
        }

        return query;
    }

    /// <summary>
    /// Builds a temporal predicate expression.
    /// </summary>
    /// <param name="value">
    /// The expression for the database value to compare.
    /// </param>
    /// <param name="criterion">
    /// The temporal criterion to convert.
    /// </param>
    public static Expression BuildTemporalPredicateBody(
        this Expression value,
        PropertyCriterion<TemporalValue> criterion)
    {
        var predicate = BuildTemporalComparison(value, criterion);
        return criterion.Negate ? Expression.Not(predicate) : predicate;
    }

    private static Expression BuildTemporalComparison(Expression value, PropertyCriterion<TemporalValue> criterion)
    {
        var nullConstant = Expression.Constant(null, typeof(DateTime?));
        switch (criterion.Operator)
        {
            case CriteriaValueOperator.IsNull:
                return Expression.Equal(value, nullConstant);
            case CriteriaValueOperator.IsNotNull:
                return Expression.NotEqual(value, nullConstant);
            case CriteriaValueOperator.In:
                return BuildTemporalMembership(value, criterion, exclude: false);
            case CriteriaValueOperator.NotIn:
                return BuildTemporalMembership(value, criterion, exclude: true);
            default:
                return BuildTemporalPeriod(
                    value,
                    criterion.Operator,
                    criterion.Value ??
                    throw new NotSupportedException(
                        $"Operator {criterion.Operator} requires a temporal value"));
        }
    }

    private static Expression BuildTemporalMembership(
        Expression value,
        PropertyCriterion<TemporalValue> criterion,
        bool exclude)
    {
        var operand = exclude ? CriteriaValueOperator.NotEquals : CriteriaValueOperator.Equals;
        Expression? accumulated = null;
        foreach (var requested in criterion.Values ?? [])
        {
            var term = BuildTemporalPeriod(value, operand, requested);
            accumulated = accumulated is null
                ? term
                : exclude ? Expression.AndAlso(accumulated, term) : Expression.OrElse(accumulated, term);
        }

        return accumulated ?? Expression.Constant(exclude);
    }

    private static Expression BuildTemporalPeriod(
        Expression value,
        CriteriaValueOperator op,
        TemporalValue requested)
    {
        var start = Expression.Constant(requested.PeriodStart(), typeof(DateTime?));
        var end = Expression.Constant(requested.PeriodEndExclusive(), typeof(DateTime?));
        return op switch
        {
            CriteriaValueOperator.Equals => Expression.AndAlso(
                Expression.GreaterThanOrEqual(value, start),
                Expression.LessThan(value, end)),
            CriteriaValueOperator.NotEquals => Expression.Not(Expression.AndAlso(
                Expression.GreaterThanOrEqual(value, start),
                Expression.LessThan(value, end))),
            CriteriaValueOperator.GreaterThan => Expression.GreaterThanOrEqual(value, end),
            CriteriaValueOperator.GreaterThanOrEqual => Expression.GreaterThanOrEqual(value, start),
            CriteriaValueOperator.LessThan => Expression.LessThan(value, start),
            CriteriaValueOperator.LessThanOrEqual => Expression.LessThan(value, end),
            var other => throw new NotSupportedException($"Operator {other} is not supported for temporal criteria")
        };
    }

    private static Expression<Func<TEntity, bool>> BuildPredicate<TEntity, TProperty>(
        PropertyCriterion<TProperty> criterion,
        string property)
            where TEntity : class
            where TProperty : IEquatable<TProperty>
    {
        return criterion.Operator switch
        {
            CriteriaValueOperator.Equals => e =>
                EF.Property<TProperty>(e, property).Equals(criterion.Value),
            CriteriaValueOperator.NotEquals => e =>
                !EF.Property<TProperty>(e, property).Equals(criterion.Value),
            CriteriaValueOperator.IsNull => e =>
                EF.Property<TProperty>(e, property) == null,
            CriteriaValueOperator.IsNotNull => e =>
                EF.Property<TProperty>(e, property) != null,
            CriteriaValueOperator.In => e =>
                EF.Property<TProperty>(e, property) != null &&
                criterion.Values != null &&
                criterion.Values.Contains(EF.Property<TProperty>(e, property)),
            CriteriaValueOperator.NotIn => e =>
                EF.Property<TProperty>(e, property) == null ||
                criterion.Values == null ||
                !criterion.Values.Contains(EF.Property<TProperty>(e, property)),
            var op => throw new NotSupportedException($"Operator {op} is not supported for {typeof(TProperty).Name}")
        };
    }

    private static Expression<Func<TEntity, bool>> BuildPredicate<TEntity>(
        PropertyCriterion<string> criterion,
        string property) where TEntity : class
    {
        return criterion.Operator switch
        {
            CriteriaValueOperator.ContainsString => e =>
                criterion.Value != null &&
                EF.Property<string>(e, property) != null &&
                EF.Property<string>(e, property).Contains(criterion.Value),
            CriteriaValueOperator.NotContainsString => e =>
                criterion.Value == null ||
                EF.Property<string>(e, property) == null ||
                !EF.Property<string>(e, property).Contains(criterion.Value),
            CriteriaValueOperator.StartsWithString => e =>
                criterion.Value != null &&
                EF.Property<string>(e, property) != null &&
                EF.Property<string>(e, property).StartsWith(criterion.Value),
            CriteriaValueOperator.NotStartsWithString => e =>
                criterion.Value == null ||
                EF.Property<string>(e, property) == null ||
                !EF.Property<string>(e, property).StartsWith(criterion.Value),
            CriteriaValueOperator.EndsWithString => e =>
                criterion.Value != null &&
                EF.Property<string>(e, property) != null &&
                EF.Property<string>(e, property).EndsWith(criterion.Value),
            CriteriaValueOperator.NotEndsWithString => e =>
                criterion.Value == null ||
                EF.Property<string>(e, property) == null ||
                !EF.Property<string>(e, property).EndsWith(criterion.Value),
            _ => BuildPredicate<TEntity, string>(criterion, property)
        };
    }

    private static Expression<Func<TEntity, bool>> BuildPredicate<TEntity>(
        PropertyCriterion<int> criterion,
        string property) where TEntity : class
    {
        return criterion.Operator switch
        {
            CriteriaValueOperator.LessThan => e => EF.Property<int?>(e, property) < criterion.Value,
            CriteriaValueOperator.LessThanOrEqual => e => EF.Property<int?>(e, property) <= criterion.Value,
            CriteriaValueOperator.GreaterThan => e => EF.Property<int?>(e, property) > criterion.Value,
            CriteriaValueOperator.GreaterThanOrEqual => e => EF.Property<int?>(e, property) >= criterion.Value,
            _ => BuildPredicate<TEntity, int>(criterion, property)
        };
    }

    private static Expression<Func<TEntity, bool>> BuildPredicate<TEntity>(
        PropertyCriterion<DateTime> criterion,
        string property)
            where TEntity : class
    {
        return criterion.Operator switch
        {
            CriteriaValueOperator.LessThan => e => EF.Property<DateTime?>(e, property) < criterion.Value,
            CriteriaValueOperator.LessThanOrEqual => e => EF.Property<DateTime?>(e, property) <= criterion.Value,
            CriteriaValueOperator.GreaterThan => e => EF.Property<DateTime?>(e, property) > criterion.Value,
            CriteriaValueOperator.GreaterThanOrEqual => e => EF.Property<DateTime?>(e, property) >= criterion.Value,
            _ => BuildPredicate<TEntity, DateTime>(criterion, property)
        };
    }

    private static Expression<Func<TEntity, bool>> BuildPredicate<TEntity>(
        PropertyCriterion<TimeSpan> criterion,
        string property) where TEntity : class
    {
        return criterion.Operator switch
        {
            CriteriaValueOperator.LessThan => e => EF.Property<TimeSpan>(e, property) < criterion.Value,
            CriteriaValueOperator.LessThanOrEqual => e => EF.Property<TimeSpan>(e, property) <= criterion.Value,
            CriteriaValueOperator.GreaterThan => e => EF.Property<TimeSpan>(e, property) > criterion.Value,
            CriteriaValueOperator.GreaterThanOrEqual => e => EF.Property<TimeSpan>(e, property) >= criterion.Value,
            _ => BuildPredicate<TEntity, TimeSpan>(criterion, property)
        };
    }
}
