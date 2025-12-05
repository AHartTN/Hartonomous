using Hartonomous.Core.Application.Common;
using Hartonomous.Core.Application.Interfaces;
using Hartonomous.Core.Domain.Entities;
using Hartonomous.Core.Domain.Utilities;
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
        // Create spatial coordinate from input
        // Assuming input X, Y, Z are raw values. M is defaulted to 0.
        // We map them to the coordinate system.
        // NOTE: The command likely takes double, but FromUniversalProperties takes uint/int.
        // We cast for now.
        var center = SpatialCoordinate.FromUniversalProperties(
            (uint)request.CenterX, 
            (int)request.CenterY, // Assuming Y is mapped to Entropy in input
            (int)request.CenterZ, // Assuming Z is Compressibility
            0);                   // M = Connectivity = 0

        // Determine the Hilbert Tile for this coordinate
        // We'll use a default level, or maybe the command should specify it?
        // For "manual" creation, let's assume Level 10 (coarse).
        int level = 10;
        
        var (prefixHigh, prefixLow) = Hartonomous.Core.Domain.Utilities.HilbertCurve4D.GetHilbertTileId(
            center.HilbertHigh, center.HilbertLow, level);

        // Check if this specific tile landmark already exists
        // Name is derived: "H:XXXX-XXXX_L10"
        // The user-provided 'Name' (e.g., "Home") is now just a Label/Description?
        // Or we might need a separate mapping.
        // For now, let's append the user name to the description.
        string desc = string.IsNullOrEmpty(request.Description) 
            ? $"User named: {request.Name}" 
            : $"{request.Description} (User named: {request.Name})";

        // Create landmark (or get existing if we were doing that, but here we try to create)
        // If it exists, the repo unique constraint on Name might fail?
        // Actually, the new Name is derived. If the derived name exists, we fail.
        // But the user passed 'request.Name'.
        // In the new paradigm, 'request.Name' effectively becomes an alias.
        // But `Landmark.Name` property IS the ID.
        // We should probably allow `Landmark.Create` to take an alias?
        // But `Landmark.Name` is `private set`.
        
        // Let's assume we create the standard deterministic landmark.
        var landmark = Landmark.Create(
            hilbertPrefixHigh: prefixHigh,
            hilbertPrefixLow: prefixLow,
            level: level,
            description: desc
        );

        // If a landmark for this tile already exists, we should probably update it?
        // The `GetByNameAsync` check above used `request.Name`.
        // Now the name is `landmark.Name`.
        var existingTile = await _repository.GetByNameAsync(landmark.Name, cancellationToken);
        if (existingTile != null)
        {
             return Result<CreateLandmarkResponse>.Failure($"Landmark for this tile ({landmark.Name}) already exists.");
        }

        await _repository.AddAsync(landmark, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result<CreateLandmarkResponse>.Success(new CreateLandmarkResponse
        {
            LandmarkId = landmark.Id,
            Name = landmark.Name // Returns the SYSTEM name (H:...)
        });
    }
}
