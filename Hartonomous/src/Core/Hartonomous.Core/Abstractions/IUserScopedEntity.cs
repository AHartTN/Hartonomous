namespace Hartonomous.Core.Abstractions;

/// <summary>
/// Interface for entities that are scoped to a specific user
/// Provides multi-tenant isolation at the data access level
/// </summary>
public interface IUserScopedEntity
{
    /// <summary>
    /// User identifier for multi-tenant data isolation
    /// All operations on this entity are automatically scoped to this user
    /// </summary>
    string UserId { get; set; }

    /// <summary>
    /// Unique identifier for the entity
    /// </summary>
    Guid Id { get; set; }

    /// <summary>
    /// Entity creation timestamp
    /// </summary>
    DateTime CreatedAt { get; set; }

    /// <summary>
    /// Entity last update timestamp
    /// </summary>
    DateTime UpdatedAt { get; set; }
}