/*
 * Copyright (c) 2024-2025 Hartonomous AI Agent Factory Platform. All Rights Reserved.
 *
 * This software is proprietary and confidential. Unauthorized copying, distribution,
 * modification, or use of this software, in whole or in part, is strictly prohibited.
 *
 * This file contains the model weight repository for Model Query Engine (MQE) weight management.
 * Features SQL Server FILESTREAM integration, weight data streaming, and checksum validation.
 */

using Dapper;
using Hartonomous.ModelQuery.DTOs;
using Hartonomous.ModelQuery.Interfaces;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using System.Text.Json;

namespace Hartonomous.ModelQuery.Repositories;

public class ModelWeightRepository : IModelWeightRepository
{
    private readonly string _connectionString;
    private readonly string _fileStreamPath;

    public ModelWeightRepository(IConfiguration configuration)
    {
        _connectionString = configuration["ConnectionStrings:DefaultConnection"]
            ?? throw new InvalidOperationException("DefaultConnection string not found");
        _fileStreamPath = configuration["ModelStorage:FileStreamPath"]
            ?? throw new InvalidOperationException("FileStreamPath not found");
    }

    public async Task<IEnumerable<ModelWeightDto>> GetModelWeightsAsync(Guid modelId, string userId)
    {
        const string sql = @"
            SELECT w.WeightId, w.ModelId, w.LayerName, w.WeightName, w.DataType,
                   w.Shape, w.SizeBytes, w.StoragePath, w.ChecksumSha256, w.CreatedAt
            FROM dbo.ModelWeights w
            INNER JOIN dbo.ModelMetadata m ON w.ModelId = m.ModelId
            INNER JOIN dbo.Projects p ON m.ProjectId = p.ProjectId
            WHERE w.ModelId = @ModelId AND p.UserId = @UserId
            ORDER BY w.LayerName, w.WeightName";

        using var connection = new SqlConnection(_connectionString);
        var results = await connection.QueryAsync(sql, new { ModelId = modelId, UserId = userId });

        return results.Select(r => new ModelWeightDto(
            r.WeightId,
            r.ModelId,
            r.LayerName,
            r.WeightName,
            r.DataType,
            JsonSerializer.Deserialize<int[]>(r.Shape) ?? Array.Empty<int>(),
            r.SizeBytes,
            r.StoragePath,
            r.ChecksumSha256,
            r.CreatedAt
        ));
    }

    public async Task<ModelWeightDto?> GetWeightByIdAsync(Guid weightId, string userId)
    {
        const string sql = @"
            SELECT w.WeightId, w.ModelId, w.LayerName, w.WeightName, w.DataType,
                   w.Shape, w.SizeBytes, w.StoragePath, w.ChecksumSha256, w.CreatedAt
            FROM dbo.ModelWeights w
            INNER JOIN dbo.ModelMetadata m ON w.ModelId = m.ModelId
            INNER JOIN dbo.Projects p ON m.ProjectId = p.ProjectId
            WHERE w.WeightId = @WeightId AND p.UserId = @UserId";

        using var connection = new SqlConnection(_connectionString);
        var result = await connection.QuerySingleOrDefaultAsync(sql, new { WeightId = weightId, UserId = userId });

        if (result == null) return null;

        return new ModelWeightDto(
            result.WeightId,
            result.ModelId,
            result.LayerName,
            result.WeightName,
            result.DataType,
            JsonSerializer.Deserialize<int[]>(result.Shape) ?? Array.Empty<int>(),
            result.SizeBytes,
            result.StoragePath,
            result.ChecksumSha256,
            result.CreatedAt
        );
    }

    public async Task<IEnumerable<ModelWeightDto>> GetWeightsByLayerAsync(Guid modelId, string layerName, string userId)
    {
        const string sql = @"
            SELECT w.WeightId, w.ModelId, w.LayerName, w.WeightName, w.DataType,
                   w.Shape, w.SizeBytes, w.StoragePath, w.ChecksumSha256, w.CreatedAt
            FROM dbo.ModelWeights w
            INNER JOIN dbo.ModelMetadata m ON w.ModelId = m.ModelId
            INNER JOIN dbo.Projects p ON m.ProjectId = p.ProjectId
            WHERE w.ModelId = @ModelId AND w.LayerName = @LayerName AND p.UserId = @UserId
            ORDER BY w.WeightName";

        using var connection = new SqlConnection(_connectionString);
        var results = await connection.QueryAsync(sql, new { ModelId = modelId, LayerName = layerName, UserId = userId });

        return results.Select(r => new ModelWeightDto(
            r.WeightId,
            r.ModelId,
            r.LayerName,
            r.WeightName,
            r.DataType,
            JsonSerializer.Deserialize<int[]>(r.Shape) ?? Array.Empty<int>(),
            r.SizeBytes,
            r.StoragePath,
            r.ChecksumSha256,
            r.CreatedAt
        ));
    }

