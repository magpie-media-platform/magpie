using System;
using System.Linq;
using System.Linq.Expressions;

using Magpie.Models.Enums;
using Magpie.Models.Filtering;

using Microsoft.EntityFrameworkCore;

namespace Magpie.Core.Repositories;

internal static class QueryableExtensions
{
    public static IQueryable<TEntity> ApplyAllStructCriteria<TEntity, TProperty>(
        this IQueryable<TEntity> query,
        AllCriteria<PropertyCriterion<TProperty>>? criteria,
        string property)
            where TEntity : class
            where TProperty : struct
    {
        if (criteria is not { All.Count: > 0 })
        {
            return query;
        }

        foreach (var c in criteria.All)
        {
            query = query.Where(BuildStructPredicate<TEntity, TProperty>(c, property));
        }

        return query;
    }

    public static IQueryable<TEntity> ApplyAllEquatableCriteria<TEntity, TProperty>(
        this IQueryable<TEntity> query,
        AllCriteria<PropertyCriterion<TProperty>>? criteria,
        string property)
            where TEntity : class
            where TProperty : IEquatable<TProperty>
    {
        if (criteria is not { All.Count: > 0 })
        {
            return query;
        }

        foreach (var c in criteria.All)
        {
            query = query.Where(BuildEquatablePredicate<TEntity, TProperty>(c, property));
        }

        return query;
    }

    public static IQueryable<TEntity> ApplyAllComparableCriteria<TEntity, TProperty>(
        this IQueryable<TEntity> query,
        AllCriteria<PropertyCriterion<TProperty>>? criteria,
        string property)
            where TEntity : class
            where TProperty : IComparable<TProperty>
    {
        if (criteria is not { All.Count: > 0 })
        {
            return query;
        }

        foreach (var c in criteria.All)
        {
            query = query.Where(BuildComparablePredicate<TEntity, TProperty>(c, property));
        }

        return query;
    }

    public static IQueryable<TEntity> ApplyAllStringCriteria<TEntity>(
        this IQueryable<TEntity> query,
        AllCriteria<PropertyCriterion<string>>? criteria,
        string property) where TEntity : class
    {
        if (criteria is not { All.Count: > 0 })
        {
            return query;
        }

        foreach (var c in criteria.All)
        {
            query = query.Where(BuildStringPredicate<TEntity>(c, property));
        }

        return query;
    }

    public static Expression<Func<TEntity, bool>> BuildStructPredicate<TEntity, TProperty>(
        PropertyCriterion<TProperty> criterion,
        string property)
            where TEntity : class
            where TProperty : struct
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
            _ => throw new NotSupportedException(
                $"Operator {criterion.Operator} is not supported for {typeof(TProperty).Name}")
        };
    }

    public static Expression<Func<TEntity, bool>> BuildEquatablePredicate<TEntity, TProperty>(
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
            _ => throw new NotSupportedException(
                $"Operator {criterion.Operator} is not supported for {typeof(TProperty).Name}")
        };
    }
    public static Expression<Func<TEntity, bool>> BuildComparablePredicate<TEntity, TProperty>(
        PropertyCriterion<TProperty> criterion,
        string property)
            where TEntity : class
            where TProperty : IComparable<TProperty>
    {
        return criterion.Operator switch
        {
            CriteriaValueOperator.Equals => e =>
                EF.Property<TProperty>(e, property).Equals(criterion.Value),
            CriteriaValueOperator.NotEquals => e =>
                !EF.Property<TProperty>(e, property).Equals(criterion.Value),
            CriteriaValueOperator.LessThan => e =>
                EF.Property<TProperty>(e, property).CompareTo(criterion.Value) < 0,
            CriteriaValueOperator.LessThanOrEqual => e =>
                EF.Property<TProperty>(e, property).CompareTo(criterion.Value) <= 0,
            CriteriaValueOperator.GreaterThan => e =>
                EF.Property<TProperty>(e, property).CompareTo(criterion.Value) > 0,
            CriteriaValueOperator.GreaterThanOrEqual => e =>
                EF.Property<TProperty>(e, property).CompareTo(criterion.Value) >= 0,
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
            _ => throw new NotSupportedException(
                $"Operator {criterion.Operator} is not supported for {typeof(TProperty).Name}")
        };
    }

    public static Expression<Func<TEntity, bool>> BuildStringPredicate<TEntity>(
        PropertyCriterion<string> criterion,
        string property) where TEntity : class
    {
        return criterion.Operator switch
        {
            CriteriaValueOperator.Equals => e =>
                EF.Property<string>(e, property) == criterion.Value,
            CriteriaValueOperator.NotEquals => e =>
                EF.Property<string>(e, property) != criterion.Value,
            CriteriaValueOperator.IsNull => e =>
                EF.Property<string>(e, property) == null,
            CriteriaValueOperator.IsNotNull => e =>
                EF.Property<string>(e, property) != null,
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
            CriteriaValueOperator.In => e =>
                criterion.Values != null &&
                EF.Property<string>(e, property) != null &&
                criterion.Values.Contains(EF.Property<string>(e, property)),
            CriteriaValueOperator.NotIn => e =>
                criterion.Values == null ||
                EF.Property<string>(e, property) == null ||
                !criterion.Values.Contains(EF.Property<string>(e, property)),
            _ => throw new NotSupportedException($"Operator {criterion.Operator} is not supported for strings")
        };
    }
}
