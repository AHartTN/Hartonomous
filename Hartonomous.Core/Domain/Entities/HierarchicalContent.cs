using Hartonomous.Core.Domain.Common;
using Hartonomous.Core.Domain.ValueObjects;
using NetTopologySuite.Geometries;

namespace Hartonomous.Core.Domain.Entities;

/// <summary>
/// Represents hierarchical content structure using GEOMETRYCOLLECTIONZM
/// Combines multiple geometry types (boundaries, atoms, sequences) in single geometry
/// Enables complex document structures with parent-child relationships
/// </summary>
public sealed class HierarchicalContent : BaseEntity
{
    /// <summary>Foreign key to content ingestion</summary>
    public Guid ContentIngestionId { get; private set; }
    
    /// <summary>Complete geometric representation (GEOMETRYCOLLECTIONZM)</summary>
    public GeometryCollection CompleteGeometry { get; private set; } = null!;
    
    /// <summary>4D bounding box of all geometries</summary>
    public BoundingBox4D BoundingBox { get; private set; } = null!;
    
    /// <summary>Hierarchy level (0 = document, 1 = chapter, 2 = section, 3 = paragraph...)</summary>
    public int HierarchyLevel { get; private set; }
    
    /// <summary>Parent content ID (null for root)</summary>
    public Guid? ParentId { get; private set; }
    
    /// <summary>Semantic label (e.g., "document", "chapter", "section", "paragraph")</summary>
    public string Label { get; private set; } = null!;
    
    /// <summary>Human-readable title or heading</summary>
    public string? Title { get; private set; }
    
    /// <summary>Order/position within parent (0-based)</summary>
    public int Ordinal { get; private set; }
    
    /// <summary>Number of atoms in this content</summary>
    public int AtomCount { get; private set; }
    
    /// <summary>Number of direct children</summary>
    public int ChildCount { get; private set; }
    
    /// <summary>Total descendants (children + grandchildren + ...)</summary>
    public int DescendantCount { get; private set; }
    
    /// <summary>Centroid of all geometries in collection</summary>
    public Point Centroid { get; private set; } = null!;
    
    /// <summary>Start position in original content (byte offset)</summary>
    public long? StartOffset { get; private set; }
    
    /// <summary>End position in original content (byte offset)</summary>
    public long? EndOffset { get; private set; }
    
    /// <summary>Metadata as JSON (flexible schema)</summary>
    public string? Metadata { get; private set; }
    
    // Navigation properties
    public ContentIngestion ContentIngestion { get; private set; } = null!;
    public HierarchicalContent? Parent { get; private set; }
    public ICollection<HierarchicalContent> Children { get; private set; } = new List<HierarchicalContent>();
    
    // Convenience properties (delegated to BoundingBox)
    public double MinX => BoundingBox.MinX;
    public double MaxX => BoundingBox.MaxX;
    public double MinY => BoundingBox.MinY;
    public double MaxY => BoundingBox.MaxY;
    
    private HierarchicalContent() { } // EF Core constructor
    
