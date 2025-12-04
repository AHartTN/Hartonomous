using Hartonomous.Core.Application.Common;

namespace Hartonomous.Core.Application.Queries.BPETokens;

/// <summary>
/// Query to get BPE token by ID
/// </summary>
public sealed record GetBPETokenByIdQuery : IQuery<Result<BPETokenDto>>
{
    public required int TokenId { get; init; }
}
