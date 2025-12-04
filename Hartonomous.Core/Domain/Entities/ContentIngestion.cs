using Hartonomous.Core.Domain.Common;
using Hartonomous.Core.Domain.Enums;
using Hartonomous.Core.Domain.ValueObjects;

namespace Hartonomous.Core.Domain.Entities;

/// <summary>
/// Tracks content ingestion process from raw input to constant decomposition
/// Maintains provenance and enables tracing back to original sources
/// </summary>
public class ContentIngestion : BaseEntity
{
    /// <summary>Original content hash</summary>
    public Hash256 ContentHash { get; private set; } = null!;
    
    /// <summary>Type of content ingested</summary>
    public ContentType ContentType { get; private set; }
    
    /// <summary>Original content size in bytes</summary>
    public long OriginalSize { get; private set; }
    
    /// <summary>Number of constants extracted</summary>
    public int ConstantCount { get; private set; }
    
    /// <summary>Number of unique constants (after deduplication)</summary>
    public int UniqueConstantCount { get; private set; }
    
    /// <summary>Deduplication ratio (unique/total)</summary>
    public double DeduplicationRatio { get; private set; }
    
    /// <summary>Total processing time in milliseconds</summary>
    public long ProcessingTimeMs { get; private set; }
    
    /// <summary>Source identifier (filename, URL, etc.)</summary>
    public string? SourceIdentifier { get; private set; }
    
    /// <summary>Metadata about the ingestion (JSON)</summary>
    public string? Metadata { get; private set; }
    
    /// <summary>Ingestion start timestamp</summary>
    public DateTime StartedAt { get; private set; }
    
    /// <summary>Ingestion completion timestamp</summary>
    public DateTime? CompletedAt { get; private set; }
    
    /// <summary>Ingestion failure timestamp</summary>
    public DateTime? FailedAt { get; private set; }
    
    /// <summary>Whether ingestion succeeded</summary>
    public bool IsSuccessful { get; private set; }
    
    /// <summary>Error message if ingestion failed</summary>
    public string? ErrorMessage { get; private set; }
    
    /// <summary>IDs of constants created from this ingestion</summary>
    public List<Guid> ConstantIds { get; private set; } = new();
    
    private ContentIngestion() { } // EF Core constructor
    
    public static ContentIngestion Create(
        byte[] content, 
        ContentType contentType, 
        string? sourceIdentifier = null,
        string? metadata = null)
    {
        if (content == null || content.Length == 0)
        {
            throw new ArgumentException("Content cannot be null or empty", nameof(content));
        }
        
        var hash = Hash256.Compute(content);
        var now = DateTime.UtcNow;
        
        var ingestion = new ContentIngestion
        {
            Id = Guid.NewGuid(),
            ContentHash = hash,
            ContentType = contentType,
            OriginalSize = content.Length,
            ConstantCount = 0,
            UniqueConstantCount = 0,
            DeduplicationRatio = 0.0,
            ProcessingTimeMs = 0,
            SourceIdentifier = sourceIdentifier,
            Metadata = metadata,
            StartedAt = now,
            IsSuccessful = false,
            CreatedAt = now
        };
        
        return ingestion;
    }
    
    public void RecordConstants(List<Guid> constantIds, int uniqueCount)
    {
        if (constantIds == null)
        {
            throw new ArgumentNullException(nameof(constantIds));
        }
        
        if (uniqueCount < 0 || uniqueCount > constantIds.Count)
        {
            throw new ArgumentException("Invalid unique count", nameof(uniqueCount));
        }
        
        ConstantIds = new List<Guid>(constantIds);
        ConstantCount = constantIds.Count;
        UniqueConstantCount = uniqueCount;
        DeduplicationRatio = ConstantCount > 0 ? (double)UniqueConstantCount / ConstantCount : 0.0;
        UpdatedAt = DateTime.UtcNow;
    }
    
    public void Complete(long processingTimeMs)
    {
        if (processingTimeMs < 0)
        {
            throw new ArgumentException("Processing time cannot be negative", nameof(processingTimeMs));
        }
        
        ProcessingTimeMs = processingTimeMs;
        CompletedAt = DateTime.UtcNow;
        IsSuccessful = true;
        UpdatedAt = DateTime.UtcNow;
    }
    
    public void Fail(string errorMessage)
    {
        if (string.IsNullOrWhiteSpace(errorMessage))
        {
            throw new ArgumentException("Error message cannot be null or empty", nameof(errorMessage));
        }
        
        ErrorMessage = errorMessage;
        FailedAt = DateTime.UtcNow;
        CompletedAt = DateTime.UtcNow;
        IsSuccessful = false;
        UpdatedAt = DateTime.UtcNow;
    }
}
