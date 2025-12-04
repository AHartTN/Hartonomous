using Hartonomous.Core.Domain.Common;

namespace Hartonomous.Core.Domain.Events;

/// <summary>
/// Domain event raised when a new constant is created
/// </summary>
public sealed class ConstantCreatedEvent : IDomainEvent
{
    public Guid EventId { get; } = Guid.NewGuid();
    public DateTime OccurredOn { get; } = DateTime.UtcNow;
    public Guid ConstantId { get; }
    public string Hash { get; }
    public int Size { get; }
    public string ContentType { get; }
    
    public ConstantCreatedEvent(Guid constantId, string hash, int size, string contentType)
    {
        ConstantId = constantId;
        Hash = hash;
        Size = size;
        ContentType = contentType;
    }
}
