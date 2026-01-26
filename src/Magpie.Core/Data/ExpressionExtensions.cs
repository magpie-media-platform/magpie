using System;
using System.Collections.Generic;
using System.Linq.Expressions;

namespace Magpie.Core.Data;

/// <summary>
/// Extension methods for building dynamic LINQ expressions.
/// </summary>
internal static class ExpressionExtensions
{
    /// <summary>
    /// Combines a property selector with a predicate to create a composite expression.
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <typeparam name="TProperty">The property type.</typeparam>
    /// <param name="selector">The property selector expression.</param>
    /// <param name="predicate">The predicate to apply to the selected property.</param>
    public static Expression<Func<TEntity, bool>> CombineWithPredicate<TEntity, TProperty>(
        this Expression<Func<TEntity, TProperty>> selector,
        Expression<Func<TProperty, bool>> predicate)
    {
        if (selector.Parameters is not [var selectorParam, ..] || predicate.Parameters is not [var predicateParam, ..])
        {
            throw new ArgumentException("Expressions must have at least one parameter.");
        }

        var predicateBody = new ParameterReplacer(predicateParam, selector.Body).Visit(predicate.Body);

        return Expression.Lambda<Func<TEntity, bool>>(predicateBody!, selectorParam);
    }

    /// <summary>
    /// Combines multiple predicates with OR logic.
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <param name="predicates">The predicates to combine.</param>
    public static Expression<Func<TEntity, bool>> OrAll<TEntity>(
        this IReadOnlyList<Expression<Func<TEntity, bool>>> predicates)
    {
        if (predicates is [])
        {
            return _ => true;
        }

        if (predicates is [var single])
        {
            return single;
        }

        var parameter = Expression.Parameter(typeof(TEntity), "e");

        Expression? combined = null;

        foreach (var predicate in predicates)
        {
            if (predicate.Parameters is not [var predicateParam, ..])
            {
                continue;
            }
            var body = new ParameterReplacer(predicateParam, parameter).Visit(predicate.Body);

            combined = combined is null ? body : Expression.OrElse(combined, body!);
        }

        return Expression.Lambda<Func<TEntity, bool>>(combined!, parameter);
    }

    private sealed class ParameterReplacer(Expression from, Expression to) : ExpressionVisitor
    {
        public override Expression? Visit(Expression? node)
        {
            return node == from ? to : base.Visit(node);
        }
    }
}
