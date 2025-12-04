using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Hartonomous.Infrastructure.Messaging;

/// <summary>
/// In-memory implementation of message queue service.
/// Used for development and testing. Not persistent or distributed.
/// </summary>
public class InMemoryQueueService : IMessageQueueService
{
    private readonly ConcurrentDictionary<string, ConcurrentQueue<InMemoryMessage>> _queues = new();
    private readonly ConcurrentDictionary<string, InMemoryMessage> _processingMessages = new();
    private readonly ILogger<InMemoryQueueService> _logger;

    public InMemoryQueueService(ILogger<InMemoryQueueService> logger)
    {
        _logger = logger;
    }

    public Task PublishAsync<T>(
        string queueName,
        T message,
        CancellationToken cancellationToken = default)
    {
        var queue = _queues.GetOrAdd(queueName, _ => new ConcurrentQueue<InMemoryMessage>());
        
        var inMemoryMessage = new InMemoryMessage
        {
            MessageId = Guid.NewGuid().ToString(),
            Content = message!,
            InsertedOn = DateTimeOffset.UtcNow,
            ExpiresOn = DateTimeOffset.UtcNow.AddDays(7),
            DequeueCount = 0
        };

        queue.Enqueue(inMemoryMessage);
        _logger.LogInformation("Published message to in-memory queue {QueueName}", queueName);

        return Task.CompletedTask;
    }

    public Task PublishWithDelayAsync<T>(
        string queueName,
        T message,
        TimeSpan delay,
        CancellationToken cancellationToken = default)
    {
        var queue = _queues.GetOrAdd(queueName, _ => new ConcurrentQueue<InMemoryMessage>());
        
        var inMemoryMessage = new InMemoryMessage
        {
            MessageId = Guid.NewGuid().ToString(),
            Content = message!,
            InsertedOn = DateTimeOffset.UtcNow,
            ExpiresOn = DateTimeOffset.UtcNow.AddDays(7),
            NextVisibleOn = DateTimeOffset.UtcNow.Add(delay),
            DequeueCount = 0
        };

        queue.Enqueue(inMemoryMessage);
        _logger.LogInformation("Published delayed message to in-memory queue {QueueName} with delay {Delay}", 
            queueName, delay);

        return Task.CompletedTask;
    }

    public Task<QueueMessage<T>?> ReceiveAsync<T>(
        string queueName,
        TimeSpan? visibilityTimeout = null,
        CancellationToken cancellationToken = default)
    {
        if (!_queues.TryGetValue(queueName, out var queue))
        {
            return Task.FromResult<QueueMessage<T>?>(null);
        }

        while (queue.TryDequeue(out var inMemoryMessage))
        {
            // Check if message is visible
            if (inMemoryMessage.NextVisibleOn.HasValue && 
                inMemoryMessage.NextVisibleOn.Value > DateTimeOffset.UtcNow)
            {
                // Re-enqueue and continue
                queue.Enqueue(inMemoryMessage);
                continue;
            }

            // Check if expired
            if (inMemoryMessage.ExpiresOn < DateTimeOffset.UtcNow)
            {
                continue; // Skip expired message
            }

            inMemoryMessage.DequeueCount++;
            inMemoryMessage.PopReceipt = Guid.NewGuid().ToString();
            inMemoryMessage.NextVisibleOn = DateTimeOffset.UtcNow.Add(visibilityTimeout ?? TimeSpan.FromMinutes(5));

            _processingMessages[inMemoryMessage.MessageId] = inMemoryMessage;

            return Task.FromResult<QueueMessage<T>?>(new QueueMessage<T>
            {
                MessageId = inMemoryMessage.MessageId,
                PopReceipt = inMemoryMessage.PopReceipt,
                Content = (T)inMemoryMessage.Content,
                DequeueCount = inMemoryMessage.DequeueCount,
                InsertedOn = inMemoryMessage.InsertedOn,
                ExpiresOn = inMemoryMessage.ExpiresOn,
                NextVisibleOn = inMemoryMessage.NextVisibleOn
            });
        }

        return Task.FromResult<QueueMessage<T>?>(null);
    }

    public async Task<IEnumerable<QueueMessage<T>>> ReceiveBatchAsync<T>(
        string queueName,
        int maxMessages = 10,
        TimeSpan? visibilityTimeout = null,
        CancellationToken cancellationToken = default)
    {
        var messages = new List<QueueMessage<T>>();
        
        for (int i = 0; i < maxMessages; i++)
        {
            var message = await ReceiveAsync<T>(queueName, visibilityTimeout, cancellationToken);
            if (message == null)
            {
                break;
            }
            messages.Add(message);
        }

        return messages;
    }

    public Task CompleteAsync(
        string queueName,
        string messageId,
        string popReceipt,
        CancellationToken cancellationToken = default)
    {
        // Validate pop receipt
        if (_processingMessages.TryGetValue(messageId, out var message) &&
            message.PopReceipt == popReceipt)
        {
            _processingMessages.TryRemove(messageId, out _);
            _logger.LogInformation("Completed message {MessageId} from in-memory queue {QueueName}", 
                messageId, queueName);
        }
        else
        {
            _logger.LogWarning("Failed to complete message {MessageId} - invalid pop receipt or message not found", 
                messageId);
        }

        return Task.CompletedTask;
    }

    public Task AbandonAsync(
        string queueName,
        string messageId,
        string popReceipt,
        CancellationToken cancellationToken = default)
    {
        if (_processingMessages.TryRemove(messageId, out var message) &&
            message.PopReceipt == popReceipt)
        {
            var queue = _queues.GetOrAdd(queueName, _ => new ConcurrentQueue<InMemoryMessage>());
            
            // Make visible immediately
            message.NextVisibleOn = DateTimeOffset.UtcNow;
            message.PopReceipt = null;
            
            queue.Enqueue(message);
            _logger.LogInformation("Abandoned message {MessageId} back to in-memory queue {QueueName}", 
                messageId, queueName);
        }

        return Task.CompletedTask;
    }

    public Task<int> GetQueueLengthAsync(
        string queueName,
        CancellationToken cancellationToken = default)
    {
        if (_queues.TryGetValue(queueName, out var queue))
        {
            return Task.FromResult(queue.Count);
        }

        return Task.FromResult(0);
    }

    private class InMemoryMessage
    {
        public required string MessageId { get; set; }
        public string? PopReceipt { get; set; }
        public required object Content { get; set; }
        public int DequeueCount { get; set; }
        public DateTimeOffset InsertedOn { get; set; }
        public DateTimeOffset ExpiresOn { get; set; }
        public DateTimeOffset? NextVisibleOn { get; set; }
    }
}
