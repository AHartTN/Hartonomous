using NetTopologySuite.Geometries;

namespace Hartonomous.Data.Spatial;

/// <summary>
/// Base class for entities with spatial data using PostGIS
/// </summary>
public abstract class SpatialEntity
{
    /// <summary>
    /// Geographic location using SRID 4326 (WGS 84)
    /// </summary>
    public Point? Location { get; set; }

    /// <summary>
    /// Geographic area or region
    /// </summary>
    public Polygon? Area { get; set; }

    /// <summary>
    /// Path or route as a line string
    /// </summary>
    public LineString? Route { get; set; }

    /// <summary>
    /// Generic geometry field for complex spatial data
    /// </summary>
    public Geometry? SpatialData { get; set; }

    /// <summary>
    /// Spatial Reference System Identifier
    /// Default: 4326 (WGS 84 - GPS coordinates)
    /// </summary>
    public int SRID { get; set; } = 4326;
}
