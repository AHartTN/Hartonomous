using Hartonomous.Core.Application.Common;

namespace Hartonomous.Core.Application.Queries.BPETokens;

/// <summary>
/// Query to get BPE vocabulary with pagination
/// </summary>
public sealed record GetBPEVocabularyQuery : IQuery<Result<BPEVocabularyResponse>>
{
    public int PageNumber { get; init; } = 1;
    public int PageSize { get; init; } = 100;
    public int? MinFrequency { get; init; }
    public int? MergeLevel { get; init; }
}

public sealed record BPEVocabularyResponse
{
    public required List<BPETokenDto> Tokens { get; init; }
    public required int TotalCount { get; init; }
    public required int PageNumber { get; init; }
    public required int PageSize { get; init; }
    public required int TotalPages { get; init; }
}

public sealed record BPETokenDto
{
    public required Guid Id { get; init; }
    public required Guid TokenId { get; init; }
    public required string Hash { get; init; }
    public required List<Guid> ConstantSequence { get; init; }
    public required int SequenceLength { get; init; }
    public required int MergeLevel { get; init; }
    public required long Frequency { get; init; }
    public int? VocabularyRank { get; init; }
    public required DateTime CreatedAt { get; init; }
}
