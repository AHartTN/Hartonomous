namespace Hartonomous.Core.Application.Commands.BPETokens;

/// <summary>
/// Result of merging a byte pair
/// </summary>
public sealed record MergeBytePairCommandResult
{
    public required int TokenId { get; init; }
    public required byte FirstByte { get; init; }
    public required byte SecondByte { get; init; }
    public required int MergeLevel { get; init; }
    public required long Frequency { get; init; }
}
