using Hartonomous.Core.Application.Common;

namespace Hartonomous.Core.Application.Queries.BPETokens;

/// <summary>
/// Query to get tokens by merge level
/// </summary>
public sealed record GetTokensByMergeLevelQuery : IQuery<Result<IReadOnlyList<BPETokenDto>>>
{
    public required int MergeLevel { get; init; }
}
