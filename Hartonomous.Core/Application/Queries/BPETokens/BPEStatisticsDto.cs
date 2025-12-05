namespace Hartonomous.Core.Application.Queries.BPETokens;

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
