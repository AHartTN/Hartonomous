using Hartonomous.Core.Application.Common;

namespace Hartonomous.Core.Application.Commands.Landmarks;

/// <summary>
/// Command to update landmark properties
/// </summary>
public sealed record UpdateLandmarkCommand : ICommand<Result>
{
    public required string Name { get; init; }
    public string? Description { get; init; }
    public bool? IsActive { get; init; }
}
