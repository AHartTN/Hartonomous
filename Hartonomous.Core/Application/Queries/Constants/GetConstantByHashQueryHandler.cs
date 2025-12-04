using Hartonomous.Core.Application.Common;
using Hartonomous.Core.Application.Interfaces;
using Hartonomous.Core.Domain.ValueObjects;
using MediatR;

namespace Hartonomous.Core.Application.Queries.Constants;

public sealed class GetConstantByHashQueryHandler : IRequestHandler<GetConstantByHashQuery, Result<ConstantDto?>>
{
    private readonly IConstantRepository _repository;

    public GetConstantByHashQueryHandler(IConstantRepository repository)
    {
        _repository = repository;
    }

    public async Task<Result<ConstantDto?>> Handle(GetConstantByHashQuery request, CancellationToken cancellationToken)
    {
        try
        {
            var hash = Hash256.FromHex(request.Hash);
            var constant = await _repository.GetByHashAsync(hash, cancellationToken);

            if (constant == null)
            {
                return Result<ConstantDto?>.Success(null);
            }

            var dto = new ConstantDto
            {
                Id = constant.Id,
                Hash = constant.Hash.ToString(),
                Size = constant.Size,
                ContentType = constant.ContentType,
                Status = constant.Status,
                SpatialCoordinate = new SpatialCoordinateDto
                {
                    X = constant.Coordinate?.X ?? 0,
                    Y = constant.Coordinate?.Y ?? 0,
                    Z = constant.Coordinate?.Z ?? 0
                },
                ReferenceCount = (int)constant.ReferenceCount,
                Frequency = constant.Frequency,
                CreatedAt = constant.CreatedAt,
                ProjectedAt = constant.ProjectedAt,
                ActivatedAt = constant.ActivatedAt,
                LastAccessedAt = constant.LastAccessedAt
            };

            return Result<ConstantDto?>.Success(dto);
        }
        catch (Exception ex)
        {
            return Result<ConstantDto?>.Failure($"Failed to retrieve constant: {ex.Message}");
        }
    }
}
