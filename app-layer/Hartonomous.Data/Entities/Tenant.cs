using System;
using Hartonomous.Core.Primitives;

namespace Hartonomous.Data.Entities;

public class Tenant
{
    public HartonomousId Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public bool IsActive { get; set; } = true;
    public int MaxStorageGB { get; set; } = 100;
    public int MaxCompositions { get; set; } = 1000000;
    public int MaxRelations { get; set; } = 1000000;
    public string? SubscriptionTier { get; set; }
    public string? BillingEmail { get; set; }
}
