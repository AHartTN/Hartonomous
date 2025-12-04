using Hartonomous.Core.Application.Common;
using MediatR;

namespace Hartonomous.Core.Application.Commands.ContentIngestion;

/// <summary>
/// Command to process an ingestion job (decompose content into constants).
/// </summary>
public sealed class ProcessIngestionCommand : IRequest<Result<bool>>
{
    public Guid IngestionId { get; set; }
    public int BatchSize { get; set; } = 1000;
}
