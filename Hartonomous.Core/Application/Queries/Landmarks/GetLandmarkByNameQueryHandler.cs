using Hartonomous.Core.Application.Common;
using Hartonomous.Core.Application.Interfaces;
using MediatR;

namespace Hartonomous.Core.Application.Queries.Landmarks;

public sealed class GetLandmarkByNameQueryHandler : IRequestHandler<GetLandmarkByNameQuery, Result<LandmarkDto?>>
{
    private readonly ILandmarkRepository _repository;

    public GetLandmarkByNameQueryHandler(ILandmarkRepository repository)
    {
        _repository = repository;
    }

    public async Task<Result<LandmarkDto?>> Handle(GetLandmarkByNameQuery request, CancellationToken cancellationToken)
    {
        try
        {
            var landmark = await _repository.GetByNameAsync(request.Name, cancellationToken);

            if (landmark == null)
            {
                return Result<LandmarkDto?>.Success(null);
            }

            var dto = new LandmarkDto
            {
                Id = landmark.Id,
                Name = landmark.Name,
                Description = landmark.Description,
                Center = new SpatialCoordinateDto
                {
                    X = landmark.Center.X,
                    Y = landmark.Center.Y,
                    Z = landmark.Center.Z
                },
                Radius = landmark.Radius,
                ConstantCount = (int)landmark.ConstantCount,
                Density = landmark.Density,
                IsActive = landmark.IsActive,
                CreatedAt = landmark.CreatedAt
            };

            return Result<LandmarkDto?>.Success(dto);
        }
        catch (Exception ex)
        {
            return Result<LandmarkDto?>.Failure($"Failed to retrieve landmark: {ex.Message}");
        }
    }
}
