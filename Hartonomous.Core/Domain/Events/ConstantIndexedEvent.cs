using Hartonomous.Core.Domain.Common;

namespace Hartonomous.Core.Domain.Events;

/// <summary>
/// Domain event raised when a constant is indexed with Hilbert ID
/// </summary>
public sealed class ConstantIndexedEvent : IDomainEvent
{
    public Guid EventId { get; } = Guid.NewGuid();
    public DateTime OccurredOn { get; } = DateTime.UtcNow;
    public Guid ConstantId { get; }
    public ulong HilbertIndex { get; }
    
    public ConstantIndexedEvent(Guid constantId, ulong hilbertIndex)
    {
        ConstantId = constantId;
        HilbertIndex = hilbertIndex;
    }
}
