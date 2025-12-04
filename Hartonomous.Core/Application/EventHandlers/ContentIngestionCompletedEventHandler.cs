using Hartonomous.Core.Application.Interfaces;
using Hartonomous.Core.Domain.Events;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Hartonomous.Core.Application.EventHandlers;

/// <summary>
/// Handles ContentIngestionCompleted events by triggering analytics, logging, and notifications.
/// </summary>
public sealed class ContentIngestionCompletedEventHandler : INotificationHandler<ContentIngestionCompletedEvent>
{
    private readonly IContentIngestionRepository _ingestionRepository;
    private readonly IConstantRepository _constantRepository;
    private readonly ILogger<ContentIngestionCompletedEventHandler> _logger;

    public ContentIngestionCompletedEventHandler(
        IContentIngestionRepository ingestionRepository,
        IConstantRepository constantRepository,
        ILogger<ContentIngestionCompletedEventHandler> logger)
    {
        _ingestionRepository = ingestionRepository;
        _constantRepository = constantRepository;
        _logger = logger;
    }

    public async Task Handle(ContentIngestionCompletedEvent notification, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Processing ContentIngestionCompleted event for Ingestion {IngestionId}: " +
            "{ConstantCount} total constants, {UniqueConstantCount} unique constants, " +
            "{DeduplicationRatio:P2} deduplication ratio, {ProcessingTimeMs}ms processing time",
            notification.IngestionId,
            notification.ConstantCount,
            notification.UniqueConstantCount,
            notification.DeduplicationRatio,
            notification.ProcessingTimeMs);

        try
        {
            // Retrieve the completed ingestion for analysis
            var ingestion = await _ingestionRepository.GetByIdAsync(notification.IngestionId, cancellationToken);
            
            if (ingestion == null)
            {
                _logger.LogWarning(
                    "ContentIngestion {IngestionId} not found during event processing",
                    notification.IngestionId);
                return;
            }

            // Retrieve deduplication metrics
            var deduplicationRatio = notification.DeduplicationRatio;

            var compressionRatio = notification.UniqueConstantCount > 0
                ? (double)notification.ConstantCount / notification.UniqueConstantCount
                : 1.0;

            // Log comprehensive analytics
            _logger.LogInformation(
                "Content ingestion analytics for {IngestionId}:\n" +
                "  Content Hash: {ContentHash}\n" +
                "  Content Type: {ContentType}\n" +
                "  Total Constants: {ConstantCount}\n" +
                "  Unique Constants: {UniqueConstantCount}\n" +
                "  Duplicate Constants: {DuplicateCount}\n" +
                "  Deduplication Ratio: {DeduplicationRatio:P2}\n" +
                "  Compression Ratio: {CompressionRatio:F2}x\n" +
                "  Processing Time: {ProcessingTimeMs}ms\n" +
                "  Throughput: {ThroughputKBps:F2} KB/s",
                ingestion.Id,
                ingestion.ContentHash,
                ingestion.ContentType,
                notification.ConstantCount,
                notification.UniqueConstantCount,
                notification.ConstantCount - notification.UniqueConstantCount,
                deduplicationRatio,
                compressionRatio,
                notification.ProcessingTimeMs,
                notification.ProcessingTimeMs > 0
                    ? (notification.ConstantCount / 1024.0) / (notification.ProcessingTimeMs / 1000.0)
                    : 0.0);

            // Update global statistics
            await UpdateGlobalStatisticsAsync(notification, cancellationToken);

            // TODO: Trigger additional analytics jobs
            // - Spatial density analysis
            // - Content similarity clustering
            // - BPE learning opportunity detection
            // - Anomaly detection for unusual content patterns

            // TODO: Send notifications
            // - Real-time dashboard updates
            // - Alert on high deduplication (potential data redundancy)
            // - Alert on low deduplication (novel content patterns)
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to process ContentIngestionCompleted event for Ingestion {IngestionId}",
                notification.IngestionId);
            
            // Don't rethrow - event handlers should be resilient
            // Analytics failures should not impact the core ingestion process
        }
    }

    private async Task UpdateGlobalStatisticsAsync(
        ContentIngestionCompletedEvent notification,
        CancellationToken cancellationToken)
    {
        // Get current system-wide statistics
        var totalIngestionsTask = _ingestionRepository.GetTotalIngestionsAsync(cancellationToken);
        var totalConstantsTask = _constantRepository.GetTotalConstantsAsync(cancellationToken);
        var activeConstantsTask = _constantRepository.GetActiveConstantsCountAsync(cancellationToken);

        await Task.WhenAll(totalIngestionsTask, totalConstantsTask, activeConstantsTask);

        var totalIngestions = await totalIngestionsTask;
        var totalConstants = await totalConstantsTask;
        var activeConstants = await activeConstantsTask;

        _logger.LogInformation(
            "Global system statistics:\n" +
            "  Total Ingestions: {TotalIngestions:N0}\n" +
            "  Total Constants: {TotalConstants:N0}\n" +
            "  Active Constants: {ActiveConstants:N0}\n" +
            "  Activation Rate: {ActivationRate:P2}",
            totalIngestions,
            totalConstants,
            activeConstants,
            totalConstants > 0 ? (double)activeConstants / totalConstants : 0.0);
    }
}
