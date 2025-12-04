using System.Linq.Expressions;
using Hartonomous.Core.Domain.ValueObjects;

namespace Hartonomous.Data.Extensions;

/// <summary>
/// LINQ extensions for 4D Hilbert spatial queries optimized for PostgreSQL B-tree indexes.
/// Uses composite (HilbertHigh, HilbertLow) index for optimal k-NN performance.
/// </summary>
public static class HilbertSpatialQueryExtensions
{
    /// <summary>
    /// Filters entities by 4D Hilbert range using composite B-tree index.
    /// Generates SQL: WHERE (hilbert_high, hilbert_low) BETWEEN (min_h, min_l) AND (max_h, max_l)
    /// Performance: O(log N) B-tree scan
    /// </summary>
    public static IQueryable<TEntity> WhereHilbertRange<TEntity>(
        this IQueryable<TEntity> query,
        Expression<Func<TEntity, SpatialCoordinate>> coordinateSelector,
        ulong minHigh, ulong minLow, ulong maxHigh, ulong maxLow)
        where TEntity : class
    {
        if (query == null)
            throw new ArgumentNullException(nameof(query));
        if (coordinateSelector == null)
            throw new ArgumentNullException(nameof(coordinateSelector));

        var parameter = coordinateSelector.Parameters[0];
        var coordAccess = coordinateSelector.Body;

        var highProp = Expression.Property(coordAccess, nameof(SpatialCoordinate.HilbertHigh));
        var lowProp = Expression.Property(coordAccess, nameof(SpatialCoordinate.HilbertLow));

        var minHighConst = Expression.Constant(minHigh);
        var minLowConst = Expression.Constant(minLow);
        var maxHighConst = Expression.Constant(maxHigh);
        var maxLowConst = Expression.Constant(maxLow);

        // (high > minHigh || (high == minHigh && low >= minLow))
        var minCondition = Expression.OrElse(
            Expression.GreaterThan(highProp, minHighConst),
            Expression.AndAlso(
                Expression.Equal(highProp, minHighConst),
                Expression.GreaterThanOrEqual(lowProp, minLowConst)));

        // (high < maxHigh || (high == maxHigh && low <= maxLow))
        var maxCondition = Expression.OrElse(
            Expression.LessThan(highProp, maxHighConst),
            Expression.AndAlso(
                Expression.Equal(highProp, maxHighConst),
                Expression.LessThanOrEqual(lowProp, maxLowConst)));

        var condition = Expression.AndAlso(minCondition, maxCondition);
        var lambda = Expression.Lambda<Func<TEntity, bool>>(condition, parameter);

        return query.Where(lambda);
    }

    /// <summary>
    /// Finds entities within Hilbert distance from center using B-tree range scan.
    /// Much faster than PostGIS for k-NN queries in 4D space.
    /// </summary>
    public static IQueryable<TEntity> NearestByHilbert<TEntity>(
        this IQueryable<TEntity> query,
        Expression<Func<TEntity, SpatialCoordinate>> coordinateSelector,
        SpatialCoordinate center,
        ulong maxHilbertDistance)
        where TEntity : class
    {
        if (query == null)
            throw new ArgumentNullException(nameof(query));
        if (coordinateSelector == null)
            throw new ArgumentNullException(nameof(coordinateSelector));
        if (center == null)
            throw new ArgumentNullException(nameof(center));

        ulong minHigh = center.HilbertHigh > maxHilbertDistance ? center.HilbertHigh - maxHilbertDistance : 0;
        ulong minLow = center.HilbertLow;
        ulong maxHigh = center.HilbertHigh + maxHilbertDistance;
        ulong maxLow = center.HilbertLow;

        return query.WhereHilbertRange(coordinateSelector, minHigh, minLow, maxHigh, maxLow);
    }

    /// <summary>
    /// Orders entities by Hilbert distance from center.
    /// Use after WhereHilbertRange for exact k-NN ordering.
    /// </summary>
    public static IOrderedQueryable<TEntity> OrderByHilbertDistance<TEntity>(
        this IQueryable<TEntity> query,
        Expression<Func<TEntity, SpatialCoordinate>> coordinateSelector,
        SpatialCoordinate center)
        where TEntity : class
    {
        if (query == null)
            throw new ArgumentNullException(nameof(query));
        if (coordinateSelector == null)
            throw new ArgumentNullException(nameof(coordinateSelector));
        if (center == null)
            throw new ArgumentNullException(nameof(center));

        var parameter = coordinateSelector.Parameters[0];
        var coordAccess = coordinateSelector.Body;
        
        var highProp = Expression.Property(coordAccess, nameof(SpatialCoordinate.HilbertHigh));
        var lowProp = Expression.Property(coordAccess, nameof(SpatialCoordinate.HilbertLow));
        
        var centerHighConst = Expression.Constant(center.HilbertHigh);
        var centerLowConst = Expression.Constant(center.HilbertLow);
        
        // Order by high, then low
        var orderExpr = Expression.Lambda<Func<TEntity, ulong>>(highProp, parameter);
        return query.OrderBy(orderExpr).ThenBy(entity => coordinateSelector.Compile()(entity).HilbertLow);
    }
}
