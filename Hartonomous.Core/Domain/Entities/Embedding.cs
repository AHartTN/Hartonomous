using Hartonomous.Core.Domain.Common;
using NetTopologySuite.Geometries;

namespace Hartonomous.Core.Domain.Entities;

/// <summary>
/// Represents an embedding vector stored as MULTIPOINTZM geometry
/// Each dimension is stored as a separate point in 4D space
/// Enables geometric similarity queries using PostGIS spatial operations
/// </summary>
public sealed class Embedding : BaseEntity
{
    /// <summary>Foreign key to the constant this embedding represents</summary>
    public Guid ConstantId { get; private set; }
    
    /// <summary>Embedding vector as MULTIPOINTZM (one point per dimension)</summary>
    public MultiPoint VectorGeometry { get; private set; } = null!;
    
    /// <summary>Dimensionality (e.g., 768 for BERT, 1536 for OpenAI ada-002)</summary>
    public int Dimensions { get; private set; }
    
    /// <summary>Model that generated embedding (e.g., "BERT", "GPT", "ada-002")</summary>
    public string ModelName { get; private set; } = null!;
    
    /// <summary>Model version for tracking embedding evolution</summary>
    public string? ModelVersion { get; private set; }
    
    /// <summary>Timestamp of embedding generation</summary>
    public DateTime GeneratedAt { get; private set; }
    
    /// <summary>L2 norm of the vector (for normalized vectors, this should be ~1.0)</summary>
    public double Magnitude { get; private set; }
    
    /// <summary>Whether the embedding vector is normalized</summary>
    public bool IsNormalized { get; private set; }
    
    // Navigation property
    public Constant Constant { get; private set; } = null!;
    
    private Embedding() { } // EF Core constructor
    
    /// <summary>
    /// Create embedding from float vector
    /// </summary>
    /// <param name="constantId">ID of the constant being embedded</param>
    /// <param name="vector">Dense embedding vector</param>
    /// <param name="modelName">Name of the embedding model</param>
    /// <param name="modelVersion">Optional model version</param>
    /// <param name="normalize">Whether to normalize the vector to unit length</param>
    /// <returns>New Embedding instance</returns>
    public static Embedding Create(
        Guid constantId, 
        float[] vector, 
        string modelName,
        string? modelVersion = null,
        bool normalize = false)
    {
        if (constantId == Guid.Empty)
        {
            throw new ArgumentException("Constant ID cannot be empty", nameof(constantId));
        }
        
        if (vector == null || vector.Length == 0)
        {
            throw new ArgumentException("Vector cannot be null or empty", nameof(vector));
        }
        
        if (string.IsNullOrWhiteSpace(modelName))
        {
            throw new ArgumentException("Model name cannot be null or empty", nameof(modelName));
        }
        
        // Normalize if requested
        float[] processedVector = vector;
        bool isNormalized = normalize;
        
        if (normalize)
        {
            processedVector = NormalizeVector(vector);
        }
        
        // Compute magnitude
        double magnitude = ComputeMagnitude(processedVector);
        
        // Convert float[] to MULTIPOINTZM
        // Each point: (dimension_index, embedding_value, 0, 0)
        var points = processedVector
            .Select((value, index) => new Point(
                new CoordinateZM(
                    index,           // X: dimension index
                    value,           // Y: embedding value
                    0,               // Z: reserved for future use
                    0                // M: reserved for future use
                )
            ))
            .ToArray();
        
        var multiPoint = new MultiPoint(points) { SRID = 4326 };
        
        var now = DateTime.UtcNow;
        
        return new Embedding
        {
            Id = Guid.NewGuid(),
            ConstantId = constantId,
            VectorGeometry = multiPoint,
            Dimensions = processedVector.Length,
            ModelName = modelName,
            ModelVersion = modelVersion,
            GeneratedAt = now,
            Magnitude = magnitude,
            IsNormalized = isNormalized,
            CreatedAt = now,
            CreatedBy = "System"
        };
    }
    
    /// <summary>
    /// Create embedding from double vector
    /// </summary>
    public static Embedding Create(
        Guid constantId,
        double[] vector,
        string modelName,
        string? modelVersion = null,
        bool normalize = false)
    {
        var floatVector = vector.Select(v => (float)v).ToArray();
        return Create(constantId, floatVector, modelName, modelVersion, normalize);
    }
    
