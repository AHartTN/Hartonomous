using Hartonomous.Core.Application.Common;

namespace Hartonomous.Core.Application.Commands.BPETokens;

/// <summary>
/// Command to merge a byte pair
/// </summary>
public sealed record MergeBytePairCommand : ICommand<Result<MergeBytePairCommandResult>>
{
    public required byte FirstByte { get; init; }
    public required byte SecondByte { get; init; }
    public required int MergeLevel { get; init; }
    public required long Frequency { get; init; }
}

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
