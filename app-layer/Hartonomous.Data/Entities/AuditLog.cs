using System;
using Hartonomous.Core.Primitives;

namespace Hartonomous.Data.Entities;

public class AuditLog
{
    public long Id { get; set; }
    public HartonomousId TenantId { get; set; }
    public HartonomousId? UserId { get; set; }
    public string ActionType { get; set; } = string.Empty;
    public byte[]? ContentHash { get; set; }
    public string? ContentType { get; set; }
    public string ActionDetails { get; set; } = "{}"; // JSONB
    public System.Net.IPAddress? IPAddress { get; set; }
    public string? UserAgent { get; set; }
    public string ActionResult { get; set; } = "success";
    public string? ErrorMessage { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ModifiedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ValidatedAt { get; set; } = DateTimeOffset.UtcNow;

    public virtual Tenant? Tenant { get; set; }
    public virtual User? User { get; set; }
}