    /// <summary>
    /// Extract float array from MULTIPOINTZM geometry
    /// </summary>
    /// <returns>Dense embedding vector</returns>
    public float[] ToVector()
    {
        return VectorGeometry.Geometries
            .Cast<Point>()
            .OrderBy(p => p.X) // Sort by dimension index
            .Select(p => (float)p.Y) // Extract embedding value
            .ToArray();
    }
    
    /// <summary>
    /// Extract double array from MULTIPOINTZM geometry
    /// </summary>
    public double[] ToDoubleVector()
    {
        return VectorGeometry.Geometries
            .Cast<Point>()
            .OrderBy(p => p.X)
            .Select(p => p.Y)
            .ToArray();
    }
    
    /// <summary>
    /// Compute cosine similarity with another embedding
    /// Uses PostGIS distance in high-dimensional space as approximation
    /// </summary>
    /// <param name="other">Target embedding for comparison</param>
    /// <returns>Cosine similarity in range [-1, 1]</returns>
    public double CosineSimilarity(Embedding other)
    {
        if (other == null)
        {
            throw new ArgumentNullException(nameof(other));
        }
        
        if (Dimensions != other.Dimensions)
        {
            throw new ArgumentException(
                $"Dimension mismatch: {Dimensions} != {other.Dimensions}", 
                nameof(other));
        }
        
        // For normalized vectors, PostGIS distance can approximate cosine similarity
        // distance = sqrt(2 - 2*cosine_similarity) for normalized vectors
        // cosine_similarity = 1 - (distance^2 / 2)
        
        double distance = VectorGeometry.Distance(other.VectorGeometry);
        
        if (IsNormalized && other.IsNormalized)
        {
            // Use geometric approximation for normalized vectors
            return 1.0 - (distance * distance / 2.0);
        }
        else
        {
            // Fall back to dot product computation
            var thisVector = ToDoubleVector();
            var otherVector = other.ToDoubleVector();
            
            double dotProduct = 0;
            for (int i = 0; i < Dimensions; i++)
            {
                dotProduct += thisVector[i] * otherVector[i];
            }
            
            return dotProduct / (Magnitude * other.Magnitude);
        }
    }
    
    /// <summary>
    /// Compute Euclidean distance to another embedding
    /// </summary>
    public double EuclideanDistance(Embedding other)
    {
        if (other == null)
        {
            throw new ArgumentNullException(nameof(other));
        }
        
        if (Dimensions != other.Dimensions)
        {
            throw new ArgumentException(
                $"Dimension mismatch: {Dimensions} != {other.Dimensions}",
                nameof(other));
        }
        
        return VectorGeometry.Distance(other.VectorGeometry);
    }
    
    /// <summary>
    /// Compute Manhattan (L1) distance to another embedding
    /// </summary>
    public double ManhattanDistance(Embedding other)
    {
        if (other == null)
        {
            throw new ArgumentNullException(nameof(other));
        }
        
        if (Dimensions != other.Dimensions)
        {
            throw new ArgumentException(
                $"Dimension mismatch: {Dimensions} != {other.Dimensions}",
                nameof(other));
        }
        
        var thisVector = ToDoubleVector();
        var otherVector = other.ToDoubleVector();
        
        double distance = 0;
        for (int i = 0; i < Dimensions; i++)
        {
            distance += Math.Abs(thisVector[i] - otherVector[i]);
        }
        
        return distance;
    }
    
    /// <summary>
    /// Normalize this embedding vector to unit length
    /// </summary>
    public void Normalize()
    {
        if (IsNormalized)
        {
            return; // Already normalized
        }
        
        var vector = ToVector();
        var normalized = NormalizeVector(vector);
        
        // Rebuild geometry with normalized values
        var points = normalized
            .Select((value, index) => new Point(
                new CoordinateZM(index, value, 0, 0)
            ))
            .ToArray();
        
        VectorGeometry = new MultiPoint(points) { SRID = 4326 };
        Magnitude = 1.0;
        IsNormalized = true;
        UpdatedAt = DateTime.UtcNow;
    }
    
    // Private helper methods
    
    private static float[] NormalizeVector(float[] vector)
    {
        double magnitude = ComputeMagnitude(vector);
        
        if (magnitude == 0)
        {
            throw new InvalidOperationException("Cannot normalize zero vector");
        }
        
        return vector.Select(v => (float)(v / magnitude)).ToArray();
    }
    
    private static double ComputeMagnitude(float[] vector)
    {
        double sumOfSquares = 0;
        foreach (var value in vector)
        {
            sumOfSquares += value * value;
        }
        return Math.Sqrt(sumOfSquares);
    }
}
