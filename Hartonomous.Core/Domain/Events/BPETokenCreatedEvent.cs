using Hartonomous.Core.Domain.Common;

namespace Hartonomous.Core.Domain.Events;

/// <summary>
/// Domain event raised when a BPE token is created through merging
/// </summary>
public sealed class BPETokenCreatedEvent : IDomainEvent
{
    public Guid EventId { get; } = Guid.NewGuid();
    public DateTime OccurredOn { get; } = DateTime.UtcNow;
    public Guid TokenEntityId { get; }
    public int TokenId { get; }
    public int MergeLevel { get; }
    public long Frequency { get; }
    public int SequenceLength { get; }
    
    public BPETokenCreatedEvent(
        Guid tokenEntityId, 
        int tokenId, 
        int mergeLevel, 
        long frequency, 
        int sequenceLength)
    {
        TokenEntityId = tokenEntityId;
        TokenId = tokenId;
        MergeLevel = mergeLevel;
        Frequency = frequency;
        SequenceLength = sequenceLength;
    }
}
