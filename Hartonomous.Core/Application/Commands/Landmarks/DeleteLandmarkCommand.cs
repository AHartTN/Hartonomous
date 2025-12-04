using Hartonomous.Core.Application.Common;

namespace Hartonomous.Core.Application.Commands.Landmarks;

/// <summary>
/// Command to delete (soft delete) a landmark
/// </summary>
public sealed record DeleteLandmarkCommand : ICommand<Result>
{
    public required string Name { get; init; }
}
