using Hartonomous.Core.Abstractions;
using Hartonomous.Core.Enums;

namespace Hartonomous.Core.Entities;

/// <summary>
/// Agent entity implementing IEntity<Guid> for repository pattern
/// </summary>
public class Agent : IEntity<Guid>
{
    public Guid Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string AgentName { get; set; } = string.Empty;
    public string AgentType { get; set; } = string.Empty;
    public string ConnectionId { get; set; } = string.Empty;
    public string[] Capabilities { get; set; } = Array.Empty<string>();
    public string Description { get; set; } = string.Empty;
    public Dictionary<string, object> Configuration { get; set; } = new();
    public AgentStatus Status { get; set; }
    public DateTime CreatedDate { get; set; }
    public DateTime? ModifiedDate { get; set; }
    public DateTime LastHeartbeat { get; set; }
    public Dictionary<string, object> Metrics { get; set; } = new();
}