    public async Task<Guid> CreateWeightAsync(Guid modelId, string layerName, string weightName, string dataType, int[] shape, long sizeBytes, string storagePath, string checksumSha256, string userId)
    {
        const string sql = @"
            INSERT INTO dbo.ModelWeights (WeightId, ModelId, LayerName, WeightName, DataType, Shape, SizeBytes, StoragePath, ChecksumSha256, CreatedAt)
            SELECT @WeightId, @ModelId, @LayerName, @WeightName, @DataType, @Shape, @SizeBytes, @StoragePath, @ChecksumSha256, @CreatedAt
            WHERE EXISTS (
                SELECT 1 FROM dbo.ModelMetadata m
                INNER JOIN dbo.Projects p ON m.ProjectId = p.ProjectId
                WHERE m.ModelId = @ModelId AND p.UserId = @UserId
            )";

        var weightId = Guid.NewGuid();
        using var connection = new SqlConnection(_connectionString);

        var rowsAffected = await connection.ExecuteAsync(sql, new
        {
            WeightId = weightId,
            ModelId = modelId,
            LayerName = layerName,
            WeightName = weightName,
            DataType = dataType,
            Shape = JsonSerializer.Serialize(shape),
            SizeBytes = sizeBytes,
            StoragePath = storagePath,
            ChecksumSha256 = checksumSha256,
            CreatedAt = DateTime.UtcNow,
            UserId = userId
        });

        if (rowsAffected == 0)
        {
            throw new UnauthorizedAccessException("Model not found or access denied");
        }

        return weightId;
    }

    public async Task<bool> DeleteWeightAsync(Guid weightId, string userId)
    {
        const string sql = @"
            DELETE FROM dbo.ModelWeights
            WHERE WeightId = @WeightId
            AND EXISTS (
                SELECT 1 FROM dbo.ModelMetadata m
                INNER JOIN dbo.Projects p ON m.ProjectId = p.ProjectId
                WHERE m.ModelId = ModelWeights.ModelId AND p.UserId = @UserId
            )";

        using var connection = new SqlConnection(_connectionString);
        var rowsAffected = await connection.ExecuteAsync(sql, new { WeightId = weightId, UserId = userId });
        return rowsAffected > 0;
    }

    public async Task<bool> UpdateWeightStoragePathAsync(Guid weightId, string newStoragePath, string userId)
    {
        const string sql = @"
            UPDATE dbo.ModelWeights
            SET StoragePath = @NewStoragePath, UpdatedAt = @UpdatedAt
            WHERE WeightId = @WeightId
            AND EXISTS (
                SELECT 1 FROM dbo.ModelMetadata m
                INNER JOIN dbo.Projects p ON m.ProjectId = p.ProjectId
                WHERE m.ModelId = ModelWeights.ModelId AND p.UserId = @UserId
            )";

        using var connection = new SqlConnection(_connectionString);
        var rowsAffected = await connection.ExecuteAsync(sql, new
        {
            WeightId = weightId,
            NewStoragePath = newStoragePath,
            UpdatedAt = DateTime.UtcNow,
            UserId = userId
        });

        return rowsAffected > 0;
    }

    public async Task<Stream?> GetWeightDataStreamAsync(Guid weightId, string userId)
    {
        var weight = await GetWeightByIdAsync(weightId, userId);
        if (weight == null) return null;

        var filePath = Path.Combine(_fileStreamPath, weight.StoragePath);
        if (!File.Exists(filePath)) return null;

        return new FileStream(filePath, FileMode.Open, FileAccess.Read);
    }

    public async Task<bool> StoreWeightDataAsync(Guid weightId, Stream dataStream, string userId)
    {
        var weight = await GetWeightByIdAsync(weightId, userId);
        if (weight == null) return false;

        var filePath = Path.Combine(_fileStreamPath, weight.StoragePath);
        var directory = Path.GetDirectoryName(filePath);

        if (directory != null && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write);
        await dataStream.CopyToAsync(fileStream);

        return true;
    }
}