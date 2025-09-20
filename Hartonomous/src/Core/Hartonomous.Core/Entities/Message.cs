using Hartonomous.Core.Abstractions;
using Hartonomous.Core.Enums;

namespace Hartonomous.Core.Entities;

public class Message : IEntity<Guid>
{
    public Guid Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public Guid ConversationId { get; set; }
    public Guid? AgentId { get; set; }
    public string Content { get; set; } = string.Empty;
    public MessageType MessageType { get; set; }
    public Dictionary<string, object> Metadata { get; set; } = new();
    public DateTime CreatedDate { get; set; }
    public DateTime? ModifiedDate { get; set; }
}