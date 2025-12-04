using Hartonomous.Core.Application.Common;

namespace Hartonomous.Core.Application.Queries.ContentIngestion;

/// <summary>
/// Query to get ingestion metrics
/// </summary>
public sealed record GetIngestionMetricsQuery : IQuery<Result<IngestionMetricsDto>>
{
}

/// <summary>
/// Ingestion metrics DTO
/// </summary>
public sealed record IngestionMetricsDto
{
    public required double ThroughputBytesPerSecond { get; init; }
    public required double AverageDeduplicationRatio { get; init; }
    public required long TotalBytesProcessed { get; init; }
    public required long TotalBytesSaved { get; init; }
    public required double CompressionRatio { get; init; }
}
