using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace Hartonomous.Infrastructure.Milvus;

/// <summary>
/// SQL Server 2025 native vector service
/// Implements NinaDB vector capabilities using SQL Server's native VECTOR data type
/// Replaces external Milvus dependency with integrated SQL Server solution
/// </summary>
public class SqlServerVectorService : IDisposable
{
    private readonly string _connectionString;
    private readonly ILogger<SqlServerVectorService> _logger;
    private SqlConnection? _connection;

    public SqlServerVectorService(IConfiguration configuration, ILogger<SqlServerVectorService> logger)
    {
        _logger = logger;
        _connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new ArgumentException("DefaultConnection string required for SQL Server vector operations");

        InitializeConnection();
    }

    private void InitializeConnection()
    {
        try
        {
            _connection = new SqlConnection(_connectionString);
            _connection.Open();
            _logger.LogInformation("SQL Server vector service initialized using native VECTOR capabilities");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize SQL Server vector service");
            throw;
        }
    }

    /// <summary>
    /// Ensures the vector storage tables exist
    /// Creates ComponentEmbeddings table with VECTOR column if not exists
    /// </summary>
    public async Task EnsureVectorTablesExistAsync()
    {
        try
        {
            var createTableSql = @"
                IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'ComponentEmbeddings')
                BEGIN
                    CREATE TABLE dbo.ComponentEmbeddings (
                        EmbeddingId UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
                        ComponentId UNIQUEIDENTIFIER NOT NULL,
                        ModelId UNIQUEIDENTIFIER NOT NULL,
                        UserId NVARCHAR(128) NOT NULL,
                        ComponentType NVARCHAR(100) NOT NULL,
                        Description NVARCHAR(MAX),
                        EmbeddingVector VECTOR(1536) NOT NULL, -- OpenAI embedding dimensions
                        CreatedAt DATETIME2 DEFAULT GETUTCDATE(),

                        INDEX IX_ComponentEmbeddings_ComponentId (ComponentId),
                        INDEX IX_ComponentEmbeddings_ModelId_UserId (ModelId, UserId),
                        INDEX IX_ComponentEmbeddings_ComponentType (ComponentType)
                    );

                    -- Create vector index for similarity search
                    CREATE INDEX IX_ComponentEmbeddings_Vector
                    ON dbo.ComponentEmbeddings(EmbeddingVector)
                    USING VECTOR;
                END";

            using var command = new SqlCommand(createTableSql, _connection);
            await command.ExecuteNonQueryAsync();

            _logger.LogDebug("Vector tables initialized successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to ensure vector tables exist");
            throw;
        }
    }

    /// <summary>
    /// Insert component embedding vector using SQL Server 2025 native VECTOR type
    /// </summary>
    public async Task InsertEmbeddingAsync(Guid componentId, Guid modelId, string userId,
        float[] embedding, string componentType, string description)
    {
        try
        {
            _logger.LogDebug("Inserting embedding for component {ComponentId} with {Dimensions} dimensions",
                componentId, embedding.Length);

            // Convert float array to VECTOR format - SQL Server expects JSON array format
            var vectorJson = JsonSerializer.Serialize(embedding);

            var insertSql = @"
                MERGE dbo.ComponentEmbeddings AS target
                USING (SELECT @ComponentId AS ComponentId) AS source
                ON target.ComponentId = source.ComponentId AND target.UserId = @UserId
                WHEN MATCHED THEN
                    UPDATE SET
                        EmbeddingVector = CAST(@VectorJson AS VECTOR(1536)),
                        ComponentType = @ComponentType,
                        Description = @Description,
                        CreatedAt = GETUTCDATE()
                WHEN NOT MATCHED THEN
                    INSERT (ComponentId, ModelId, UserId, ComponentType, Description, EmbeddingVector)
                    VALUES (@ComponentId, @ModelId, @UserId, @ComponentType, @Description,
                            CAST(@VectorJson AS VECTOR(1536)));";

            using var command = new SqlCommand(insertSql, _connection);
            command.Parameters.AddWithValue("@ComponentId", componentId);
            command.Parameters.AddWithValue("@ModelId", modelId);
            command.Parameters.AddWithValue("@UserId", userId);
            command.Parameters.AddWithValue("@ComponentType", componentType);
            command.Parameters.AddWithValue("@Description", description ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@VectorJson", vectorJson);

            var rowsAffected = await command.ExecuteNonQueryAsync();
            _logger.LogDebug("Embedded component {ComponentId}: {RowsAffected} rows affected", componentId, rowsAffected);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to insert embedding for component {ComponentId}", componentId);
            throw;
        }
    }

    /// <summary>
    /// Search for similar component embeddings using SQL Server native vector similarity
    /// Uses VECTOR_DISTANCE function for cosine similarity
    /// </summary>
    public async Task<IEnumerable<SimilarComponent>> SearchSimilarAsync(float[] queryEmbedding, string userId,
        int topK = 10, string? componentType = null)
    {
        try
        {
            _logger.LogDebug("Performing vector similarity search for user {UserId} with topK={TopK}", userId, topK);

            var queryVectorJson = JsonSerializer.Serialize(queryEmbedding);

            var searchSql = @"
                SELECT TOP (@TopK)
                    ce.ComponentId,
                    ce.ModelId,
                    ce.ComponentType,
                    ce.Description,
                    VECTOR_DISTANCE('cosine', ce.EmbeddingVector, CAST(@QueryVector AS VECTOR(1536))) AS Distance,
                    (1 - VECTOR_DISTANCE('cosine', ce.EmbeddingVector, CAST(@QueryVector AS VECTOR(1536)))) AS SimilarityScore
                FROM dbo.ComponentEmbeddings ce
                WHERE ce.UserId = @UserId
                " + (componentType != null ? "AND ce.ComponentType = @ComponentType" : "") + @"
                ORDER BY VECTOR_DISTANCE('cosine', ce.EmbeddingVector, CAST(@QueryVector AS VECTOR(1536))) ASC";

            using var command = new SqlCommand(searchSql, _connection);
            command.Parameters.AddWithValue("@TopK", topK);
            command.Parameters.AddWithValue("@QueryVector", queryVectorJson);
            command.Parameters.AddWithValue("@UserId", userId);

            if (componentType != null)
                command.Parameters.AddWithValue("@ComponentType", componentType);

            var results = new List<SimilarComponent>();

            using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                results.Add(new SimilarComponent
                {
                    ComponentId = reader.GetGuid("ComponentId"),
                    ModelId = reader.GetGuid("ModelId"),
                    ComponentType = reader.GetString("ComponentType"),
                    Description = reader.IsDBNull("Description") ? null : reader.GetString("Description"),
                    Distance = reader.GetDouble("Distance"),
                    SimilarityScore = reader.GetDouble("SimilarityScore")
                });
            }

            _logger.LogDebug("Vector search returned {ResultCount} similar components", results.Count);
            return results;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to perform vector similarity search");
            throw;
        }
    }

    /// <summary>
    /// Delete component embedding
    /// </summary>
    public async Task DeleteEmbeddingAsync(Guid componentId, string userId)
    {
        try
        {
            var deleteSql = @"
                DELETE FROM dbo.ComponentEmbeddings
                WHERE ComponentId = @ComponentId AND UserId = @UserId";

            using var command = new SqlCommand(deleteSql, _connection);
            command.Parameters.AddWithValue("@ComponentId", componentId);
            command.Parameters.AddWithValue("@UserId", userId);

            var rowsAffected = await command.ExecuteNonQueryAsync();
            _logger.LogDebug("Deleted embedding for component {ComponentId}: {RowsAffected} rows affected",
                componentId, rowsAffected);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete embedding for component {ComponentId}", componentId);
            throw;
        }
    }

    /// <summary>
    /// Get collection statistics
    /// </summary>
    public async Task<VectorCollectionStats> GetCollectionStatsAsync(string userId)
    {
        try
        {
            var statsSql = @"
                SELECT
                    COUNT(*) AS TotalEmbeddings,
                    COUNT(DISTINCT ModelId) AS UniqueModels,
                    COUNT(DISTINCT ComponentType) AS UniqueComponentTypes,
                    AVG(CAST(LEN(CAST(EmbeddingVector AS NVARCHAR(MAX))) AS FLOAT)) AS AvgVectorSize
                FROM dbo.ComponentEmbeddings
                WHERE UserId = @UserId";

            using var command = new SqlCommand(statsSql, _connection);
            command.Parameters.AddWithValue("@UserId", userId);

            using var reader = await command.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                return new VectorCollectionStats
                {
                    TotalEmbeddings = reader.GetInt32("TotalEmbeddings"),
                    UniqueModels = reader.GetInt32("UniqueModels"),
                    UniqueComponentTypes = reader.GetInt32("UniqueComponentTypes"),
                    AvgVectorSize = reader.IsDBNull("AvgVectorSize") ? 0 : reader.GetDouble("AvgVectorSize")
                };
            }

            return new VectorCollectionStats();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get collection stats");
            throw;
        }
    }

    /// <summary>
    /// Batch insert embeddings for better performance
    /// </summary>
    public async Task BatchInsertEmbeddingsAsync(IEnumerable<ComponentEmbedding> embeddings, string userId)
    {
        try
        {
            var embeddingsList = embeddings.ToList();
            _logger.LogDebug("Batch inserting {Count} embeddings for user {UserId}", embeddingsList.Count, userId);

            using var transaction = _connection!.BeginTransaction();

            foreach (var embedding in embeddingsList)
            {
                await InsertEmbeddingAsync(embedding.ComponentId, embedding.ModelId, userId,
                    embedding.Vector, embedding.ComponentType, embedding.Description);
            }

            transaction.Commit();
            _logger.LogDebug("Successfully batch inserted {Count} embeddings", embeddingsList.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to batch insert embeddings");
            throw;
        }
    }

    public void Dispose()
    {
        _connection?.Close();
        _connection?.Dispose();
    }
}

/// <summary>
/// Component embedding for batch operations
/// </summary>
public class ComponentEmbedding
{
    public Guid ComponentId { get; set; }
    public Guid ModelId { get; set; }
    public float[] Vector { get; set; } = Array.Empty<float>();
    public string ComponentType { get; set; } = string.Empty;
    public string? Description { get; set; }
}

/// <summary>
/// Vector collection statistics
/// </summary>
public class VectorCollectionStats
{
    public int TotalEmbeddings { get; set; }
    public int UniqueModels { get; set; }
    public int UniqueComponentTypes { get; set; }
    public double AvgVectorSize { get; set; }
}

/// <summary>
/// Similar component result from vector search
/// </summary>
public class SimilarComponent
{
    public Guid ComponentId { get; set; }
    public Guid ModelId { get; set; }
    public string ComponentType { get; set; } = string.Empty;
    public string? Description { get; set; }
    public double Distance { get; set; }
    public double SimilarityScore { get; set; }
}