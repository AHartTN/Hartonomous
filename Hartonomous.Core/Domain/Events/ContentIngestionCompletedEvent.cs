using Hartonomous.Core.Domain.Common;

namespace Hartonomous.Core.Domain.Events;

/// <summary>
/// Domain event raised when content ingestion completes
/// </summary>
public sealed class ContentIngestionCompletedEvent : IDomainEvent
{
    public Guid EventId { get; } = Guid.NewGuid();
    public DateTime OccurredOn { get; } = DateTime.UtcNow;
    public Guid IngestionId { get; }
    public int ConstantCount { get; }
    public int UniqueConstantCount { get; }
    public double DeduplicationRatio { get; }
    public long ProcessingTimeMs { get; }
    
    public ContentIngestionCompletedEvent(
        Guid ingestionId,
        int constantCount,
        int uniqueConstantCount,
        double deduplicationRatio,
        long processingTimeMs)
    {
        IngestionId = ingestionId;
        ConstantCount = constantCount;
        UniqueConstantCount = uniqueConstantCount;
        DeduplicationRatio = deduplicationRatio;
        ProcessingTimeMs = processingTimeMs;
    }
}
