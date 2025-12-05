using Hartonomous.Core.Domain.Enums;

namespace Hartonomous.Core.Application.Queries.ContentIngestion;

public sealed class IngestionDto
{
    public Guid Id { get; set; }
    public IngestionStatus Status { get; set; }
    public string? ContentHash { get; set; }
    public long ContentSize { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}
