using Hartonomous.Core.Domain.Entities;

namespace Hartonomous.Infrastructure.Services.BPE;

/// <summary>
/// Represents a pair of neighboring constants discovered through Voronoi tessellation
/// </summary>
public record ConstantPair
{
    /// <summary>First constant in the pair</summary>
    public required Guid ConstantId1 { get; init; }
    
    /// <summary>Second constant in the pair</summary>
    public required Guid ConstantId2 { get; init; }
    
    /// <summary>Euclidean distance between the constants in 3D space</summary>
    public double Distance3D { get; init; }
    
    /// <summary>Hilbert distance between the constants</summary>
    public ulong HilbertDistance { get; init; }
    
    /// <summary>Frequency this pair appears in the data (for BPE merging)</summary>
    public int Frequency { get; init; } = 1;
    
    /// <summary>Whether this pair represents a Voronoi neighbor relationship</summary>
    public bool IsVoronoiNeighbor { get; init; }
}
