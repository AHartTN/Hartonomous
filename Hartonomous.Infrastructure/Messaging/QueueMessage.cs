namespace Hartonomous.Infrastructure.Messaging;

/// <summary>
/// Message retrieved from a queue.
/// </summary>
public class QueueMessage<T>
{
    public required string MessageId { get; init; }
    public required string PopReceipt { get; init; }
    public required T Content { get; init; }
    public int DequeueCount { get; init; }
    public DateTimeOffset? InsertedOn { get; init; }
    public DateTimeOffset? ExpiresOn { get; init; }
    public DateTimeOffset? NextVisibleOn { get; init; }
}
