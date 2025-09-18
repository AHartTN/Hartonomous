using Hartonomous.Core.Abstractions;
using Hartonomous.Core.DTOs;

namespace Hartonomous.Core.Entities;

public class Workflow : IEntity<Guid>
{
    public Guid Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Definition { get; set; } = string.Empty;
    public WorkflowStatus Status { get; set; }
    public Dictionary<string, object> Parameters { get; set; } = new();
    public DateTime CreatedDate { get; set; }
    public DateTime? ModifiedDate { get; set; }
}