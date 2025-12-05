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
