namespace Hartonomous.Core.Services;

/// <summary>
/// Represents a point in 4-dimensional unsigned coordinate space.
/// Used as the result of Hilbert curve index-to-coordinates conversions.
/// </summary>
/// <param name="X">The X coordinate component.</param>
/// <param name="Y">The Y coordinate component.</param>
/// <param name="Z">The Z coordinate component.</param>
/// <param name="W">The W coordinate component (fourth dimension).</param>
public readonly record struct SpatialCoordinates4D(uint X, uint Y, uint Z, uint W);
