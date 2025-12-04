using Hartonomous.Core.Application.Common;

namespace Hartonomous.Core.Application.Commands.Constants;

/// <summary>
/// Command to activate a constant for use
/// </summary>
public sealed record ActivateConstantCommand : ICommand<Result>
{
    public required Guid ConstantId { get; init; }
}
