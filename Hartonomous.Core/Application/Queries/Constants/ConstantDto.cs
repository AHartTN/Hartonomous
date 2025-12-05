using Hartonomous.Core.Application.Queries;
using Hartonomous.Core.Domain.Enums;

namespace Hartonomous.Core.Application.Queries.Constants;

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
