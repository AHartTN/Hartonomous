namespace Hartonomous.Core.Application.Commands.BPETokens;

public sealed record MergeBPETokensResponse
{
    public required Guid TokenId { get; init; }
    public required string TokenHash { get; init; }
    public required int MergeLevel { get; init; }
    public required int SequenceLength { get; init; }
}
