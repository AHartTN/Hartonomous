using Hartonomous.Core.Application.Common;
using Hartonomous.Core.Domain.Enums;
using MediatR;

namespace Hartonomous.Core.Application.Commands.ContentIngestion;

/// <summary>
/// Command to update the status of an ingestion.
/// </summary>
public sealed class UpdateIngestionStatusCommand : IRequest<Result<bool>>
{
    public Guid IngestionId { get; set; }
    public IngestionStatus Status { get; set; }
}
