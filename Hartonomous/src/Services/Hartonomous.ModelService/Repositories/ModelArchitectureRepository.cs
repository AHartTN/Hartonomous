/*
 * Copyright (c) 2024-2025 Hartonomous AI Agent Factory Platform. All Rights Reserved.
 *
 * This software is proprietary and confidential. Unauthorized copying, distribution,
 * modification, or use of this software, in whole or in part, is strictly prohibited.
 *
 * This file contains the model architecture repository for Model Query Engine (MQE) operations.
 * Features deep model structure analysis, layer configuration management, and architectural pattern recognition.
 */

using Dapper;
using Hartonomous.ModelQuery.DTOs;
using Hartonomous.ModelQuery.Interfaces;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using System.Text.Json;

namespace Hartonomous.ModelQuery.Repositories;

public class ModelArchitectureRepository : IModelArchitectureRepository
{
    private readonly string _connectionString;

    public ModelArchitectureRepository(IConfiguration configuration)
    {
        _connectionString = configuration["ConnectionStrings:DefaultConnection"]
            ?? throw new InvalidOperationException("DefaultConnection string not found");
    }

    public async Task<ModelArchitectureDto?> GetModelArchitectureAsync(Guid modelId, string userId)
    {
        const string architectureSql = @"
            SELECT ma.ModelId, ma.ArchitectureName, ma.Framework, ma.Configuration, ma.Hyperparameters, ma.CreatedAt
            FROM dbo.ModelArchitectures ma
            INNER JOIN dbo.ModelMetadata m ON ma.ModelId = m.ModelId
            INNER JOIN dbo.Projects p ON m.ProjectId = p.ProjectId
            WHERE ma.ModelId = @ModelId AND p.UserId = @UserId";

        using var connection = new SqlConnection(_connectionString);
        var architecture = await connection.QuerySingleOrDefaultAsync(architectureSql, new { ModelId = modelId, UserId = userId });

        if (architecture == null) return null;

        var layers = await GetModelLayersAsync(modelId, userId);

        return new ModelArchitectureDto(
            architecture.ModelId,
            architecture.ArchitectureName,
            architecture.Framework,
            layers.ToList(),
            JsonSerializer.Deserialize<Dictionary<string, object>>(architecture.Configuration) ?? new Dictionary<string, object>(),
            JsonSerializer.Deserialize<Dictionary<string, object>>(architecture.Hyperparameters) ?? new Dictionary<string, object>(),
            architecture.CreatedAt
        );
    }

    public async Task<IEnumerable<ModelLayerDto>> GetModelLayersAsync(Guid modelId, string userId)
    {
        const string layersSql = @"
            SELECT ml.LayerId, ml.ModelId, ml.LayerName, ml.LayerType, ml.LayerIndex, ml.Configuration, ml.CreatedAt
            FROM dbo.ModelLayers ml
            INNER JOIN dbo.ModelMetadata m ON ml.ModelId = m.ModelId
            INNER JOIN dbo.Projects p ON m.ProjectId = p.ProjectId
            WHERE ml.ModelId = @ModelId AND p.UserId = @UserId
            ORDER BY ml.LayerIndex";

        const string weightsSql = @"
            SELECT w.WeightId, w.ModelId, w.LayerName, w.WeightName, w.DataType,
                   w.Shape, w.SizeBytes, w.StoragePath, w.ChecksumSha256, w.CreatedAt
            FROM dbo.ModelWeights w
            INNER JOIN dbo.ModelMetadata m ON w.ModelId = m.ModelId
            INNER JOIN dbo.Projects p ON m.ProjectId = p.ProjectId
            WHERE w.ModelId = @ModelId AND p.UserId = @UserId";

        using var connection = new SqlConnection(_connectionString);
        var layers = await connection.QueryAsync(layersSql, new { ModelId = modelId, UserId = userId });
        var weights = await connection.QueryAsync(weightsSql, new { ModelId = modelId, UserId = userId });

        var weightsByLayer = weights.GroupBy(w => w.LayerName).ToDictionary(g => g.Key, g => g.ToList());

        return layers.Select(l => {
            var configuration = JsonSerializer.Deserialize<Dictionary<string, object>>(l.Configuration) ?? new Dictionary<string, object>();
            var layerWeights = new List<ModelWeightDto>();
            if (weightsByLayer.ContainsKey(l.LayerName))
            {
                foreach (var w in weightsByLayer[l.LayerName])
                {
                    layerWeights.Add(new ModelWeightDto(
                        w.WeightId,
                        w.ModelId,
                        w.LayerName,
                        w.WeightName,
                        w.DataType,
                        JsonSerializer.Deserialize<int[]>(w.Shape) ?? Array.Empty<int>(),
                        w.SizeBytes,
                        w.StoragePath,
                        w.ChecksumSha256,
                        w.CreatedAt
                    ));
                }
            }

            return new ModelLayerDto(
                l.LayerId,
                l.ModelId,
                l.LayerName,
                l.LayerType,
                l.LayerIndex,
                configuration,
                layerWeights,
                l.CreatedAt
            );
        });
    }

