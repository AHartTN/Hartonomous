using Hartonomous.Core.Application.Common;

namespace Hartonomous.Core.Application.Queries.BPETokens;

/// <summary>
/// Query to get BPE statistics
/// </summary>
public sealed record GetBPEStatisticsQuery : IQuery<Result<BPEStatisticsDto>>
{
}

/// <summary>
/// BPE statistics DTO
/// </summary>
public sealed record BPEStatisticsDto
{
    public required int TotalTokens { get; init; }
    public required int MaxMergeLevel { get; init; }
    public required long TotalFrequency { get; init; }
    public required int BaseTokens { get; init; }
    public required int LearnedTokens { get; init; }
}
