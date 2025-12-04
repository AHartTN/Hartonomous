using Hartonomous.Core.Domain.Common;
using Hartonomous.Core.Domain.ValueObjects;
using NetTopologySuite.Geometries;

namespace Hartonomous.Core.Domain.Entities;

/// <summary>
/// Byte Pair Encoding token representing a composition of constants
/// Learns frequent patterns automatically through BPE algorithm
/// Stores composition as LINESTRINGZM geometry for spatial queries
/// </summary>
public class BPEToken : AggregateRoot
{
    /// <summary>Unique token identifier (learned through BPE)</summary>
    public int TokenId { get; private set; }
    
    /// <summary>Hash of the composed sequence</summary>
    public Hash256 Hash { get; private set; } = null!;
    
    /// <summary>Ordered sequence of constant IDs that compose this token</summary>
    public List<Guid> ConstantSequence { get; private set; } = new();
    
    /// <summary>LINESTRINGZM geometry representing composition path through 4D space</summary>
    public LineString? CompositionGeometry { get; private set; }
    
    /// <summary>Total geometric length of composition path in 4D space</summary>
    public double? PathLength { get; private set; }
    
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
        
        // Create single-point linestring if constant has location
        LineString? geometry = null;
        double? pathLength = null;
        
        if (constant.Location != null)
        {
            // Single point becomes a degenerate linestring (start = end)
            var coords = new[] {
                new CoordinateZM(
                    constant.Location.X,
                    constant.Location.Y,
                    constant.Location.Z,
                    constant.Location.M
                ),
                new CoordinateZM(
                    constant.Location.X,
                    constant.Location.Y,
                    constant.Location.Z,
                    constant.Location.M
                )
            };
            geometry = new LineString(coords) { SRID = 4326 };
            pathLength = 0; // Degenerate linestring has zero length
        }
        
        var token = new BPEToken
        {
            Id = Guid.NewGuid(),
            TokenId = tokenId,
            Hash = constant.Hash,
            ConstantSequence = new List<Guid> { constant.Id },
            CompositionGeometry = geometry,
            PathLength = pathLength,
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
        
        var constantList = constants.ToList();
        
        // Compute hash of merged sequence
        var mergedData = new byte[leftToken.TotalSize + rightToken.TotalSize];
        var offset = 0;
        
        foreach (var constant in constantList)
        {
            Array.Copy(constant.Data, 0, mergedData, offset, constant.Data.Length);
            offset += constant.Data.Length;
        }
        
        var hash = Hash256.Compute(mergedData);
        
        // Build composition geometry from ordered constants
        LineString? geometry = null;
        double? pathLength = null;
        
        var constantsWithLocations = constantList.Where(c => c.Location != null).ToList();
        if (constantsWithLocations.Count >= 2)
        {
            var coords = constantsWithLocations.Select(c => new CoordinateZM(
                c.Location!.X,
                c.Location.Y,
                c.Location.Z,
                c.Location.M
            )).ToArray();
            
            geometry = new LineString(coords) { SRID = 4326 };
            pathLength = geometry.Length;
        }
        
        var now = DateTime.UtcNow;
        
        var token = new BPEToken
        {
            Id = Guid.NewGuid(),
            TokenId = tokenId,
            Hash = hash,
            ConstantSequence = mergedSequence,
            CompositionGeometry = geometry,
            PathLength = pathLength,
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
        
        foreach (var constant in constantList)
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
        
        // Build composition geometry from ordered constants
        LineString? geometry = null;
        double? pathLength = null;
        
        var constantsWithLocations = constantList.Where(c => c.Location != null).ToList();
        if (constantsWithLocations.Count >= 2)
        {
            var coords = constantsWithLocations.Select(c => new CoordinateZM(
                c.Location!.X,
                c.Location.Y,
                c.Location.Z,
                c.Location.M
            )).ToArray();
            
            geometry = new LineString(coords) { SRID = 4326 };
            pathLength = geometry.Length;
        }
        else if (constantsWithLocations.Count == 1)
        {
            // Single point - create degenerate linestring
            var loc = constantsWithLocations[0].Location!;
            var coords = new[] {
                new CoordinateZM(loc.X, loc.Y, loc.Z, loc.M),
                new CoordinateZM(loc.X, loc.Y, loc.Z, loc.M)
            };
            geometry = new LineString(coords) { SRID = 4326 };
            pathLength = 0;
        }
        
        var now = DateTime.UtcNow;
        
        var token = new BPEToken
        {
            Id = Guid.NewGuid(),
            TokenId = tokenId,
            Hash = hash,
            ConstantSequence = new List<Guid>(constantSequence),
            CompositionGeometry = geometry,
            PathLength = pathLength,
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
    
    /// <summary>Get start point of composition geometry</summary>
    public Point? StartPoint => CompositionGeometry?.StartPoint;
    
    /// <summary>Get end point of composition geometry</summary>
    public Point? EndPoint => CompositionGeometry?.EndPoint;
    
    /// <summary>Check if token's geometry contains a specific point</summary>
    public bool ContainsPoint(Point point)
    {
        if (CompositionGeometry == null || point == null)
        {
            return false;
        }
        
        return CompositionGeometry.Contains(point);
    }
    
    /// <summary>Check if token contains a specific constant by ID</summary>
    public bool ContainsConstant(Guid constantId)
    {
        return ConstantSequence.Contains(constantId);
    }
    
    /// <summary>Compute geometric distance to another token's composition</summary>
    public double? GeometricDistanceTo(BPEToken other)
    {
        if (other == null)
        {
            throw new ArgumentNullException(nameof(other));
        }
        
        if (CompositionGeometry == null || other.CompositionGeometry == null)
        {
            return null;
        }
        
        return CompositionGeometry.Distance(other.CompositionGeometry);
    }
    
    /// <summary>Check if this token's geometry intersects another's</summary>
    public bool IntersectsWith(BPEToken other)
    {
        if (other?.CompositionGeometry == null || CompositionGeometry == null)
        {
            return false;
        }
        
        return CompositionGeometry.Intersects(other.CompositionGeometry);
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
