using Hartonomous.Core.Application.Common;
using Hartonomous.Core.Application.Interfaces;
using Hartonomous.Core.Domain.ValueObjects;
using MediatR;

namespace Hartonomous.Core.Application.Queries.Constants;

public sealed class GetNearbyConstantsQueryHandler : IRequestHandler<GetNearbyConstantsQuery, Result<List<ConstantDto>>>
{
    private readonly IConstantRepository _repository;

    public GetNearbyConstantsQueryHandler(IConstantRepository repository)
    {
        _repository = repository;
    }

    public async Task<Result<List<ConstantDto>>> Handle(GetNearbyConstantsQuery request, CancellationToken cancellationToken)
    {
        try
        {
            var coordinate = SpatialCoordinate.FromUniversalProperties(
                (uint)request.X,
                1_048_576, // placeholder entropy
                1_048_576, // placeholder compressibility
                0);        // no connectivity yet
            var constants = await _repository.GetKNearestConstantsAsync(coordinate, request.K, cancellationToken);

            var dtos = constants.Select(c => new ConstantDto
            {
                Id = c.Id,
                Hash = c.Hash.ToString(),
                Size = c.Size,
                ContentType = c.ContentType,
                Status = c.Status,
                SpatialCoordinate = new SpatialCoordinateDto
                {
                    X = c.Coordinate?.X ?? 0,
                    Y = c.Coordinate?.Y ?? 0,
                    Z = c.Coordinate?.Z ?? 0
                },
                ReferenceCount = (int)c.ReferenceCount,
                Frequency = c.Frequency,
                CreatedAt = c.CreatedAt,
                ProjectedAt = c.ProjectedAt,
                ActivatedAt = c.ActivatedAt,
                LastAccessedAt = c.LastAccessedAt
            }).ToList();

            return Result<List<ConstantDto>>.Success(dtos);
        }
        catch (Exception ex)
        {
            return Result<List<ConstantDto>>.Failure($"Failed to retrieve nearby constants: {ex.Message}");
        }
    }
}
