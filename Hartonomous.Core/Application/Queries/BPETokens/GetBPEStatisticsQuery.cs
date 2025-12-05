using Hartonomous.Core.Application.Common;

namespace Hartonomous.Core.Application.Queries.BPETokens;

/// <summary>
/// Query to get BPE statistics
/// </summary>
public sealed record GetBPEStatisticsQuery : IQuery<Result<BPEStatisticsDto>>
{
}
