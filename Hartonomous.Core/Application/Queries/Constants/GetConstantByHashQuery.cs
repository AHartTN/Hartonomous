using Hartonomous.Core.Application.Common;

namespace Hartonomous.Core.Application.Queries.Constants;

/// <summary>
/// Query to get a constant by its hash
/// </summary>
public sealed record GetConstantByHashQuery : IQuery<Result<ConstantDto?>>
{
    public required string Hash { get; init; }
}
