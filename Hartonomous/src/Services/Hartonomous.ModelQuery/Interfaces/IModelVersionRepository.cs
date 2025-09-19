/*
 * Copyright (c) 2024-2025 Hartonomous AI Agent Factory Platform. All Rights Reserved.
 *
 * This software is proprietary and confidential. Unauthorized copying, distribution,
 * modification, or use of this software, in whole or in part, is strictly prohibited.
 *
 * This file contains the model version repository interface for Model Query Engine (MQE).
 * Features model versioning, change detection, comparison analytics, and version lineage tracking.
 */

using Hartonomous.ModelQuery.DTOs;

namespace Hartonomous.ModelQuery.Interfaces;

public interface IModelVersionRepository
{
    Task<IEnumerable<ModelVersionDto>> GetModelVersionsAsync(Guid modelId, string userId);
    Task<ModelVersionDto?> GetVersionByIdAsync(Guid versionId, string userId);
    Task<ModelVersionDto?> GetLatestVersionAsync(Guid modelId, string userId);
    Task<Guid> CreateVersionAsync(Guid modelId, string version, string description, Dictionary<string, object> changes, string? parentVersion, string userId);
    Task<bool> DeleteVersionAsync(Guid versionId, string userId);
    Task<ModelComparisonDto?> CompareVersionsAsync(Guid versionAId, Guid versionBId, string userId);
}