    /// <summary>
    /// Create hierarchical content from geometry collection
    /// </summary>
    /// <param name="contentIngestionId">ID of content ingestion</param>
    /// <param name="geometries">Collection of geometries (points, linestrings, polygons)</param>
    /// <param name="hierarchyLevel">Depth in hierarchy tree</param>
    /// <param name="label">Semantic label</param>
    /// <param name="parentId">Optional parent ID</param>
    /// <param name="ordinal">Position within parent</param>
    /// <param name="title">Optional title/heading</param>
    /// <returns>New HierarchicalContent instance</returns>
    public static HierarchicalContent Create(
        Guid contentIngestionId,
        IEnumerable<Geometry> geometries,
        int hierarchyLevel,
        string label,
        Guid? parentId = null,
        int ordinal = 0,
        string? title = null)
    {
        if (contentIngestionId == Guid.Empty)
        {
            throw new ArgumentException("Content ingestion ID cannot be empty", nameof(contentIngestionId));
        }
        
        if (hierarchyLevel < 0)
        {
            throw new ArgumentException("Hierarchy level cannot be negative", nameof(hierarchyLevel));
        }
        
        if (string.IsNullOrWhiteSpace(label))
        {
            throw new ArgumentException("Label cannot be null or empty", nameof(label));
        }
        
        if (ordinal < 0)
        {
            throw new ArgumentException("Ordinal cannot be negative", nameof(ordinal));
        }
        
        var geometryArray = geometries?.ToArray() ?? throw new ArgumentNullException(nameof(geometries));
        
        if (geometryArray.Length == 0)
        {
            throw new ArgumentException("Must provide at least one geometry", nameof(geometries));
        }
        
        // Create geometry collection
        var collection = new GeometryCollection(geometryArray) { SRID = 4326 };
        
        // Compute centroid
        var centroid = collection.Centroid;
        centroid.SRID = 4326;
        
        // Compute 4D bounding box
        var boundingBox = BoundingBox4D.FromGeometry(collection);
        
        // Count atoms (POINT geometries)
        int atomCount = geometryArray.Count(g => g is Point);
        
        var now = DateTime.UtcNow;
        
        return new HierarchicalContent
        {
            Id = Guid.NewGuid(),
            ContentIngestionId = contentIngestionId,
            CompleteGeometry = collection,
            BoundingBox = boundingBox,
            HierarchyLevel = hierarchyLevel,
            ParentId = parentId,
            Label = label,
            Title = title,
            Ordinal = ordinal,
            AtomCount = atomCount,
            ChildCount = 0,
            DescendantCount = 0,
            Centroid = centroid,
            CreatedAt = now,
            CreatedBy = "System"
        };
    }
    
    /// <summary>
    /// Create document root (hierarchy level 0)
    /// </summary>
    public static HierarchicalContent CreateDocument(
        Guid contentIngestionId,
        IEnumerable<Geometry> geometries,
        string? title = null)
    {
        return Create(
            contentIngestionId,
            geometries,
            hierarchyLevel: 0,
            label: "document",
            parentId: null,
            ordinal: 0,
            title: title);
    }
    
    /// <summary>
    /// Create chapter (hierarchy level 1)
    /// </summary>
    public static HierarchicalContent CreateChapter(
        Guid contentIngestionId,
        Guid documentId,
        IEnumerable<Geometry> geometries,
        int chapterNumber,
        string? title = null)
    {
        return Create(
            contentIngestionId,
            geometries,
            hierarchyLevel: 1,
            label: "chapter",
            parentId: documentId,
            ordinal: chapterNumber,
            title: title);
    }
    
    /// <summary>
    /// Create section (hierarchy level 2)
    /// </summary>
    public static HierarchicalContent CreateSection(
        Guid contentIngestionId,
        Guid chapterId,
        IEnumerable<Geometry> geometries,
        int sectionNumber,
        string? title = null)
    {
        return Create(
            contentIngestionId,
            geometries,
            hierarchyLevel: 2,
            label: "section",
            parentId: chapterId,
            ordinal: sectionNumber,
            title: title);
    }
    
    /// <summary>
    /// Create paragraph (hierarchy level 3)
    /// </summary>
    public static HierarchicalContent CreateParagraph(
        Guid contentIngestionId,
        Guid sectionId,
        IEnumerable<Geometry> geometries,
        int paragraphNumber)
    {
        return Create(
            contentIngestionId,
            geometries,
            hierarchyLevel: 3,
            label: "paragraph",
            parentId: sectionId,
            ordinal: paragraphNumber,
            title: null);
    }
    
    /// <summary>
    /// Add child to this hierarchical content
    /// </summary>
    public void AddChild(HierarchicalContent child)
    {
        if (child == null)
        {
            throw new ArgumentNullException(nameof(child));
        }
        
        if (child.ParentId != Id)
        {
            throw new ArgumentException("Child's ParentId must match this content's Id", nameof(child));
        }
        
        if (child.HierarchyLevel != HierarchyLevel + 1)
        {
            throw new ArgumentException(
                $"Child hierarchy level must be one more than parent: {HierarchyLevel + 1} != {child.HierarchyLevel}",
                nameof(child));
        }
        
        Children.Add(child);
        ChildCount++;
        UpdatedAt = DateTime.UtcNow;
    }
    
