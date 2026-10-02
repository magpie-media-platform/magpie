using System;
using System.Linq;
using System.Linq.Expressions;

using Magpie.Models.Enums;
using Magpie.Models.Filtering;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;

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
        foreach (var c in criteria?.And ?? [])
        {
            query = query.Where(BuildPredicate<TEntity, TProperty>(c, property));
        }

        return query;
    }

    /// <summary>
    /// Applies case-insensitive string property criteria to a query.
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
        foreach (var c in criteria?.And ?? [])
        {
            query = query.Where(BuildStringPredicate<TEntity>(c, property, ignoreCase: true));
        }

        return query;
    }

    /// <summary>
    /// Applies string property criteria to a query of a column that holds normalized values.
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
    public static IQueryable<TEntity> ApplyNormalizedCriteriaSet<TEntity>(
        this IQueryable<TEntity> query,
        CriteriaSet<PropertyCriterion<string>>? criteria,
        string property) where TEntity : class
    {
        foreach (var c in criteria?.And ?? [])
        {
            var lowered = new PropertyCriterion<string>
            {
                Operator = c.Operator,
                Negate = c.Negate,
                Value = c.Value?.ToLowerInvariant(),
                Values = c.Values?.Select(value => value.ToLowerInvariant()).ToList()
            };
            query = query.Where(BuildStringPredicate<TEntity>(lowered, property, ignoreCase: false));
        }

        return query;
    }

    /// <summary>
    /// Applies case-sensitive string property criteria to a query.
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
    public static IQueryable<TEntity> ApplyExactCriteriaSet<TEntity>(
        this IQueryable<TEntity> query,
        CriteriaSet<PropertyCriterion<string>>? criteria,
        string property) where TEntity : class
    {
        foreach (var c in criteria?.And ?? [])
        {
            query = query.Where(BuildStringPredicate<TEntity>(c, property, ignoreCase: false));
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
        foreach (var c in criteria?.And ?? [])
        {
            query = query.Where(BuildPredicate<TEntity>(c, property));
        }

        return query;
    }

    /// <summary>
    /// Applies long integer property criteria to a query.
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
        CriteriaSet<PropertyCriterion<long>>? criteria,
        string property) where TEntity : class
    {
        foreach (var c in criteria?.And ?? [])
        {
            query = query.Where(BuildPredicate<TEntity>(c, property));
        }

        return query;
    }

    /// <summary>
    /// Applies date property criteria to a query.
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
        foreach (var c in criteria?.And ?? [])
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
        foreach (var c in criteria?.And ?? [])
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
        foreach (var c in criteria?.And ?? [])
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
    /// Applies a case-insensitive string criterion to a selected column of a query.
    /// </summary>
    /// <typeparam name="T">
    /// The entity type of the query.
    /// </typeparam>
    /// <param name="query">
    /// The query to filter.
    /// </param>
    /// <param name="criterion">
    /// The criterion to apply to the column.
    /// </param>
    /// <param name="selector">
    /// The selector of the column to filter on.
    /// </param>
    public static IQueryable<T> ApplyStringPredicate<T>(
        this IQueryable<T> query,
        PropertyCriterion<string> criterion,
        Expression<Func<T, string?>> selector)
    {
        return query.Where(
            BuildStringPredicate<T>(criterion, selector.Parameters[0], selector.Body, ignoreCase: true));
    }

    /// <summary>
    /// Converts a negative string criterion into its positive form.
    /// </summary>
    /// <param name="criterion">
    /// The criterion to convert.
    /// </param>
    public static PropertyCriterion<string> ToInnerPositive(this PropertyCriterion<string> criterion) =>
        criterion.Operator switch
        {
            CriteriaValueOperator.NotContainsString =>
                criterion with { Operator = CriteriaValueOperator.ContainsString },
            CriteriaValueOperator.NotEquals => criterion with { Operator = CriteriaValueOperator.Equals },
            _ => criterion,
        };

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

    private static Expression<Func<T, bool>> BuildStringPredicate<T>(
        PropertyCriterion<string> criterion,
        ParameterExpression parameter,
        Expression accessor,
        bool ignoreCase)
    {
        var value = ignoreCase ? criterion.Value?.ToLowerInvariant() : criterion.Value;
        var values = ignoreCase
            ? criterion.Values?.Select(item => item.ToLowerInvariant()).ToList()
            : criterion.Values?.ToList();
        Expression<Func<string?, bool>> test = criterion.Operator switch
        {
            CriteriaValueOperator.Equals => v => value != null && v != null && v == value,
            CriteriaValueOperator.NotEquals => v => value == null || v == null || v != value,
            CriteriaValueOperator.In => v => values != null && v != null && values.Contains(v),
            CriteriaValueOperator.NotIn => v => values == null || v == null || !values.Contains(v),
            CriteriaValueOperator.ContainsString => v => value != null && v != null && v.Contains(value),
            CriteriaValueOperator.NotContainsString => v => value == null || v == null || !v.Contains(value),
            CriteriaValueOperator.StartsWithString => v => value != null && v != null && v.StartsWith(value),
            CriteriaValueOperator.NotStartsWithString => v => value == null || v == null || !v.StartsWith(value),
            CriteriaValueOperator.EndsWithString => v => value != null && v != null && v.EndsWith(value),
            CriteriaValueOperator.NotEndsWithString => v => value == null || v == null || !v.EndsWith(value),
            CriteriaValueOperator.IsNull => v => v == null,
            CriteriaValueOperator.IsNotNull => v => v != null,
            var op => throw new NotSupportedException($"Operator {op} is not supported for string fields")
        };

        Expression<Func<string, string>> lower = v => v.ToLower();
        var compared = ignoreCase
            ? ReplacingExpressionVisitor.Replace(lower.Parameters[0], accessor, lower.Body)
            : accessor;
        var body = ReplacingExpressionVisitor.Replace(test.Parameters[0], compared, test.Body);
        return Expression.Lambda<Func<T, bool>>(body, parameter);
    }

    private static Expression<Func<TEntity, bool>> BuildStringPredicate<TEntity>(
        PropertyCriterion<string> criterion,
        string property,
        bool ignoreCase) where TEntity : class
    {
        var parameter = Expression.Parameter(typeof(TEntity), "e");
        var accessor = Expression.Call(
            typeof(EF),
            nameof(EF.Property),
            [typeof(string)],
            parameter,
            Expression.Constant(property));
        return BuildStringPredicate<TEntity>(criterion, parameter, accessor, ignoreCase);
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
        PropertyCriterion<int> criterion,
        string property) where TEntity : class
    {
        var values = criterion.Values?.ToList();
        return criterion.Operator switch
        {
            CriteriaValueOperator.Equals => e => EF.Property<int?>(e, property) == criterion.Value,
            CriteriaValueOperator.NotEquals => e => EF.Property<int?>(e, property) != criterion.Value,
            CriteriaValueOperator.LessThan => e => EF.Property<int?>(e, property) < criterion.Value,
            CriteriaValueOperator.LessThanOrEqual => e => EF.Property<int?>(e, property) <= criterion.Value,
            CriteriaValueOperator.GreaterThan => e => EF.Property<int?>(e, property) > criterion.Value,
            CriteriaValueOperator.GreaterThanOrEqual => e => EF.Property<int?>(e, property) >= criterion.Value,
            CriteriaValueOperator.IsNull => e => EF.Property<int?>(e, property) == null,
            CriteriaValueOperator.IsNotNull => e => EF.Property<int?>(e, property) != null,
            CriteriaValueOperator.In => e =>
                EF.Property<int?>(e, property) != null &&
                values != null &&
                values.Contains(EF.Property<int?>(e, property)!.Value),
            CriteriaValueOperator.NotIn => e =>
                EF.Property<int?>(e, property) == null ||
                values == null ||
                !values.Contains(EF.Property<int?>(e, property)!.Value),
            var op => throw new NotSupportedException($"Operator {op} is not supported for integer fields")
        };
    }

    private static Expression<Func<TEntity, bool>> BuildPredicate<TEntity>(
        PropertyCriterion<long> criterion,
        string property) where TEntity : class
    {
        return criterion.Operator switch
        {
            CriteriaValueOperator.LessThan => e => EF.Property<long>(e, property) < criterion.Value,
            CriteriaValueOperator.LessThanOrEqual => e => EF.Property<long>(e, property) <= criterion.Value,
            CriteriaValueOperator.GreaterThan => e => EF.Property<long>(e, property) > criterion.Value,
            CriteriaValueOperator.GreaterThanOrEqual => e => EF.Property<long>(e, property) >= criterion.Value,
            _ => BuildPredicate<TEntity, long>(criterion, property)
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
