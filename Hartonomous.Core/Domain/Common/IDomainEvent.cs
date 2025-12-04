using MediatR;

namespace Hartonomous.Core.Domain.Common;

/// <summary>
/// Base interface for all domain events
/// </summary>
public interface IDomainEvent : INotification
{
    DateTime OccurredOn { get; }
    Guid EventId { get; }
}
