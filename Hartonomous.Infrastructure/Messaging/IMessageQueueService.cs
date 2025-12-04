namespace Hartonomous.Infrastructure.Messaging;

/// <summary>
/// Service for publishing and consuming messages from a queue.
/// Supports Azure Storage Queue or in-memory queue.
/// </summary>
public interface IMessageQueueService
{
    /// <summary>
    /// Publish a message to a queue.
    /// </summary>
    /// <param name="queueName">Queue name</param>
    /// <param name="message">Message content</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task PublishAsync<T>(
        string queueName,
        T message,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Publish a message with a delay (visibility timeout).
    /// </summary>
    Task PublishWithDelayAsync<T>(
        string queueName,
        T message,
        TimeSpan delay,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Receive a message from a queue.
    /// </summary>
    /// <param name="queueName">Queue name</param>
    /// <param name="visibilityTimeout">How long the message is hidden from other consumers</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Message and receipt handle, or null if no messages available</returns>
    Task<QueueMessage<T>?> ReceiveAsync<T>(
        string queueName,
        TimeSpan? visibilityTimeout = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Receive multiple messages from a queue.
    /// </summary>
    Task<IEnumerable<QueueMessage<T>>> ReceiveBatchAsync<T>(
        string queueName,
        int maxMessages = 10,
        TimeSpan? visibilityTimeout = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Delete a message from the queue (mark as processed).
    /// </summary>
    Task CompleteAsync(
        string queueName,
        string messageId,
        string popReceipt,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Return a message to the queue (make it visible again).
    /// </summary>
    Task AbandonAsync(
        string queueName,
        string messageId,
        string popReceipt,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get approximate queue length.
    /// </summary>
    Task<int> GetQueueLengthAsync(
        string queueName,
        CancellationToken cancellationToken = default);
}

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
