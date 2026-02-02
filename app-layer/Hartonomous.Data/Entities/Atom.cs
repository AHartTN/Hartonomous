using System;
using Hartonomous.Core.Primitives;

namespace Hartonomous.Data.Entities;

public class Atom
{
    public HartonomousId Id { get; set; }
    public int Codepoint { get; set; }
    public HartonomousId PhysicalityId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ModifiedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ValidatedAt { get; set; } = DateTimeOffset.UtcNow;

    public virtual Physicality? Physicality { get; set; }
}