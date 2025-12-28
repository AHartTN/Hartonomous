using Hartonomous.Core.Models;

namespace Hartonomous.Core.Services.Abstractions;

/// <summary>
/// Trajectory operations - RLE-compressed paths through semantic space.
/// "Hello" → H(1), e(1), l(2), o(1) - NOT 5 separate records.
/// </summary>
public interface ITrajectoryService
{
    /// <summary>
    /// Build RLE-compressed trajectory from text.
    /// Repeated characters are compressed: "aaa" → a(3).
    /// </summary>
    TrajectoryPoint[] BuildTrajectory(string text);

    /// <summary>
    /// Store trajectory as relationship between nodes.
    /// The trajectory is stored as a LineStringZM geometry.
    /// </summary>
    void StoreTrajectory(
        NodeId source,
        NodeId target,
        TrajectoryPoint[] points,
        double weight,
        RelationshipType type = RelationshipType.SemanticLink,
        NodeId context = default);

    /// <summary>
    /// Convert trajectory back to text (expanding RLE).
    /// </summary>
    string TrajectoryToText(TrajectoryPoint[] points);
}
