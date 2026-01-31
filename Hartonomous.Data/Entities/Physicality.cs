using System;
using Hartonomous.Core.Primitives;
using NetTopologySuite.Geometries;

namespace Hartonomous.Data.Entities;

public class Physicality
{
    public HartonomousId Id { get; set; }
    
    // Hilbert index stored as UINT128 (mapped to numeric(39,0))
    public UInt128 Hilbert { get; set; }
    
    // 4D Spatial data (POINTZM)
    public Point Centroid { get; set; } = Point.Empty;
    
    // 4D Trajectory data (GEOMETRYZM)
    public Geometry? Trajectory { get; set; }
    
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}