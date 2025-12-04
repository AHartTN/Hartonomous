using Hartonomous.Core.Application.Common;
using Hartonomous.Core.Domain.Enums;

namespace Hartonomous.Core.Application.Commands.ContentIngestion;

/// <summary>
/// Command to ingest content and decompose it into constants
/// </summary>
public sealed record IngestContentCommand : ICommand<Result<IngestContentResponse>>
{
    /// <summary>
    /// Raw content bytes to ingest
    /// </summary>
    public required byte[] ContentData { get; init; }

    /// <summary>
    /// Type of content being ingested
    /// </summary>
    public required ContentType ContentType { get; init; }

    /// <summary>
    /// Optional source information for provenance tracking
    /// </summary>
    public string? SourceUri { get; init; }

    /// <summary>
    /// Optional metadata about the content
    /// </summary>
    public Dictionary<string, string>? Metadata { get; init; }
}

public sealed record IngestContentResponse
{
    public required Guid IngestionId { get; init; }
    public required string ContentHash { get; init; }
    public required int TotalConstantsCreated { get; init; }
    public required int UniqueConstantsCreated { get; init; }
    public required double DeduplicationRatio { get; init; }
    public required long ProcessingTimeMs { get; init; }
}
