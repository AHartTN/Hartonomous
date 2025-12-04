using Hartonomous.Core.Domain.Common;

namespace Hartonomous.Core.Domain.Events;

/// <summary>
/// Domain event raised when a constant is projected to spatial coordinates
/// </summary>
public sealed class ConstantProjectedEvent : IDomainEvent
{
    public Guid EventId { get; } = Guid.NewGuid();
    public DateTime OccurredOn { get; } = DateTime.UtcNow;
    public Guid ConstantId { get; }
    public double X { get; }
    public double Y { get; }
    public double Z { get; }
    
    public ConstantProjectedEvent(Guid constantId, double x, double y, double z)
    {
        ConstantId = constantId;
        X = x;
        Y = y;
        Z = z;
    }
}
