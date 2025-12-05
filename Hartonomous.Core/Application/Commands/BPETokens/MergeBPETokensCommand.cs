using Hartonomous.Core.Application.Common;

namespace Hartonomous.Core.Application.Commands.BPETokens;

/// <summary>
/// Command to merge two constants or tokens into a new BPE token
/// </summary>
public sealed record MergeBPETokensCommand : ICommand<Result<MergeBPETokensResponse>>
{
    public required List<Guid> ConstantSequence { get; init; }
}
