namespace Hartonomous.Core.Domain.Enums;

/// <summary>
/// Processing status of a constant in the system
/// </summary>
public enum ConstantStatus
{
    /// <summary>Constant newly ingested, pending processing</summary>
    Pending = 0,
    
    /// <summary>Hash, spatial coordinates, and Hilbert index computed</summary>
    Projected = 1,
    
    /// <summary>Available for BPE composition</summary>
    Active = 2,
    
    /// <summary>Marked as duplicate, references canonical constant</summary>
    Deduplicated = 3,
    
    /// <summary>Archived but retained for historical queries</summary>
    Archived = 4,
    
    /// <summary>Failed processing with error</summary>
    Failed = 5
}
