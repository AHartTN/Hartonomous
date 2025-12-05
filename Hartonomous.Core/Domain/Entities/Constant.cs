using Hartonomous.Core.Domain.Common;
using Hartonomous.Core.Domain.Enums;
using Hartonomous.Core.Domain.ValueObjects;
using NetTopologySuite.Geometries;

namespace Hartonomous.Core.Domain.Entities;

/// <summary>
/// Represents an atomic constant in the content-addressable storage system
/// Core primitive for universal deduplication and atomic decomposition
/// </summary>
public class Constant : BaseEntity
{
    /// <summary>Content hash (SHA-256) serving as content address</summary>
    public Hash256 Hash { get; private set; } = null!;
    
    /// <summary>Raw constant data (byte sequence)</summary>
    public byte[] Data { get; private set; } = null!;
    
    /// <summary>Size in bytes</summary>
    public int Size { get; private set; }
    
    /// <summary>Type of content this constant originates from</summary>
    public ContentType ContentType { get; private set; }
    
    /// <summary>Processing status</summary>
    public ConstantStatus Status { get; private set; }
    
    /// <summary>Timestamp when constant was projected to spatial coordinates</summary>
    public DateTime? ProjectedAt { get; private set; }
    
    /// <summary>Timestamp when constant became active</summary>
    public DateTime? ActivatedAt { get; private set; }
    
    /// <summary>3D spatial coordinate from deterministic hash projection</summary>
    public SpatialCoordinate? Coordinate { get; private set; }
    
    /// <summary>PostGIS Point for spatial indexing and queries</summary>
    public Point? Location { get; private set; }
    
    /// <summary>Reference count - number of compositions using this constant</summary>
    public long ReferenceCount { get; private set; }
    
    /// <summary>If deduplicated, ID of the canonical constant</summary>
    public Guid? CanonicalConstantId { get; private set; }
    
    /// <summary>Navigation property to canonical constant</summary>
    public Constant? CanonicalConstant { get; private set; }
    
    /// <summary>Whether this constant is a duplicate</summary>
    public bool IsDuplicate { get; private set; }
    
    /// <summary>Timestamp when constant was deduplicated</summary>
    public DateTime? DeduplicatedAt { get; private set; }
    
    /// <summary>Frequency of occurrence across all ingested content</summary>
    public long Frequency { get; private set; }
    
    /// <summary>First ingestion timestamp</summary>
    public DateTime FirstSeenAt { get; private set; }
    
    /// <summary>Last access timestamp for cache eviction</summary>
    public DateTime LastAccessedAt { get; private set; }
    
    /// <summary>Error message if processing failed</summary>
    public string? ErrorMessage { get; private set; }
    
    // Navigation properties
    public ICollection<BPEToken> ComposingTokens { get; private set; } = new List<BPEToken>();
    
    private Constant() { } // EF Core constructor
    
    public static Constant Create(byte[] data, ContentType contentType)
    {
        if (data == null || data.Length == 0)
        {
            throw new ArgumentException("Constant data cannot be null or empty", nameof(data));
        }
        
        var hash = Hash256.Compute(data);
        var now = DateTime.UtcNow;
        
        var constant = new Constant
        {
            Id = Guid.NewGuid(),
            Hash = hash,
            Data = (byte[])data.Clone(),
            Size = data.Length,
            ContentType = contentType,
            Status = ConstantStatus.Pending,
            ReferenceCount = 0,
            Frequency = 1,
            FirstSeenAt = now,
            LastAccessedAt = now,
            CreatedAt = now
        };
        
        return constant;
    }
    
    public void Project()
    {
        if (Status != ConstantStatus.Pending)
        {
            throw new InvalidOperationException($"Cannot project constant in status {Status}");
        }
        
        // Use placeholder values for backward compatibility
        // Applications should use ProjectWithQuantization for actual quantization
        const int placeholderEntropy = 1_048_576; // Mid-range
        const int placeholderCompressibility = 1_048_576; // Mid-range
        const int placeholderConnectivity = 0; // No references yet
        
        // Compute deterministic spatial coordinate from hash + metadata
        Coordinate = SpatialCoordinate.FromHash(
            Hash,
            placeholderEntropy,
            placeholderCompressibility,
            placeholderConnectivity);
        
        // Create PostGIS POINTZM for spatial indexing
        Location = Coordinate.ToPoint();
        
        Status = ConstantStatus.Projected;
        ProjectedAt = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
    }
    
    public void ProjectWithQuantization(int quantizedEntropy, int quantizedCompressibility, int quantizedConnectivity)
    {
        if (Status != ConstantStatus.Pending)
        {
            throw new InvalidOperationException($"Cannot project constant in status {Status}");
        }
        
        // Compute deterministic spatial coordinate from hash + actual quantized metadata
        Coordinate = SpatialCoordinate.FromHash(
            Hash,
            quantizedEntropy,
            quantizedCompressibility,
            quantizedConnectivity);
        
        // Create PostGIS POINTZM for spatial indexing
        Location = Coordinate.ToPoint();
        
        Status = ConstantStatus.Projected;
        ProjectedAt = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
    }
    
    public void Activate()
    {
        if (Status != ConstantStatus.Projected)
        {
            throw new InvalidOperationException($"Cannot activate constant in status {Status}");
        }
        
        Status = ConstantStatus.Active;
        ActivatedAt = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
    }
    
    public void MarkAsDuplicate(Guid canonicalConstantId)
    {
        if (canonicalConstantId == Guid.Empty)
        {
            throw new ArgumentException("Canonical constant ID cannot be empty", nameof(canonicalConstantId));
        }
        
        if (canonicalConstantId == Id)
        {
            throw new ArgumentException("Cannot mark constant as duplicate of itself", nameof(canonicalConstantId));
        }
        
        CanonicalConstantId = canonicalConstantId;
        IsDuplicate = true;
        DeduplicatedAt = DateTime.UtcNow;
        Status = ConstantStatus.Deduplicated;
        UpdatedAt = DateTime.UtcNow;
    }
    
    public void IncrementReferenceCount()
    {
        ReferenceCount++;
        UpdatedAt = DateTime.UtcNow;
    }
    
    public void DecrementReferenceCount()
    {
        if (ReferenceCount > 0)
        {
            ReferenceCount--;
            UpdatedAt = DateTime.UtcNow;
        }
    }
    
    public void IncrementFrequency()
    {
        Frequency++;
        LastAccessedAt = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
    }
    
    public void RecordAccess()
    {
        LastAccessedAt = DateTime.UtcNow;
    }
    
    public void MarkAsFailed(string errorMessage)
    {
        if (string.IsNullOrWhiteSpace(errorMessage))
        {
            throw new ArgumentException("Error message cannot be null or empty", nameof(errorMessage));
        }
        
        Status = ConstantStatus.Failed;
        ErrorMessage = errorMessage;
        UpdatedAt = DateTime.UtcNow;
    }
    
    public void Archive()
    {
        if (Status != ConstantStatus.Active)
        {
            throw new InvalidOperationException($"Can only archive active constants, current status: {Status}");
        }
        
        Status = ConstantStatus.Archived;
        UpdatedAt = DateTime.UtcNow;
    }
}
