namespace Hartonomous.Core.Domain.Common;

/// <summary>
/// Base aggregate root for DDD aggregates
/// </summary>
public abstract class AggregateRoot : BaseEntity
{
    public int Version { get; set; }
}
