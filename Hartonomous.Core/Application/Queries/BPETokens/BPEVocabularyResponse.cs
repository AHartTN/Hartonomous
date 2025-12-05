namespace Hartonomous.Core.Application.Queries.BPETokens;

public sealed record BPEVocabularyResponse
{
    public required List<BPETokenDto> Tokens { get; init; }
    public required int TotalCount { get; init; }
    public required int PageNumber { get; init; }
    public required int PageSize { get; init; }
    public required int TotalPages { get; init; }
}
