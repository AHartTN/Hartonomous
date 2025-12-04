using System.Text.Json;
using Azure.Storage.Queues;
using Azure.Storage.Queues.Models;
using Microsoft.Extensions.Logging;

namespace Hartonomous.Infrastructure.Messaging;

/// <summary>
/// Azure Storage Queue implementation of message queue service.
/// </summary>
public class AzureQueueService : IMessageQueueService
{
    private readonly QueueServiceClient _queueServiceClient;
    private readonly ILogger<AzureQueueService> _logger;
    private readonly JsonSerializerOptions _jsonOptions;

    public AzureQueueService(
        QueueServiceClient queueServiceClient,
        ILogger<AzureQueueService> logger)
    {
        _queueServiceClient = queueServiceClient;
        _logger = logger;
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };
    }

    public async Task PublishAsync<T>(
        string queueName,
        T message,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var queueClient = _queueServiceClient.GetQueueClient(queueName);
            await queueClient.CreateIfNotExistsAsync(cancellationToken: cancellationToken);

            var messageJson = JsonSerializer.Serialize(message, _jsonOptions);
            await queueClient.SendMessageAsync(messageJson, cancellationToken: cancellationToken);

            _logger.LogInformation("Published message to queue {QueueName}", queueName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish message to queue {QueueName}", queueName);
            throw;
        }
    }

    public async Task PublishWithDelayAsync<T>(
        string queueName,
        T message,
        TimeSpan delay,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var queueClient = _queueServiceClient.GetQueueClient(queueName);
            await queueClient.CreateIfNotExistsAsync(cancellationToken: cancellationToken);

            var messageJson = JsonSerializer.Serialize(message, _jsonOptions);
            await queueClient.SendMessageAsync(
                messageJson,
                visibilityTimeout: delay,
                cancellationToken: cancellationToken);

            _logger.LogInformation("Published delayed message to queue {QueueName} with delay {Delay}", queueName, delay);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish delayed message to queue {QueueName}", queueName);
            throw;
        }
    }

    public async Task<QueueMessage<T>?> ReceiveAsync<T>(
        string queueName,
        TimeSpan? visibilityTimeout = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var queueClient = _queueServiceClient.GetQueueClient(queueName);
            await queueClient.CreateIfNotExistsAsync(cancellationToken: cancellationToken);

            var response = await queueClient.ReceiveMessageAsync(
                visibilityTimeout: visibilityTimeout,
                cancellationToken: cancellationToken);

            if (response.Value == null)
            {
                return null;
            }

            var azureMessage = response.Value;
            var content = JsonSerializer.Deserialize<T>(azureMessage.MessageText, _jsonOptions);

            if (content == null)
            {
                _logger.LogWarning("Failed to deserialize message {MessageId} from queue {QueueName}", 
                    azureMessage.MessageId, queueName);
                return null;
            }

            return new QueueMessage<T>
            {
                MessageId = azureMessage.MessageId,
                PopReceipt = azureMessage.PopReceipt,
                Content = content,
                DequeueCount = (int)azureMessage.DequeueCount,
                InsertedOn = azureMessage.InsertedOn,
                ExpiresOn = azureMessage.ExpiresOn,
                NextVisibleOn = azureMessage.NextVisibleOn
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to receive message from queue {QueueName}", queueName);
            throw;
        }
    }

    public async Task<IEnumerable<QueueMessage<T>>> ReceiveBatchAsync<T>(
        string queueName,
        int maxMessages = 10,
        TimeSpan? visibilityTimeout = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var queueClient = _queueServiceClient.GetQueueClient(queueName);
            await queueClient.CreateIfNotExistsAsync(cancellationToken: cancellationToken);

            var response = await queueClient.ReceiveMessagesAsync(
                maxMessages: Math.Min(maxMessages, 32), // Azure max is 32
                visibilityTimeout: visibilityTimeout,
                cancellationToken: cancellationToken);

            var messages = new List<QueueMessage<T>>();
            foreach (var azureMessage in response.Value)
            {
                try
                {
                    var content = JsonSerializer.Deserialize<T>(azureMessage.MessageText, _jsonOptions);
                    if (content != null)
                    {
                        messages.Add(new QueueMessage<T>
                        {
                            MessageId = azureMessage.MessageId,
                            PopReceipt = azureMessage.PopReceipt,
                            Content = content,
                            DequeueCount = (int)azureMessage.DequeueCount,
                            InsertedOn = azureMessage.InsertedOn,
                            ExpiresOn = azureMessage.ExpiresOn,
                            NextVisibleOn = azureMessage.NextVisibleOn
                        });
                    }
                }
                catch (JsonException ex)
                {
                    _logger.LogWarning(ex, "Failed to deserialize message {MessageId} from queue {QueueName}",
                        azureMessage.MessageId, queueName);
                }
            }

            return messages;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to receive batch from queue {QueueName}", queueName);
            throw;
        }
    }

    public async Task CompleteAsync(
        string queueName,
        string messageId,
        string popReceipt,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var queueClient = _queueServiceClient.GetQueueClient(queueName);
            await queueClient.DeleteMessageAsync(messageId, popReceipt, cancellationToken);

            _logger.LogInformation("Completed message {MessageId} from queue {QueueName}", messageId, queueName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to complete message {MessageId} from queue {QueueName}", messageId, queueName);
            throw;
        }
    }

    public async Task AbandonAsync(
        string queueName,
        string messageId,
        string popReceipt,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var queueClient = _queueServiceClient.GetQueueClient(queueName);
            
            // Make message visible immediately
            await queueClient.UpdateMessageAsync(
                messageId,
                popReceipt,
                visibilityTimeout: TimeSpan.Zero,
                cancellationToken: cancellationToken);

            _logger.LogInformation("Abandoned message {MessageId} from queue {QueueName}", messageId, queueName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to abandon message {MessageId} from queue {QueueName}", messageId, queueName);
            throw;
        }
    }

    public async Task<int> GetQueueLengthAsync(
        string queueName,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var queueClient = _queueServiceClient.GetQueueClient(queueName);
            var properties = await queueClient.GetPropertiesAsync(cancellationToken);
            return (int)properties.Value.ApproximateMessagesCount;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get queue length for {QueueName}", queueName);
            return 0;
        }
    }
}
