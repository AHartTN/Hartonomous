using Hartonomous.Core.Application.Interfaces;
using Hartonomous.Core.Domain.Events;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Hartonomous.Core.Application.EventHandlers;

/// <summary>
/// Handles ConstantDeduplicated events by updating canonical references and statistics.
/// </summary>
public sealed class ConstantDeduplicatedEventHandler : INotificationHandler<ConstantDeduplicatedEvent>
{
    private readonly IConstantRepository _constantRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<ConstantDeduplicatedEventHandler> _logger;

    public ConstantDeduplicatedEventHandler(
        IConstantRepository constantRepository,
        IUnitOfWork unitOfWork,
        ILogger<ConstantDeduplicatedEventHandler> logger)
    {
        _constantRepository = constantRepository;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task Handle(ConstantDeduplicatedEvent notification, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Processing ConstantDeduplicated event: Duplicate {DuplicateId} references canonical {CanonicalId}",
            notification.DuplicateConstantId,
            notification.CanonicalConstantId);

        try
        {
            // Retrieve the canonical constant
            var canonicalConstant = await _constantRepository.GetByIdAsync(
                notification.CanonicalConstantId,
                cancellationToken);
            
            if (canonicalConstant == null)
            {
                _logger.LogWarning(
                    "Canonical Constant {CanonicalId} not found during deduplication event processing",
                    notification.CanonicalConstantId);
                return;
            }

            // Increment reference count on the canonical constant
            // This tracks how many times this constant has been referenced/deduplicated
            canonicalConstant.IncrementReferenceCount();

            // Update frequency for BPE learning
            // Frequency represents how common this constant is across all content
            canonicalConstant.IncrementFrequency();

            await _unitOfWork.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Successfully updated canonical Constant {CanonicalId}: ReferenceCount={ReferenceCount}, Frequency={Frequency}",
                canonicalConstant.Id,
                canonicalConstant.ReferenceCount,
                canonicalConstant.Frequency);

            // Note: The duplicate constant itself is not deleted from the database
            // It remains as a record of the deduplication event with a reference to the canonical constant
            // This allows for audit trails and potential future analysis
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to process ConstantDeduplicated event for duplicate {DuplicateId} -> canonical {CanonicalId}",
                notification.DuplicateConstantId,
                notification.CanonicalConstantId);
            
            // Don't rethrow - event handlers should be resilient
            // Statistics can be recalculated later if needed
        }
    }
}
