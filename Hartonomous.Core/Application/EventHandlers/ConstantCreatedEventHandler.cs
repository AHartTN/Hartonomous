using Hartonomous.Core.Application.Interfaces;
using Hartonomous.Core.Domain.Events;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Hartonomous.Core.Application.EventHandlers;

/// <summary>
/// Handles ConstantCreated events by triggering automatic spatial projection.
/// </summary>
public sealed class ConstantCreatedEventHandler : INotificationHandler<ConstantCreatedEvent>
{
    private readonly IConstantRepository _constantRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<ConstantCreatedEventHandler> _logger;

    public ConstantCreatedEventHandler(
        IConstantRepository constantRepository,
        IUnitOfWork unitOfWork,
        ILogger<ConstantCreatedEventHandler> logger)
    {
        _constantRepository = constantRepository;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task Handle(ConstantCreatedEvent notification, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Processing ConstantCreated event for Constant {ConstantId} with hash {Hash}",
            notification.ConstantId,
            notification.Hash);

        try
        {
            // Retrieve the constant that was just created
            var constant = await _constantRepository.GetByIdAsync(notification.ConstantId, cancellationToken);
            
            if (constant == null)
            {
                _logger.LogWarning(
                    "Constant {ConstantId} not found during event processing",
                    notification.ConstantId);
                return;
            }

            // Automatically project the constant to 3D space based on its hash
            // The Project() method assigns a deterministic spatial coordinate
            constant.Project();

            await _unitOfWork.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Successfully projected Constant {ConstantId} to spatial coordinate ({X}, {Y}, {Z})",
                constant.Id,
                constant.Coordinate?.X ?? 0,
                constant.Coordinate?.Y ?? 0,
                constant.Coordinate?.Z ?? 0);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to process ConstantCreated event for Constant {ConstantId}",
                notification.ConstantId);
            
            // Don't rethrow - event handlers should be resilient
            // Failed projections can be retried via background worker
        }
    }
}
