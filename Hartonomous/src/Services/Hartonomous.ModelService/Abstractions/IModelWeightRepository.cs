/*
 * Copyright (c) 2024-2025 Hartonomous AI Agent Factory Platform. All Rights Reserved.
 *
 * This software is proprietary and confidential. Unauthorized copying, distribution,
 * modification, or use of this software, in whole or in part, is strictly prohibited.
 *
 * This file contains the model weight repository interface for Model Query Engine (MQE).
 * Features weight data management, FILESTREAM operations, and secure weight streaming with checksums.
 */

using Hartonomous.ModelQuery.DTOs;

namespace Hartonomous.ModelQuery.Interfaces;

public interface IModelWeightRepository
{
    Task<IEnumerable<ModelWeightDto>> GetModelWeightsAsync(Guid modelId, string userId);
    Task<ModelWeightDto?> GetWeightByIdAsync(Guid weightId, string userId);
    Task<IEnumerable<ModelWeightDto>> GetWeightsByLayerAsync(Guid modelId, string layerName, string userId);
    Task<Guid> CreateWeightAsync(Guid modelId, string layerName, string weightName, string dataType, int[] shape, long sizeBytes, string storagePath, string checksumSha256, string userId);
    Task<bool> DeleteWeightAsync(Guid weightId, string userId);
    Task<bool> UpdateWeightStoragePathAsync(Guid weightId, string newStoragePath, string userId);
    Task<Stream?> GetWeightDataStreamAsync(Guid weightId, string userId);
    Task<bool> StoreWeightDataAsync(Guid weightId, Stream dataStream, string userId);
}