    public async Task<ModelLayerDto?> GetLayerByIdAsync(Guid layerId, string userId)
    {
        const string sql = @"
            SELECT ml.LayerId, ml.ModelId, ml.LayerName, ml.LayerType, ml.LayerIndex, ml.Configuration, ml.CreatedAt
            FROM dbo.ModelLayers ml
            INNER JOIN dbo.ModelMetadata m ON ml.ModelId = m.ModelId
            INNER JOIN dbo.Projects p ON m.ProjectId = p.ProjectId
            WHERE ml.LayerId = @LayerId AND p.UserId = @UserId";

        using var connection = new SqlConnection(_connectionString);
        var layer = await connection.QuerySingleOrDefaultAsync(sql, new { LayerId = layerId, UserId = userId });

        if (layer == null) return null;

        // Get weights for this layer
        const string weightsSql = @"
            SELECT w.WeightId, w.ModelId, w.LayerName, w.WeightName, w.DataType,
                   w.Shape, w.SizeBytes, w.StoragePath, w.ChecksumSha256, w.CreatedAt
            FROM dbo.ModelWeights w
            INNER JOIN dbo.ModelMetadata m ON w.ModelId = m.ModelId
            INNER JOIN dbo.Projects p ON m.ProjectId = p.ProjectId
            WHERE w.ModelId = @ModelId AND w.LayerName = @LayerName AND p.UserId = @UserId";

        var weights = await connection.QueryAsync(weightsSql, new { ModelId = layer.ModelId, LayerName = layer.LayerName, UserId = userId });

        return new ModelLayerDto(
            layer.LayerId,
            layer.ModelId,
            layer.LayerName,
            layer.LayerType,
            layer.LayerIndex,
            JsonSerializer.Deserialize<Dictionary<string, object>>(layer.Configuration) ?? new Dictionary<string, object>(),
            weights.Select(w => new ModelWeightDto(
                w.WeightId,
                w.ModelId,
                w.LayerName,
                w.WeightName,
                w.DataType,
                JsonSerializer.Deserialize<int[]>(w.Shape) ?? Array.Empty<int>(),
                w.SizeBytes,
                w.StoragePath,
                w.ChecksumSha256,
                w.CreatedAt
            )).ToList(),
            layer.CreatedAt
        );
    }

    public async Task<Guid> CreateLayerAsync(Guid modelId, string layerName, string layerType, int layerIndex, Dictionary<string, object> configuration, string userId)
    {
        const string sql = @"
            INSERT INTO dbo.ModelLayers (LayerId, ModelId, LayerName, LayerType, LayerIndex, Configuration, CreatedAt)
            SELECT @LayerId, @ModelId, @LayerName, @LayerType, @LayerIndex, @Configuration, @CreatedAt
            WHERE EXISTS (
                SELECT 1 FROM dbo.ModelMetadata m
                INNER JOIN dbo.Projects p ON m.ProjectId = p.ProjectId
                WHERE m.ModelId = @ModelId AND p.UserId = @UserId
            )";

        var layerId = Guid.NewGuid();
        using var connection = new SqlConnection(_connectionString);

        var rowsAffected = await connection.ExecuteAsync(sql, new
        {
            LayerId = layerId,
            ModelId = modelId,
            LayerName = layerName,
            LayerType = layerType,
            LayerIndex = layerIndex,
            Configuration = JsonSerializer.Serialize(configuration),
            CreatedAt = DateTime.UtcNow,
            UserId = userId
        });

        if (rowsAffected == 0)
        {
            throw new UnauthorizedAccessException("Model not found or access denied");
        }

        return layerId;
    }

