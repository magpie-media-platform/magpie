using System;
using System.Linq;
using System.Linq.Expressions;

using Magpie.Models.Enums;
using Magpie.Models.Filtering;

using Microsoft.EntityFrameworkCore;

namespace Magpie.Core.Repositories;

internal static class QueryableExtensions
{
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

    public static IQueryable<TEntity> ApplyEnumCriteriaSet<TEntity, TProperty>(
        this IQueryable<TEntity> query,
        CriteriaSet<PropertyCriterion<TProperty>>? criteria,
        string property)
            where TEntity : class
            where TProperty : struct, Enum
    {
        if (criteria?.And is null || !criteria.And.Any())
        {
            return query;
        }

        foreach (var c in criteria.And)
        {
            query = query.Where(BuildEnumPredicate<TEntity, TProperty>(c, property));
        }

        return query;
    }

    public static Expression<Func<TEntity, bool>> BuildPredicate<TEntity, TProperty>(
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

    public static Expression<Func<TEntity, bool>> BuildPredicate<TEntity>(
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

    public static Expression<Func<TEntity, bool>> BuildPredicate<TEntity>(
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

    public static Expression<Func<TEntity, bool>> BuildPredicate<TEntity>(
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

    public static Expression<Func<TEntity, bool>> BuildPredicate<TEntity>(
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

    public static Expression<Func<TEntity, bool>> BuildEnumPredicate<TEntity, TProperty>(
        PropertyCriterion<TProperty> criterion,
        string property)
            where TEntity : class
            where TProperty : struct, Enum
    {
        return criterion.Operator switch
        {
            CriteriaValueOperator.Equals => e =>
                EF.Property<TProperty>(e, property).Equals(criterion.Value),
            CriteriaValueOperator.NotEquals => e =>
                !EF.Property<TProperty>(e, property).Equals(criterion.Value),
            CriteriaValueOperator.In => e =>
                criterion.Values != null &&
                criterion.Values.Contains(EF.Property<TProperty>(e, property)),
            CriteriaValueOperator.NotIn => e =>
                criterion.Values == null ||
                !criterion.Values.Contains(EF.Property<TProperty>(e, property)),
            var op => throw new NotSupportedException($"Operator {op} is not supported for {typeof(TProperty).Name}")
        };
    }
}
