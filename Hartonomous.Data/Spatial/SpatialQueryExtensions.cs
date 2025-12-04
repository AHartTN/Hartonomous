using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;

namespace Hartonomous.Data.Spatial;

/// <summary>
/// Extension methods for spatial queries with PostGIS
/// </summary>
public static class SpatialQueryExtensions
{
    /// <summary>
    /// Find entities within a radius (in meters) of a point
    /// </summary>
    public static IQueryable<T> WithinRadius<T>(
        this IQueryable<T> query,
        Point center,
        double radiusInMeters) where T : SpatialEntity
    {
        return query.Where(e =>
            e.Location != null &&
            e.Location.Distance(center) <= radiusInMeters);
    }

    /// <summary>
    /// Find entities within a bounding box
    /// </summary>
    public static IQueryable<T> WithinBounds<T>(
        this IQueryable<T> query,
        double minLon,
        double minLat,
        double maxLon,
        double maxLat) where T : SpatialEntity
    {
        var geometryFactory = new GeometryFactory(new PrecisionModel(), 4326);
        var boundingBox = geometryFactory.CreatePolygon(new Coordinate[]
        {
            new(minLon, minLat),
            new(maxLon, minLat),
            new(maxLon, maxLat),
            new(minLon, maxLat),
            new(minLon, minLat)
        });

        return query.Where(e =>
            e.Location != null &&
            boundingBox.Contains(e.Location));
    }

    /// <summary>
    /// Find entities that intersect with a geometry
    /// </summary>
    public static IQueryable<T> Intersects<T>(
        this IQueryable<T> query,
        Geometry geometry) where T : SpatialEntity
    {
        return query.Where(e =>
            e.SpatialData != null &&
            e.SpatialData.Intersects(geometry));
    }

    /// <summary>
    /// Order by distance from a point (nearest first)
    /// </summary>
    public static IQueryable<T> OrderByDistanceFrom<T>(
        this IQueryable<T> query,
        Point point) where T : SpatialEntity
    {
        return query
            .Where(e => e.Location != null)
            .OrderBy(e => e.Location!.Distance(point));
    }
}
