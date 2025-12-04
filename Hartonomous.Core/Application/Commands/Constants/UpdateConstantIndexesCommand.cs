using Hartonomous.Core.Application.Common;
using MediatR;

namespace Hartonomous.Core.Application.Commands.Constants;

/// <summary>
/// Command to update spatial indexes for a batch of constants.
/// </summary>
public sealed class UpdateConstantIndexesCommand : IRequest<Result<int>>
{
    public List<Guid> ConstantIds { get; set; } = new();
    public int BatchSize { get; set; } = 1000;
}
