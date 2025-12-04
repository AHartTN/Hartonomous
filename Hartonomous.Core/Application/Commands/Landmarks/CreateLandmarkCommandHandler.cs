using Hartonomous.Core.Application.Common;
using Hartonomous.Core.Application.Interfaces;
using Hartonomous.Core.Domain.Entities;
using Hartonomous.Core.Domain.ValueObjects;
using MediatR;

namespace Hartonomous.Core.Application.Commands.Landmarks;

public sealed class CreateLandmarkCommandHandler : IRequestHandler<CreateLandmarkCommand, Result<CreateLandmarkResponse>>
{
    private readonly ILandmarkRepository _repository;
    private readonly IUnitOfWork _unitOfWork;

    public CreateLandmarkCommandHandler(ILandmarkRepository repository, IUnitOfWork unitOfWork)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<CreateLandmarkResponse>> Handle(CreateLandmarkCommand request, CancellationToken cancellationToken)
    {
        // Check if landmark with same name already exists
        var existing = await _repository.GetByNameAsync(request.Name, cancellationToken);
        if (existing != null)
        {
            return Result<CreateLandmarkResponse>.Failure($"Landmark with name '{request.Name}' already exists");
        }

        // Create spatial coordinate
        var center = SpatialCoordinate.Create(request.CenterX, request.CenterY, request.CenterZ);

        // Create landmark
        var landmark = Landmark.Create(
            name: request.Name,
            description: request.Description,
            center: center,
            radius: request.Radius
        );

        await _repository.AddAsync(landmark, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result<CreateLandmarkResponse>.Success(new CreateLandmarkResponse
        {
            LandmarkId = landmark.Id,
            Name = landmark.Name
        });
    }
}
