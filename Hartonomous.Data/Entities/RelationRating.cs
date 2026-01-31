using System;
using Hartonomous.Core.Primitives;

namespace Hartonomous.Data.Entities;

public class RelationRating
{
    public HartonomousId RelationId { get; set; }
    public ulong Observations { get; set; } = 1;
    public double RatingValue { get; set; } = 1000;
    public double KFactor { get; set; } = 1.0;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ModifiedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ValidatedAt { get; set; } = DateTimeOffset.UtcNow;

    public virtual Relation? Relation { get; set; }
}
