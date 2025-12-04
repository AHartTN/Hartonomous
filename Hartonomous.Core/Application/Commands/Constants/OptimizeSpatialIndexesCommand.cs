using Hartonomous.Core.Application.Common;
using MediatR;

namespace Hartonomous.Core.Application.Commands.Constants;

/// <summary>
/// Command to optimize spatial indexes (VACUUM, REINDEX, etc.).
/// </summary>
public sealed class OptimizeSpatialIndexesCommand : IRequest<Result<bool>>
{
}
