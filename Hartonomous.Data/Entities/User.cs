using System;
using Hartonomous.Core.Primitives;

namespace Hartonomous.Data.Entities;

public class User
{
    public HartonomousId Id { get; set; }
    public HartonomousId TenantId { get; set; }
    public string Username { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string Role { get; set; } = "user";
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastLoginAt { get; set; }

    public virtual Tenant? Tenant { get; set; }
}
