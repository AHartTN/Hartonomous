using Hartonomous.Core.Domain.Common;
using Hartonomous.Core.Domain.ValueObjects;

namespace Hartonomous.Core.Domain.Entities;

/// <summary>
/// Byte Pair Encoding token representing a composition of constants
/// Learns frequent patterns automatically through BPE algorithm
/// </summary>
public class BPEToken : AggregateRoot
{
    /// <summary>Unique token identifier (learned through BPE)</summary>
    public int TokenId { get; private set; }
    
    /// <summary>Hash of the composed sequence</summary>
    public Hash256 Hash { get; private set; } = null!;
    
    /// <summary>Ordered sequence of constant IDs that compose this token</summary>
    public List<Guid> ConstantSequence { get; private set; } = new();
    
    /// <summary>Total size in bytes of composed constants</summary>
    public int TotalSize { get; private set; }
    
    /// <summary>Number of constants in the sequence</summary>
    public int SequenceLength { get; private set; }
    
    /// <summary>Frequency of this token across all content</summary>
    public long Frequency { get; private set; }
    
    /// <summary>BPE merge level (0 = single constant, higher = more merged)</summary>
    public int MergeLevel { get; private set; }
    
    /// <summary>Parent token IDs that were merged to create this token</summary>
    public List<int>? ParentTokenIds { get; private set; }
    
    /// <summary>Whether this token is actively used in composition</summary>
    public bool IsActive { get; private set; }
    
    /// <summary>Compression ratio compared to raw data</summary>
    public double CompressionRatio { get; private set; }
    
    /// <summary>Last time this token was used</summary>
    public DateTime LastUsedAt { get; private set; }
    
    /// <summary>Number of times this token appears in BPE vocabulary</summary>
    public long VocabularyRank { get; private set; }
    
    // Navigation properties
    public ICollection<Constant> Constants { get; private set; } = new List<Constant>();
    
    private BPEToken() { } // EF Core constructor
    
    public static BPEToken CreateFromSingleConstant(int tokenId, Constant constant)
    {
        if (constant == null)
        {
            throw new ArgumentNullException(nameof(constant));
        }
        
        if (tokenId < 0)
        {
            throw new ArgumentException("Token ID must be non-negative", nameof(tokenId));
        }
        
        var now = DateTime.UtcNow;
        
        var token = new BPEToken
        {
            Id = Guid.NewGuid(),
            TokenId = tokenId,
            Hash = constant.Hash,
            ConstantSequence = new List<Guid> { constant.Id },
            TotalSize = constant.Size,
            SequenceLength = 1,
            Frequency = 1,
            MergeLevel = 0,
            IsActive = true,
            CompressionRatio = 1.0,
            LastUsedAt = now,
            VocabularyRank = 0,
            CreatedAt = now,
            Version = 1
        };
        
        token.Constants.Add(constant);
        
        return token;
    }
    
    public static BPEToken CreateFromMerge(
        int tokenId, 
        BPEToken leftToken, 
        BPEToken rightToken, 
        long frequency,
        IEnumerable<Constant> constants)
    {
        if (leftToken == null)
        {
            throw new ArgumentNullException(nameof(leftToken));
        }
        
        if (rightToken == null)
        {
            throw new ArgumentNullException(nameof(rightToken));
        }
        
        if (tokenId < 0)
        {
            throw new ArgumentException("Token ID must be non-negative", nameof(tokenId));
        }
        
        if (frequency < 0)
        {
            throw new ArgumentException("Frequency cannot be negative", nameof(frequency));
        }
        
        // Merge constant sequences
        var mergedSequence = new List<Guid>();
        mergedSequence.AddRange(leftToken.ConstantSequence);
        mergedSequence.AddRange(rightToken.ConstantSequence);
        
        // Compute hash of merged sequence
        var mergedData = new byte[leftToken.TotalSize + rightToken.TotalSize];
        var offset = 0;
        
        foreach (var constant in constants)
        {
            Array.Copy(constant.Data, 0, mergedData, offset, constant.Data.Length);
            offset += constant.Data.Length;
        }
        
        var hash = Hash256.Compute(mergedData);
        var now = DateTime.UtcNow;
        
        var token = new BPEToken
        {
            Id = Guid.NewGuid(),
            TokenId = tokenId,
            Hash = hash,
            ConstantSequence = mergedSequence,
            TotalSize = leftToken.TotalSize + rightToken.TotalSize,
            SequenceLength = leftToken.SequenceLength + rightToken.SequenceLength,
            Frequency = frequency,
            MergeLevel = Math.Max(leftToken.MergeLevel, rightToken.MergeLevel) + 1,
            ParentTokenIds = new List<int> { leftToken.TokenId, rightToken.TokenId },
            IsActive = true,
            CompressionRatio = CalculateCompressionRatio(mergedSequence.Count, leftToken.TotalSize + rightToken.TotalSize),
            LastUsedAt = now,
            VocabularyRank = 0,
            CreatedAt = now,
            Version = 1
        };
        
        foreach (var constant in constants)
        {
            token.Constants.Add(constant);
        }
        
        return token;
    }
    
    /// <summary>
    /// Create BPE token from a sequence of constants (simplified - for direct constant merging)
    /// </summary>
    public static BPEToken CreateFromConstantSequence(
        int tokenId,
        List<Guid> constantSequence,
        Hash256 hash,
        int mergeLevel,
        IEnumerable<Constant> constants)
    {
        if (constantSequence == null || !constantSequence.Any())
        {
            throw new ArgumentException("Constant sequence cannot be empty", nameof(constantSequence));
        }
        
        if (tokenId < 0)
        {
            throw new ArgumentException("Token ID must be non-negative", nameof(tokenId));
        }
        
        var constantList = constants.ToList();
        var totalSize = constantList.Sum(c => c.Size);
        var now = DateTime.UtcNow;
        
        var token = new BPEToken
        {
            Id = Guid.NewGuid(),
            TokenId = tokenId,
            Hash = hash,
            ConstantSequence = new List<Guid>(constantSequence),
            TotalSize = totalSize,
            SequenceLength = constantSequence.Count,
            Frequency = 1,
            MergeLevel = mergeLevel,
            IsActive = true,
            CompressionRatio = CalculateCompressionRatio(constantSequence.Count, totalSize),
            LastUsedAt = now,
            VocabularyRank = 0,
            CreatedAt = now,
            Version = 1
        };
        
        foreach (var constant in constantList)
        {
            token.Constants.Add(constant);
        }
        
        return token;
    }
    
    private static double CalculateCompressionRatio(int sequenceLength, int totalSize)
    {
        // Compression ratio = original size / compressed size
        // Assume each constant reference is 16 bytes (Guid), raw size is totalSize
        var compressedSize = sequenceLength * 16;
        return totalSize > 0 ? (double)totalSize / compressedSize : 1.0;
    }
    
    public void IncrementFrequency()
    {
        Frequency++;
        LastUsedAt = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
        Version++;
    }
    
    public void UpdateVocabularyRank(long rank)
    {
        if (rank < 0)
        {
            throw new ArgumentException("Vocabulary rank cannot be negative", nameof(rank));
        }
        
        VocabularyRank = rank;
        UpdatedAt = DateTime.UtcNow;
        Version++;
    }
    
    public void Deactivate()
    {
        IsActive = false;
        UpdatedAt = DateTime.UtcNow;
        Version++;
    }
    
    public void Reactivate()
    {
        IsActive = true;
        LastUsedAt = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
        Version++;
    }
    
    public void RecordUsage()
    {
        LastUsedAt = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
    }
}
