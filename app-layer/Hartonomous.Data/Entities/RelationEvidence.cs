using System;
using Hartonomous.Core.Primitives;

namespace Hartonomous.Data.Entities;

public class RelationEvidence
{
    public HartonomousId Id { get; set; }
    public HartonomousId ContentId { get; set; }
    public HartonomousId RelationId { get; set; }
    public bool IsValid { get; set; } = true;
    public double SourceRating { get; set; } = 1000;
    public double SignalStrength { get; set; } = 1.0;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ModifiedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ValidatedAt { get; set; } = DateTimeOffset.UtcNow;

    public virtual Content? Content { get; set; }
    public virtual Relation? Relation { get; set; }
}
