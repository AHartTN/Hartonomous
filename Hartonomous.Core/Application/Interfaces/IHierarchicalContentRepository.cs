using Hartonomous.Core.Domain.Entities;
using NetTopologySuite.Geometries;

namespace Hartonomous.Core.Application.Interfaces;

/// <summary>
/// Repository interface for HierarchicalContent entity with tree traversal queries
/// </summary>
public interface IHierarchicalContentRepository : IRepository<HierarchicalContent>
{
    /// <summary>
    /// Get root-level content for an ingestion
    /// </summary>
    Task<List<HierarchicalContent>> GetRootContentAsync(
        Guid contentIngestionId,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Get all content at specific hierarchy level
    /// </summary>
    Task<List<HierarchicalContent>> GetByHierarchyLevelAsync(
        Guid contentIngestionId,
        int hierarchyLevel,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Get direct children of a parent content
    /// </summary>
    Task<List<HierarchicalContent>> GetChildrenAsync(
        Guid parentId,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Get all descendants of a parent content (recursive)
    /// </summary>
    Task<List<HierarchicalContent>> GetDescendantsAsync(
        Guid parentId,
        int? maxDepth = null,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Get ancestor chain from content to root
    /// </summary>
    Task<List<HierarchicalContent>> GetAncestorsAsync(
        Guid contentId,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Get siblings (same parent) of a content
    /// </summary>
    Task<List<HierarchicalContent>> GetSiblingsAsync(
        Guid contentId,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Get content by label (e.g., all "chapter" nodes)
    /// </summary>
    Task<List<HierarchicalContent>> GetByLabelAsync(
        string label,
        Guid? contentIngestionId = null,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Search content by title
    /// </summary>
    Task<List<HierarchicalContent>> SearchByTitleAsync(
        string titlePattern,
        Guid? contentIngestionId = null,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Find content containing a specific point
    /// </summary>
    Task<List<HierarchicalContent>> FindContainingPointAsync(
        Point location,
        Guid? contentIngestionId = null,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Find content intersecting with a geometry
    /// </summary>
    Task<List<HierarchicalContent>> FindIntersectingAsync(
        Geometry geometry,
        Guid? contentIngestionId = null,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Get content by byte offset range
    /// </summary>
    Task<HierarchicalContent?> GetByOffsetAsync(
        Guid contentIngestionId,
        long byteOffset,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Get complete tree structure for an ingestion
    /// </summary>
    Task<HierarchicalContentTree> GetTreeStructureAsync(
        Guid contentIngestionId,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Get statistics for hierarchical content
    /// </summary>
    Task<HierarchyStatistics> GetStatisticsAsync(
        Guid contentIngestionId,
        CancellationToken cancellationToken = default);
}
