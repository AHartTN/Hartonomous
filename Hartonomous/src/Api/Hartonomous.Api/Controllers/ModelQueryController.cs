/*
 * Copyright (c) 2024-2025 Hartonomous AI Agent Factory Platform. All Rights Reserved.
 *
 * This software is proprietary and confidential. Unauthorized copying, distribution,
 * modification, or use of this software, in whole or in part, is strictly prohibited.
 *
 * This file contains the Model Query Controller - API endpoints for the revolutionary
 * Model Query Engine that enables T-SQL queries against large language models.
 * The neural mapping and memory-mapped file access patterns represent proprietary
 * intellectual property and trade secrets.
 */

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Hartonomous.Infrastructure.Security;
using System.Data;

namespace Hartonomous.Api.Controllers;

/// <summary>
/// API controller for Model Query Engine operations
/// Exposes SQL CLR functions for LLM model file querying
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ModelQueryController : ControllerBase
{
    private readonly string _connectionString;
    private readonly ILogger<ModelQueryController> _logger;

    public ModelQueryController(IConfiguration configuration, ILogger<ModelQueryController> logger)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new ArgumentException("DefaultConnection is required");
        _logger = logger;
    }

    /// <summary>
    /// Query specific byte range from a model component using memory-mapped file access
    /// Core functionality for the "Neural Map" concept
    /// </summary>
    [HttpGet("components/{componentId}/bytes")]
    public async Task<ActionResult<ModelBytesResult>> QueryModelBytes(
        Guid componentId, [FromQuery] long offset, [FromQuery] int length)
    {
        try
        {
            var userId = User.GetUserId();

            if (offset < 0 || length <= 0 || length > 1024 * 1024) // Max 1MB per request
            {
                return BadRequest("Invalid offset or length parameters");
            }

            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            // First verify user has access to this component
            if (!await VerifyComponentAccessAsync(connection, componentId, userId))
            {
                return NotFound("Component not found or access denied");
            }

            // Query bytes using SQL CLR function
            using var command = new SqlCommand("SELECT dbo.QueryModelBytes(@ComponentId, @Offset, @Length)", connection);
            command.Parameters.AddWithValue("@ComponentId", componentId);
            command.Parameters.AddWithValue("@Offset", offset);
            command.Parameters.AddWithValue("@Length", length);

            var result = await command.ExecuteScalarAsync();
            if (result == DBNull.Value || result == null)
            {
                return NotFound("No data found at specified offset");
            }

            var bytes = (byte[])result;
            return Ok(new ModelBytesResult
            {
                ComponentId = componentId,
                Offset = offset,
                Length = bytes.Length,
                Data = Convert.ToBase64String(bytes),
                Timestamp = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to query model bytes for component: {ComponentId}", componentId);
            return StatusCode(500, "Failed to query model bytes");
        }
    }

    /// <summary>
    /// Search for patterns within model weights using neural pattern recognition
    /// </summary>
    [HttpPost("components/{componentId}/search-pattern")]
    public async Task<ActionResult<PatternSearchResult>> SearchPattern(
        Guid componentId, [FromBody] PatternSearchRequest request)
    {
        try
        {
            var userId = User.GetUserId();

            if (request.Pattern == null || request.Pattern.Length == 0)
            {
                return BadRequest("Pattern data is required");
            }

            if (request.Tolerance < 0 || request.Tolerance > 1)
            {
                return BadRequest("Tolerance must be between 0 and 1");
            }

            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            // Verify user access
            if (!await VerifyComponentAccessAsync(connection, componentId, userId))
            {
                return NotFound("Component not found or access denied");
            }

            // Search for pattern using SQL CLR function
            using var command = new SqlCommand("SELECT dbo.FindPatternInWeights(@ComponentId, @Pattern, @Tolerance)", connection);
            command.Parameters.AddWithValue("@ComponentId", componentId);
            command.Parameters.AddWithValue("@Pattern", request.Pattern);
            command.Parameters.AddWithValue("@Tolerance", request.Tolerance);

            var result = await command.ExecuteScalarAsync();
            bool patternFound = result != DBNull.Value && (bool)result;

            return Ok(new PatternSearchResult
            {
                ComponentId = componentId,
                PatternFound = patternFound,
                PatternSize = request.Pattern.Length,
                Tolerance = request.Tolerance,
                SearchTime = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to search pattern in component: {ComponentId}", componentId);
            return StatusCode(500, "Failed to search pattern");
        }
    }

    /// <summary>
    /// Get comprehensive statistics for a model component
    /// Supports model analysis and debugging
    /// </summary>
    [HttpGet("components/{componentId}/stats")]
    public async Task<ActionResult<ModelStatsResult>> GetModelStats(Guid componentId)
    {
        try
        {
            var userId = User.GetUserId();

            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            // Verify user access
            if (!await VerifyComponentAccessAsync(connection, componentId, userId))
            {
                return NotFound("Component not found or access denied");
            }

            // Get model statistics using SQL CLR function
            using var command = new SqlCommand("SELECT dbo.GetModelStats(@ComponentId)", connection);
            command.Parameters.AddWithValue("@ComponentId", componentId);

            var result = await command.ExecuteScalarAsync();
            if (result == DBNull.Value || result == null)
            {
                return NotFound("No statistics available for this component");
            }

            return Ok(new ModelStatsResult
            {
                ComponentId = componentId,
                Statistics = result.ToString()!,
                GeneratedAt = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get model statistics for component: {ComponentId}", componentId);
            return StatusCode(500, "Failed to retrieve model statistics");
        }
    }

    /// <summary>
    /// List all queryable components for the authenticated user
    /// </summary>
    [HttpGet("components")]
    public async Task<ActionResult<IEnumerable<QueryableComponent>>> GetQueryableComponents()
    {
        try
        {
            var userId = User.GetUserId();

            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            const string sql = @"
                SELECT c.ComponentId, c.ComponentName, c.ComponentType, m.ModelName, p.ProjectName,
                       DATALENGTH(w.WeightData) as SizeBytes
                FROM dbo.ModelComponents c
                INNER JOIN dbo.ModelMetadata m ON c.ModelId = m.ModelId
                INNER JOIN dbo.Projects p ON m.ProjectId = p.ProjectId
                LEFT JOIN dbo.ComponentWeights w ON c.ComponentId = w.ComponentId
                WHERE p.UserId = @UserId
                ORDER BY p.ProjectName, m.ModelName, c.ComponentName";

            using var command = new SqlCommand(sql, connection);
            command.Parameters.AddWithValue("@UserId", userId);

            var components = new List<QueryableComponent>();
            using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                components.Add(new QueryableComponent
                {
                    ComponentId = reader.GetGuid("ComponentId"),
                    ComponentName = reader.GetString("ComponentName"),
                    ComponentType = reader.GetString("ComponentType"),
                    ModelName = reader.GetString("ModelName"),
                    ProjectName = reader.GetString("ProjectName"),
                    SizeBytes = reader.IsDBNull("SizeBytes") ? 0 : reader.GetInt64("SizeBytes")
                });
            }

            return Ok(components);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get queryable components for user");
            return StatusCode(500, "Failed to retrieve queryable components");
        }
    }

    /// <summary>
    /// Verify user has access to the specified component
    /// </summary>
    private async Task<bool> VerifyComponentAccessAsync(SqlConnection connection, Guid componentId, string userId)
    {
        const string sql = @"
            SELECT COUNT(1)
            FROM dbo.ModelComponents c
            INNER JOIN dbo.ModelMetadata m ON c.ModelId = m.ModelId
            INNER JOIN dbo.Projects p ON m.ProjectId = p.ProjectId
            WHERE c.ComponentId = @ComponentId AND p.UserId = @UserId";

        using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@ComponentId", componentId);
        command.Parameters.AddWithValue("@UserId", userId);

        var count = (int)(await command.ExecuteScalarAsync() ?? 0);
        return count > 0;
    }
}

/// <summary>
/// Result of model bytes query
/// </summary>
public class ModelBytesResult
{
    public Guid ComponentId { get; set; }
    public long Offset { get; set; }
    public int Length { get; set; }
    public string Data { get; set; } = string.Empty; // Base64 encoded
    public DateTime Timestamp { get; set; }
}

/// <summary>
/// Request for pattern search
/// </summary>
public class PatternSearchRequest
{
    public byte[] Pattern { get; set; } = Array.Empty<byte>();
    public double Tolerance { get; set; } = 0.1;
}

/// <summary>
/// Result of pattern search
/// </summary>
public class PatternSearchResult
{
    public Guid ComponentId { get; set; }
    public bool PatternFound { get; set; }
    public int PatternSize { get; set; }
    public double Tolerance { get; set; }
    public DateTime SearchTime { get; set; }
}

/// <summary>
/// Result of model statistics query
/// </summary>
public class ModelStatsResult
{
    public Guid ComponentId { get; set; }
    public string Statistics { get; set; } = string.Empty;
    public DateTime GeneratedAt { get; set; }
}

/// <summary>
/// Information about a queryable model component
/// </summary>
public class QueryableComponent
{
    public Guid ComponentId { get; set; }
    public string ComponentName { get; set; } = string.Empty;
    public string ComponentType { get; set; } = string.Empty;
    public string ModelName { get; set; } = string.Empty;
    public string ProjectName { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
}