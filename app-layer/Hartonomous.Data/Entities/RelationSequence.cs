using System;
using Hartonomous.Core.Primitives;

namespace Hartonomous.Data.Entities;

public class RelationSequence
{
    public HartonomousId Id { get; set; }
    public HartonomousId RelationId { get; set; }
    public HartonomousId CompositionId { get; set; }
    public uint Ordinal { get; set; }
    public uint Occurrences { get; set; } = 1;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ModifiedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ValidatedAt { get; set; } = DateTimeOffset.UtcNow;

    public virtual Relation? Relation { get; set; }
    public virtual Composition? Composition { get; set; }
}
