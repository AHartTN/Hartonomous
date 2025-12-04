using Hartonomous.Core.Application.Common;
using Hartonomous.Core.Application.Interfaces;
using MediatR;

namespace Hartonomous.Core.Application.Queries.BPETokens;

public sealed class GetBPEVocabularyQueryHandler : IRequestHandler<GetBPEVocabularyQuery, Result<BPEVocabularyResponse>>
{
    private readonly IBPETokenRepository _repository;

    public GetBPEVocabularyQueryHandler(IBPETokenRepository repository)
    {
        _repository = repository;
    }

    public async Task<Result<BPEVocabularyResponse>> Handle(GetBPEVocabularyQuery request, CancellationToken cancellationToken)
    {
        try
        {
            // Get total count - use simple approach for now
            var allTokens = await _repository.GetTopByFrequencyAsync(int.MaxValue, cancellationToken);
            
            // Apply filters
            var filteredTokens = allTokens.AsEnumerable();
            if (request.MinFrequency.HasValue)
            {
                filteredTokens = filteredTokens.Where(t => t.Frequency >= request.MinFrequency.Value);
            }
            if (request.MergeLevel.HasValue)
            {
                filteredTokens = filteredTokens.Where(t => t.MergeLevel == request.MergeLevel.Value);
            }

            var totalCount = filteredTokens.Count();
            
            if (totalCount == 0)
            {
                return Result<BPEVocabularyResponse>.Success(new BPEVocabularyResponse
                {
                    Tokens = new List<BPETokenDto>(),
                    TotalCount = 0,
                    PageNumber = request.PageNumber,
                    PageSize = request.PageSize,
                    TotalPages = 0
                });
            }

            // Get paginated tokens
            var skip = (request.PageNumber - 1) * request.PageSize;
            var tokens = filteredTokens.Skip(skip).Take(request.PageSize).ToList();

            var dtos = tokens.Select(t => new BPETokenDto
            {
                Id = t.Id,
                TokenId = Guid.NewGuid(), // Note: TokenId is int in entity but Guid in DTO
                Hash = t.Hash.ToString(),
                ConstantSequence = t.ConstantSequence,
                SequenceLength = t.SequenceLength,
                MergeLevel = t.MergeLevel,
                Frequency = t.Frequency,
                VocabularyRank = t.VocabularyRank > 0 ? (int?)t.VocabularyRank : null,
                CreatedAt = t.CreatedAt
            }).ToList();

            var totalPages = (int)Math.Ceiling(totalCount / (double)request.PageSize);

            return Result<BPEVocabularyResponse>.Success(new BPEVocabularyResponse
            {
                Tokens = dtos,
                TotalCount = totalCount,
                PageNumber = request.PageNumber,
                PageSize = request.PageSize,
                TotalPages = totalPages
            });
        }
        catch (Exception ex)
        {
            return Result<BPEVocabularyResponse>.Failure($"Failed to retrieve BPE vocabulary: {ex.Message}");
        }
    }
}
