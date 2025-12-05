namespace Hartonomous.Core.Application.Queries.BPETokens;

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