    /// <summary>
    /// Set byte offsets in original content
    /// </summary>
    public void SetOffsets(long startOffset, long endOffset)
    {
        if (startOffset < 0)
        {
            throw new ArgumentException("Start offset cannot be negative", nameof(startOffset));
        }
        
        if (endOffset < startOffset)
        {
            throw new ArgumentException("End offset must be >= start offset", nameof(endOffset));
        }
        
        StartOffset = startOffset;
        EndOffset = endOffset;
        UpdatedAt = DateTime.UtcNow;
    }
    
    /// <summary>
    /// Set metadata (as JSON string)
    /// </summary>
    public void SetMetadata(string metadata)
    {
        if (string.IsNullOrWhiteSpace(metadata))
        {
            throw new ArgumentException("Metadata cannot be null or empty", nameof(metadata));
        }
        
        Metadata = metadata;
        UpdatedAt = DateTime.UtcNow;
    }
    
    /// <summary>
    /// Update descendant count (recursive up the tree)
    /// </summary>
    public void UpdateDescendantCount(int delta)
    {
        DescendantCount += delta;
        UpdatedAt = DateTime.UtcNow;
    }
    
    /// <summary>
    /// Check if this content contains a point
    /// </summary>
    public bool Contains(Point point)
    {
        if (point == null)
        {
            return false;
        }
        
        return CompleteGeometry.Contains(point);
    }
    
    /// <summary>
    /// Check if this content intersects with another
    /// </summary>
    public bool Intersects(HierarchicalContent other)
    {
        if (other == null)
        {
            return false;
        }
        
        return CompleteGeometry.Intersects(other.CompleteGeometry);
    }
    
    /// <summary>
    /// Compute distance to another hierarchical content
    /// </summary>
    public double DistanceTo(HierarchicalContent other)
    {
        if (other == null)
        {
            throw new ArgumentNullException(nameof(other));
        }
        
        return Centroid.Distance(other.Centroid);
    }
    
    /// <summary>
    /// Get all geometries of specific type from collection
    /// </summary>
    public IEnumerable<T> GetGeometriesOfType<T>() where T : Geometry
    {
        return CompleteGeometry.Geometries
            .OfType<T>();
    }
    
    /// <summary>
    /// Get boundary polygon (if exists in collection)
    /// </summary>
    public Polygon? GetBoundary()
    {
        return CompleteGeometry.Geometries
            .OfType<Polygon>()
            .FirstOrDefault();
    }
    
    /// <summary>
    /// Get atom points (if exist in collection)
    /// </summary>
    public IEnumerable<Point> GetAtomPoints()
    {
        return CompleteGeometry.Geometries
            .OfType<Point>();
    }
    
    /// <summary>
    /// Get sequence linestrings (if exist in collection)
    /// </summary>
    public IEnumerable<LineString> GetSequences()
    {
        return CompleteGeometry.Geometries
            .OfType<LineString>();
    }
    
    /// <summary>
    /// Get multi-point collections (if exist)
    /// </summary>
    public IEnumerable<MultiPoint> GetMultiPoints()
    {
        return CompleteGeometry.Geometries
            .OfType<MultiPoint>();
    }
    
    /// <summary>
    /// Rebuild geometry collection with new geometries
    /// </summary>
    public void UpdateGeometry(IEnumerable<Geometry> newGeometries)
    {
        if (newGeometries == null)
        {
            throw new ArgumentNullException(nameof(newGeometries));
        }
        
        var geometryArray = newGeometries.ToArray();
        if (geometryArray.Length == 0)
        {
            throw new ArgumentException("Must provide at least one geometry", nameof(newGeometries));
        }
        
        CompleteGeometry = new GeometryCollection(geometryArray) { SRID = 4326 };
        
        Centroid = CompleteGeometry.Centroid;
        Centroid.SRID = 4326;
        
        BoundingBox = BoundingBox4D.FromGeometry(CompleteGeometry);
        
        AtomCount = geometryArray.Count(g => g is Point);
        UpdatedAt = DateTime.UtcNow;
    }
}
