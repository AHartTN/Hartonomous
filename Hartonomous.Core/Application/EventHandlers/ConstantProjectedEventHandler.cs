using Hartonomous.Core.Application.Interfaces;
using Hartonomous.Core.Domain.Events;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Hartonomous.Core.Application.EventHandlers;

/// <summary>
/// Handles ConstantProjected events by triggering Hilbert curve indexing.
/// </summary>
public sealed class ConstantProjectedEventHandler : INotificationHandler<ConstantProjectedEvent>
{
    private readonly IConstantRepository _constantRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<ConstantProjectedEventHandler> _logger;

    public ConstantProjectedEventHandler(
        IConstantRepository constantRepository,
        IUnitOfWork unitOfWork,
        ILogger<ConstantProjectedEventHandler> logger)
    {
        _constantRepository = constantRepository;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task Handle(ConstantProjectedEvent notification, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Processing ConstantProjected event for Constant {ConstantId} at coordinate ({X}, {Y}, {Z})",
            notification.ConstantId,
            notification.X,
            notification.Y,
            notification.Z);

        try
        {
            // Retrieve the constant that was just projected
            var constant = await _constantRepository.GetByIdAsync(notification.ConstantId, cancellationToken);
            
            if (constant == null)
            {
                _logger.LogWarning(
                    "Constant {ConstantId} not found during event processing",
                    notification.ConstantId);
                return;
            }

            // Hilbert curve index is automatically computed when SpatialCoordinate.Create() is called.
            // No manual indexing step needed - the coordinate already contains its Hilbert index.
            // Log the indexed state for diagnostics.

            await _unitOfWork.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Constant {ConstantId} projected with Hilbert4D index ({HilbertHigh}, {HilbertLow})",
                constant.Id,
                constant.Coordinate!.HilbertHigh,
                constant.Coordinate!.HilbertLow);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to process ConstantProjected event for Constant {ConstantId}",
                notification.ConstantId);
            
            // Don't rethrow - event handlers should be resilient
            // Failed indexing can be retried via background worker
        }
    }
}
