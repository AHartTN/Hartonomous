using System;
using Hartonomous.Core.Primitives;

namespace Hartonomous.Data.Entities;

public class Relation
{
    public HartonomousId Id { get; set; }
    public HartonomousId PhysicalityId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public virtual Physicality? Physicality { get; set; }
}
