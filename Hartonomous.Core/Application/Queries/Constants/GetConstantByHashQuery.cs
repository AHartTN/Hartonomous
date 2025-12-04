using Hartonomous.Core.Application.Common;
using Hartonomous.Core.Domain.Enums;

namespace Hartonomous.Core.Application.Queries.Constants;

/// <summary>
/// Query to get a constant by its hash
/// </summary>
public sealed record GetConstantByHashQuery : IQuery<Result<ConstantDto?>>
{
    public required string Hash { get; init; }
}

public sealed record ConstantDto
{
    public required Guid Id { get; init; }
    public required string Hash { get; init; }
    public required int Size { get; init; }
    public required ContentType ContentType { get; init; }
    public required ConstantStatus Status { get; init; }
    public required SpatialCoordinateDto SpatialCoordinate { get; init; }
    public required int ReferenceCount { get; init; }
    public required long Frequency { get; init; }
    public required DateTime CreatedAt { get; init; }
    public DateTime? ProjectedAt { get; init; }
    public DateTime? ActivatedAt { get; init; }
    public DateTime? LastAccessedAt { get; init; }
}

public sealed record SpatialCoordinateDto
{
    public required double X { get; init; }
    public required double Y { get; init; }
    public required double Z { get; init; }
}