    public async Task<bool> UpdateLayerConfigurationAsync(Guid layerId, Dictionary<string, object> configuration, string userId)
    {
        const string sql = @"
            UPDATE dbo.ModelLayers
            SET Configuration = @Configuration, UpdatedAt = @UpdatedAt
            WHERE LayerId = @LayerId
            AND EXISTS (
                SELECT 1 FROM dbo.ModelMetadata m
                INNER JOIN dbo.Projects p ON m.ProjectId = p.ProjectId
                WHERE m.ModelId = ModelLayers.ModelId AND p.UserId = @UserId
            )";

        using var connection = new SqlConnection(_connectionString);
        var rowsAffected = await connection.ExecuteAsync(sql, new
        {
            LayerId = layerId,
            Configuration = JsonSerializer.Serialize(configuration),
            UpdatedAt = DateTime.UtcNow,
            UserId = userId
        });

        return rowsAffected > 0;
    }

    public async Task<bool> DeleteLayerAsync(Guid layerId, string userId)
    {
        const string sql = @"
            DELETE FROM dbo.ModelLayers
            WHERE LayerId = @LayerId
            AND EXISTS (
                SELECT 1 FROM dbo.ModelMetadata m
                INNER JOIN dbo.Projects p ON m.ProjectId = p.ProjectId
                WHERE m.ModelId = ModelLayers.ModelId AND p.UserId = @UserId
            )";

        using var connection = new SqlConnection(_connectionString);
        var rowsAffected = await connection.ExecuteAsync(sql, new { LayerId = layerId, UserId = userId });
        return rowsAffected > 0;
    }

    public async Task<Guid> CreateArchitectureAsync(Guid modelId, string architectureName, string framework, Dictionary<string, object> configuration, Dictionary<string, object> hyperparameters, string userId)
    {
        const string sql = @"
            INSERT INTO dbo.ModelArchitectures (ModelId, ArchitectureName, Framework, Configuration, Hyperparameters, CreatedAt)
            SELECT @ModelId, @ArchitectureName, @Framework, @Configuration, @Hyperparameters, @CreatedAt
            WHERE EXISTS (
                SELECT 1 FROM dbo.ModelMetadata m
                INNER JOIN dbo.Projects p ON m.ProjectId = p.ProjectId
                WHERE m.ModelId = @ModelId AND p.UserId = @UserId
            )";

        using var connection = new SqlConnection(_connectionString);

        var rowsAffected = await connection.ExecuteAsync(sql, new
        {
            ModelId = modelId,
            ArchitectureName = architectureName,
            Framework = framework,
            Configuration = JsonSerializer.Serialize(configuration),
            Hyperparameters = JsonSerializer.Serialize(hyperparameters),
            CreatedAt = DateTime.UtcNow,
            UserId = userId
        });

        if (rowsAffected == 0)
        {
            throw new UnauthorizedAccessException("Model not found or access denied");
        }

        return modelId;
    }

    public async Task<bool> UpdateArchitectureAsync(Guid modelId, string architectureName, string framework, Dictionary<string, object> configuration, Dictionary<string, object> hyperparameters, string userId)
    {
        const string sql = @"
            UPDATE dbo.ModelArchitectures
            SET ArchitectureName = @ArchitectureName, Framework = @Framework,
                Configuration = @Configuration, Hyperparameters = @Hyperparameters, UpdatedAt = @UpdatedAt
            WHERE ModelId = @ModelId
            AND EXISTS (
                SELECT 1 FROM dbo.ModelMetadata m
                INNER JOIN dbo.Projects p ON m.ProjectId = p.ProjectId
                WHERE m.ModelId = @ModelId AND p.UserId = @UserId
            )";

        using var connection = new SqlConnection(_connectionString);
        var rowsAffected = await connection.ExecuteAsync(sql, new
        {
            ModelId = modelId,
            ArchitectureName = architectureName,
            Framework = framework,
            Configuration = JsonSerializer.Serialize(configuration),
            Hyperparameters = JsonSerializer.Serialize(hyperparameters),
            UpdatedAt = DateTime.UtcNow,
            UserId = userId
        });

        return rowsAffected > 0;
    }
}