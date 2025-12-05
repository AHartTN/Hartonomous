using Hartonomous.Core.Application.Common;

namespace Hartonomous.Core.Application.Queries.ContentIngestion;

/// <summary>
/// Query to get content ingestion by ID
/// </summary>
public sealed record GetContentIngestionByIdQuery : IQuery<Result<ContentIngestionDto>>
{
    public required Guid IngestionId { get; init; }
}
