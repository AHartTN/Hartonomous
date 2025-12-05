namespace Hartonomous.Core.Application.Queries.Constants;

/// <summary>
/// Nearest constant DTO with distance
/// </summary>
public sealed record NearestConstantDto
{
    public required Guid Id { get; init; }
    public required string Hash { get; init; }
    public required double Distance { get; init; }
    public required double X { get; init; }
    public required double Y { get; init; }
    public required double Z { get; init; }
}
