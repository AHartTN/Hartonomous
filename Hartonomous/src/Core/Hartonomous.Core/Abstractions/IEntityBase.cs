/*
 * Copyright (c) 2024-2025 Hartonomous AI Agent Factory Platform. All Rights Reserved.
 *
 * This software is proprietary and confidential. Unauthorized copying, distribution,
 * modification, or use of this software, in whole or in part, is strictly prohibited.
 *
 * This file contains the base entity interface for repository pattern consistency.
 * Provides common properties for all domain entities with proper multi-tenant isolation.
 */

namespace Hartonomous.Core.Abstractions;

/// <summary>
/// Base interface for all entities with standardized properties
/// </summary>
/// <typeparam name="TKey">Primary key type</typeparam>
public interface IEntityBase<TKey> where TKey : IEquatable<TKey>
{
    TKey Id { get; set; }
    string UserId { get; set; }
    DateTime CreatedDate { get; set; }
    DateTime? ModifiedDate { get; set; }
}

/// <summary>
/// Base interface for entities with Guid primary keys
/// </summary>
public interface IEntityBase : IEntityBase<Guid>
{
}