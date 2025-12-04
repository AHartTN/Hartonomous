using Hartonomous.Core.Application.Interfaces;
using Hartonomous.Core.Domain.Events;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Hartonomous.Core.Application.EventHandlers;

/// <summary>
/// Handles ConstantIndexed events by triggering activation.
/// </summary>
public sealed class ConstantIndexedEventHandler : INotificationHandler<ConstantIndexedEvent>
{
    private readonly IConstantRepository _constantRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<ConstantIndexedEventHandler> _logger;

    public ConstantIndexedEventHandler(
        IConstantRepository constantRepository,
        IUnitOfWork unitOfWork,
        ILogger<ConstantIndexedEventHandler> logger)
    {
        _constantRepository = constantRepository;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task Handle(ConstantIndexedEvent notification, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Processing ConstantIndexed event for Constant {ConstantId} with Hilbert index {HilbertIndex}",
            notification.ConstantId,
            notification.HilbertIndex);

        try
        {
            // Retrieve the constant that was just indexed
            var constant = await _constantRepository.GetByIdAsync(notification.ConstantId, cancellationToken);
            
            if (constant == null)
            {
                _logger.LogWarning(
                    "Constant {ConstantId} not found during event processing",
                    notification.ConstantId);
                return;
            }

            // Automatically activate the constant for use in the system
            // The Activate() method marks the constant as ready for queries and BPE learning
            constant.Activate();

            await _unitOfWork.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Successfully activated Constant {ConstantId} at {ActivatedAt}",
                constant.Id,
                constant.ActivatedAt);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to process ConstantIndexed event for Constant {ConstantId}",
                notification.ConstantId);
            
            // Don't rethrow - event handlers should be resilient
            // Failed activation can be retried via background worker
        }
    }
}
