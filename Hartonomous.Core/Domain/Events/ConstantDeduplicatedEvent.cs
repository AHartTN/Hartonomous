using Hartonomous.Core.Domain.Common;

namespace Hartonomous.Core.Domain.Events;

/// <summary>
/// Domain event raised when a constant is marked as duplicate
/// </summary>
public sealed class ConstantDeduplicatedEvent : IDomainEvent
{
    public Guid EventId { get; } = Guid.NewGuid();
    public DateTime OccurredOn { get; } = DateTime.UtcNow;
    public Guid DuplicateConstantId { get; }
    public Guid CanonicalConstantId { get; }
    
    public ConstantDeduplicatedEvent(Guid duplicateConstantId, Guid canonicalConstantId)
    {
        DuplicateConstantId = duplicateConstantId;
        CanonicalConstantId = canonicalConstantId;
    }
}
