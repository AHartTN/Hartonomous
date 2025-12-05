using Hartonomous.Core.Application.Common;

namespace Hartonomous.Core.Application.Queries.Constants;

/// <summary>
/// Query to get constant statistics
/// </summary>
public sealed record GetConstantStatisticsQuery : IQuery<Result<ConstantStatisticsDto>>
{
}
