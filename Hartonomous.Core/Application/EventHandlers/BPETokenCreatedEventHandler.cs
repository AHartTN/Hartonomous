using Hartonomous.Core.Application.Interfaces;
using Hartonomous.Core.Domain.Events;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Hartonomous.Core.Application.EventHandlers;

/// <summary>
/// Handles BPETokenCreated events by updating vocabulary ranks and statistics.
/// </summary>
public sealed class BPETokenCreatedEventHandler : INotificationHandler<BPETokenCreatedEvent>
{
    private readonly IBPETokenRepository _bpeTokenRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<BPETokenCreatedEventHandler> _logger;

    public BPETokenCreatedEventHandler(
        IBPETokenRepository bpeTokenRepository,
        IUnitOfWork unitOfWork,
        ILogger<BPETokenCreatedEventHandler> logger)
    {
        _bpeTokenRepository = bpeTokenRepository;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task Handle(BPETokenCreatedEvent notification, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Processing BPETokenCreated event for TokenId {TokenId} at MergeLevel {MergeLevel}, Frequency {Frequency}",
            notification.TokenId,
            notification.MergeLevel,
            notification.Frequency);

        try
        {
            // Update vocabulary ranks for all tokens
            // This ensures tokens are properly ranked by frequency for compression efficiency
            await UpdateVocabularyRanksAsync(cancellationToken);

            _logger.LogInformation(
                "Successfully updated vocabulary ranks after creating TokenId {TokenId}",
                notification.TokenId);

            // Log vocabulary statistics
            var vocabularySize = await _bpeTokenRepository.GetVocabularySizeAsync(cancellationToken);
            var topTokens = await _bpeTokenRepository.GetTopByFrequencyAsync(10, cancellationToken);

            _logger.LogInformation(
                "Current vocabulary: {VocabularySize} tokens. Top token frequency: {TopFrequency}",
                vocabularySize,
                topTokens.FirstOrDefault()?.Frequency ?? 0);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to process BPETokenCreated event for TokenId {TokenId}",
                notification.TokenId);
            
            // Don't rethrow - event handlers should be resilient
            // Vocabulary ranks can be recalculated later via background job
        }
    }

    private async Task UpdateVocabularyRanksAsync(CancellationToken cancellationToken)
    {
        // Get all tokens ordered by frequency (descending)
        var allTokens = await _bpeTokenRepository.GetTopByFrequencyAsync(
            int.MaxValue, // Get all tokens
            cancellationToken);

        // Assign ranks based on frequency order
        var rank = 1;
        foreach (var token in allTokens)
        {
            token.UpdateVocabularyRank(rank);
            rank++;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        _logger.LogDebug(
            "Updated vocabulary ranks for {TokenCount} tokens",
            allTokens.Count());
    }
}
