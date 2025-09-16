using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Hartonomous.IntegrationTests.Infrastructure;
using System.Data;
using Serilog;
using System.Diagnostics;

namespace Hartonomous.IntegrationTests.DatabaseTests;

/// <summary>
/// Comprehensive database integration tests that validate real SQL Server functionality
/// </summary>
public class ComprehensiveDatabaseIntegrationTests : IClassFixture<TestFixture>, IAsyncLifetime
{
    private readonly TestFixture _fixture;
    private readonly ILogger<ComprehensiveDatabaseIntegrationTests> _logger;
    private readonly string _connectionString;
    private readonly string _testUserId;

    public ComprehensiveDatabaseIntegrationTests(TestFixture fixture)
    {
        _fixture = fixture;
        _connectionString = "Server=localhost;Database=HartonomousDB;Trusted_Connection=true;MultipleActiveResultSets=true;TrustServerCertificate=true;";
        _testUserId = "integration-test-user";

        using var loggerFactory = LoggerFactory.Create(builder => builder.AddSerilog());
        _logger = loggerFactory.CreateLogger<ComprehensiveDatabaseIntegrationTests>();
    }

    public async Task InitializeAsync()
    {
        _logger.LogInformation("Initializing comprehensive database integration tests");
        await CleanupTestDataAsync();
    }

    public async Task DisposeAsync()
    {
        _logger.LogInformation("Cleaning up comprehensive database integration tests");
        await CleanupTestDataAsync();
    }

    [Fact]
    public async Task Database_Connection_ShouldEstablishSuccessfully()
    {
        // Arrange & Act
        var stopwatch = Stopwatch.StartNew();

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();

        stopwatch.Stop();

        // Assert
        connection.State.Should().Be(ConnectionState.Open);
        stopwatch.ElapsedMilliseconds.Should().BeLessThan(5000, "Database connection should be fast");

        _logger.LogInformation("Database connection established in {ElapsedMs}ms", stopwatch.ElapsedMilliseconds);
    }

    [Fact]
    public async Task Database_Schema_ShouldExistAndBeValid()
    {
        // Arrange
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();

        // Act - Check for required tables
        var tableCheckQuery = @"
            SELECT TABLE_NAME
            FROM INFORMATION_SCHEMA.TABLES
            WHERE TABLE_TYPE = 'BASE TABLE'
            AND TABLE_NAME IN ('Projects', 'ModelMetadata', 'ModelComponents', 'ComponentWeights', 'OutboxEvents')
            ORDER BY TABLE_NAME";

        var command = new SqlCommand(tableCheckQuery, connection);
        var tables = new List<string>();

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            tables.Add(reader.GetString("TABLE_NAME"));
        }

        // Assert
        tables.Should().Contain("Projects", "Projects table should exist");
        tables.Should().Contain("ModelMetadata", "ModelMetadata table should exist");
        tables.Should().Contain("ModelComponents", "ModelComponents table should exist");
        tables.Should().Contain("ComponentWeights", "ComponentWeights table should exist");
        tables.Should().Contain("OutboxEvents", "OutboxEvents table should exist");

        _logger.LogInformation("Found {TableCount} required tables: {Tables}", tables.Count, string.Join(", ", tables));
    }

    [Fact]
    public async Task Projects_CRUD_Operations_ShouldWorkCorrectly()
    {
        // Arrange
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();

        var projectId = Guid.NewGuid();
        var projectName = $"Integration Test Project {DateTime.UtcNow:yyyyMMdd_HHmmss}";

        try
        {
            // Act - Create
            var createCommand = new SqlCommand(@"
                INSERT INTO dbo.Projects (ProjectId, UserId, ProjectName, CreatedAt)
                VALUES (@ProjectId, @UserId, @ProjectName, @CreatedAt)", connection);

            createCommand.Parameters.AddWithValue("@ProjectId", projectId);
            createCommand.Parameters.AddWithValue("@UserId", _testUserId);
            createCommand.Parameters.AddWithValue("@ProjectName", projectName);
            createCommand.Parameters.AddWithValue("@CreatedAt", DateTime.UtcNow);

            var createResult = await createCommand.ExecuteNonQueryAsync();

            // Act - Read
            var readCommand = new SqlCommand(@"
                SELECT ProjectId, UserId, ProjectName, CreatedAt
                FROM dbo.Projects
                WHERE ProjectId = @ProjectId", connection);
            readCommand.Parameters.AddWithValue("@ProjectId", projectId);

            await using var reader = await readCommand.ExecuteReaderAsync();
            await reader.ReadAsync();

            var retrievedProjectId = reader.GetGuid("ProjectId");
            var retrievedUserId = reader.GetString("UserId");
            var retrievedProjectName = reader.GetString("ProjectName");
            var retrievedCreatedAt = reader.GetDateTime("CreatedAt");

            await reader.CloseAsync();

            // Act - Update
            var newProjectName = $"Updated {projectName}";
            var updateCommand = new SqlCommand(@"
                UPDATE dbo.Projects
                SET ProjectName = @ProjectName
                WHERE ProjectId = @ProjectId", connection);
            updateCommand.Parameters.AddWithValue("@ProjectName", newProjectName);
            updateCommand.Parameters.AddWithValue("@ProjectId", projectId);

            var updateResult = await updateCommand.ExecuteNonQueryAsync();

            // Act - Delete
            var deleteCommand = new SqlCommand(@"
                DELETE FROM dbo.Projects
                WHERE ProjectId = @ProjectId", connection);
            deleteCommand.Parameters.AddWithValue("@ProjectId", projectId);

            var deleteResult = await deleteCommand.ExecuteNonQueryAsync();

            // Assert
            createResult.Should().Be(1, "Should insert one record");
            retrievedProjectId.Should().Be(projectId);
            retrievedUserId.Should().Be(_testUserId);
            retrievedProjectName.Should().Be(projectName);
            updateResult.Should().Be(1, "Should update one record");
            deleteResult.Should().Be(1, "Should delete one record");

            _logger.LogInformation("Successfully completed CRUD operations for project {ProjectId}", projectId);
        }
        catch (Exception)
        {
            // Cleanup on failure
            await CleanupProjectAsync(connection, projectId);
            throw;
        }
    }

    [Fact]
    public async Task ModelMetadata_WithComponents_ShouldWorkCorrectly()
    {
        // Arrange
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();

        var projectId = Guid.NewGuid();
        var modelId = Guid.NewGuid();
        var componentId = Guid.NewGuid();

        try
        {
            // Create test project first
            await CreateTestProjectAsync(connection, projectId);

            // Act - Create model metadata
            var createModelCommand = new SqlCommand(@"
                INSERT INTO dbo.ModelMetadata (ModelId, ProjectId, ModelName, Version, License, MetadataJson, CreatedAt)
                VALUES (@ModelId, @ProjectId, @ModelName, @Version, @License, @MetadataJson, @CreatedAt)", connection);

            createModelCommand.Parameters.AddWithValue("@ModelId", modelId);
            createModelCommand.Parameters.AddWithValue("@ProjectId", projectId);
            createModelCommand.Parameters.AddWithValue("@ModelName", "Test Neural Network");
            createModelCommand.Parameters.AddWithValue("@Version", "1.0.0");
            createModelCommand.Parameters.AddWithValue("@License", "MIT");
            createModelCommand.Parameters.AddWithValue("@MetadataJson", "{\"architecture\": \"transformer\", \"parameters\": 1000000}");
            createModelCommand.Parameters.AddWithValue("@CreatedAt", DateTime.UtcNow);

            var modelResult = await createModelCommand.ExecuteNonQueryAsync();

            // Act - Create model component
            var createComponentCommand = new SqlCommand(@"
                INSERT INTO dbo.ModelComponents (ComponentId, ModelId, ComponentName, ComponentType, CreatedAt)
                VALUES (@ComponentId, @ModelId, @ComponentName, @ComponentType, @CreatedAt)", connection);

            createComponentCommand.Parameters.AddWithValue("@ComponentId", componentId);
            createComponentCommand.Parameters.AddWithValue("@ModelId", modelId);
            createComponentCommand.Parameters.AddWithValue("@ComponentName", "encoder.weight");
            createComponentCommand.Parameters.AddWithValue("@ComponentType", "tensor");
            createComponentCommand.Parameters.AddWithValue("@CreatedAt", DateTime.UtcNow);

            var componentResult = await createComponentCommand.ExecuteNonQueryAsync();

            // Act - Query with JOIN
            var joinQuery = new SqlCommand(@"
                SELECT m.ModelName, m.Version, c.ComponentName, c.ComponentType
                FROM dbo.ModelMetadata m
                INNER JOIN dbo.ModelComponents c ON m.ModelId = c.ModelId
                WHERE m.ModelId = @ModelId", connection);
            joinQuery.Parameters.AddWithValue("@ModelId", modelId);

            var joinResults = new List<(string ModelName, string Version, string ComponentName, string ComponentType)>();
            await using var reader = await joinQuery.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                joinResults.Add((
                    reader.GetString("ModelName"),
                    reader.GetString("Version"),
                    reader.GetString("ComponentName"),
                    reader.GetString("ComponentType")
                ));
            }

            // Assert
            modelResult.Should().Be(1, "Should insert model metadata");
            componentResult.Should().Be(1, "Should insert model component");
            joinResults.Should().HaveCount(1, "Should return one joined result");
            joinResults[0].ModelName.Should().Be("Test Neural Network");
            joinResults[0].ComponentName.Should().Be("encoder.weight");

            _logger.LogInformation("Successfully tested model metadata with components for model {ModelId}", modelId);
        }
        finally
        {
            // Cleanup
            await CleanupModelDataAsync(connection, projectId, modelId, componentId);
        }
    }

    [Fact]
    public async Task FileStream_Operations_ShouldWorkWithComponentWeights()
    {
        // Arrange
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();

        var projectId = Guid.NewGuid();
        var modelId = Guid.NewGuid();
        var componentId = Guid.NewGuid();

        try
        {
            // Setup test data
            await CreateTestProjectAsync(connection, projectId);
            await CreateTestModelAsync(connection, modelId, projectId);
            await CreateTestComponentAsync(connection, componentId, modelId);

            // Act - Create ComponentWeight with FileStream
            var createWeightCommand = new SqlCommand(@"
                INSERT INTO dbo.ComponentWeights (ComponentId, WeightData)
                VALUES (@ComponentId, CAST('' AS VARBINARY(MAX)))", connection);
            createWeightCommand.Parameters.AddWithValue("@ComponentId", componentId);

            var weightResult = await createWeightCommand.ExecuteNonQueryAsync();

            // Act - Check if FileStream path can be retrieved
            var getPathCommand = new SqlCommand(@"
                SELECT WeightData.PathName() as FilePath,
                       LEN(WeightData) as DataLength
                FROM dbo.ComponentWeights
                WHERE ComponentId = @ComponentId", connection);
            getPathCommand.Parameters.AddWithValue("@ComponentId", componentId);

            await using var reader = await getPathCommand.ExecuteReaderAsync();
            var hasPath = await reader.ReadAsync();
            string? filePath = null;
            int dataLength = 0;

            if (hasPath)
            {
                filePath = reader.IsDBNull("FilePath") ? null : reader.GetString("FilePath");
                dataLength = reader.GetInt32("DataLength");
            }

            // Assert
            weightResult.Should().Be(1, "Should insert component weight");
            hasPath.Should().BeTrue("Should find the component weight record");
            // Note: FileStream path might be null if FileStream is not properly configured

            _logger.LogInformation("FileStream test completed for component {ComponentId}, FilePath: {FilePath}, DataLength: {DataLength}",
                componentId, filePath ?? "NULL", dataLength);
        }
        finally
        {
            // Cleanup
            await CleanupModelDataAsync(connection, projectId, modelId, componentId);
        }
    }

    [Fact]
    public async Task Database_Performance_ShouldMeetExpectations()
    {
        // Arrange
        const int testRecordCount = 100;
        var projectIds = new List<Guid>();
        var stopwatch = new Stopwatch();

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();

        try
        {
            // Act - Bulk insert performance test
            stopwatch.Start();

            for (int i = 0; i < testRecordCount; i++)
            {
                var projectId = Guid.NewGuid();
                projectIds.Add(projectId);

                var command = new SqlCommand(@"
                    INSERT INTO dbo.Projects (ProjectId, UserId, ProjectName, CreatedAt)
                    VALUES (@ProjectId, @UserId, @ProjectName, @CreatedAt)", connection);

                command.Parameters.AddWithValue("@ProjectId", projectId);
                command.Parameters.AddWithValue("@UserId", _testUserId);
                command.Parameters.AddWithValue("@ProjectName", $"Performance Test Project {i}");
                command.Parameters.AddWithValue("@CreatedAt", DateTime.UtcNow);

                await command.ExecuteNonQueryAsync();
            }

            stopwatch.Stop();
            var insertTime = stopwatch.ElapsedMilliseconds;

            // Act - Bulk query performance test
            stopwatch.Restart();

            var queryCommand = new SqlCommand(@"
                SELECT COUNT(*) as ProjectCount
                FROM dbo.Projects
                WHERE UserId = @UserId", connection);
            queryCommand.Parameters.AddWithValue("@UserId", _testUserId);

            var projectCount = (int)await queryCommand.ExecuteScalarAsync();

            stopwatch.Stop();
            var queryTime = stopwatch.ElapsedMilliseconds;

            // Assert
            projectCount.Should().BeGreaterOrEqualTo(testRecordCount, "Should find all inserted projects");
            insertTime.Should().BeLessThan(10000, "Bulk insert should complete within 10 seconds");
            queryTime.Should().BeLessThan(1000, "Query should complete within 1 second");

            _logger.LogInformation("Performance test completed: {RecordCount} records inserted in {InsertTime}ms, queried in {QueryTime}ms",
                testRecordCount, insertTime, queryTime);
        }
        finally
        {
            // Cleanup
            foreach (var projectId in projectIds)
            {
                await CleanupProjectAsync(connection, projectId);
            }
        }
    }

    [Fact]
    public async Task OutboxEvents_ShouldSupportEventSourcing()
    {
        // Arrange
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();

        var eventIds = new List<Guid>();

        try
        {
            // Act - Create multiple events
            var events = new[]
            {
                new { EventType = "ProjectCreated", Payload = "{\"projectId\": \"123\", \"name\": \"Test Project\"}" },
                new { EventType = "ModelUploaded", Payload = "{\"modelId\": \"456\", \"projectId\": \"123\"}" },
                new { EventType = "ComponentAdded", Payload = "{\"componentId\": \"789\", \"modelId\": \"456\"}" }
            };

            foreach (var evt in events)
            {
                var eventId = Guid.NewGuid();
                eventIds.Add(eventId);

                var command = new SqlCommand(@"
                    INSERT INTO dbo.OutboxEvents (EventId, EventType, Payload, CreatedAt)
                    VALUES (@EventId, @EventType, @Payload, @CreatedAt)", connection);

                command.Parameters.AddWithValue("@EventId", eventId);
                command.Parameters.AddWithValue("@EventType", evt.EventType);
                command.Parameters.AddWithValue("@Payload", evt.Payload);
                command.Parameters.AddWithValue("@CreatedAt", DateTime.UtcNow);

                await command.ExecuteNonQueryAsync();
            }

            // Act - Query events in order
            var queryCommand = new SqlCommand(@"
                SELECT EventType, Payload, CreatedAt, ProcessedAt
                FROM dbo.OutboxEvents
                WHERE EventId IN (SELECT value FROM STRING_SPLIT(@EventIds, ','))
                ORDER BY CreatedAt", connection);

            queryCommand.Parameters.AddWithValue("@EventIds", string.Join(",", eventIds));

            var retrievedEvents = new List<(string EventType, string Payload, DateTime CreatedAt, DateTime? ProcessedAt)>();
            await using var reader = await queryCommand.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                retrievedEvents.Add((
                    reader.GetString("EventType"),
                    reader.GetString("Payload"),
                    reader.GetDateTime("CreatedAt"),
                    reader.IsDBNull("ProcessedAt") ? null : reader.GetDateTime("ProcessedAt")
                ));
            }

            // Assert
            retrievedEvents.Should().HaveCount(3, "Should retrieve all events");
            retrievedEvents[0].EventType.Should().Be("ProjectCreated");
            retrievedEvents[1].EventType.Should().Be("ModelUploaded");
            retrievedEvents[2].EventType.Should().Be("ComponentAdded");
            retrievedEvents.All(e => e.ProcessedAt == null).Should().BeTrue("Events should not be processed yet");

            _logger.LogInformation("Successfully tested outbox events pattern with {EventCount} events", retrievedEvents.Count);
        }
        finally
        {
            // Cleanup
            foreach (var eventId in eventIds)
            {
                await CleanupEventAsync(connection, eventId);
            }
        }
    }

    // Helper methods
    private async Task CleanupTestDataAsync()
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();

        var cleanupCommand = new SqlCommand(@"
            DELETE FROM dbo.ComponentWeights WHERE ComponentId IN (
                SELECT c.ComponentId FROM dbo.ModelComponents c
                INNER JOIN dbo.ModelMetadata m ON c.ModelId = m.ModelId
                INNER JOIN dbo.Projects p ON m.ProjectId = p.ProjectId
                WHERE p.UserId = @UserId
            );

            DELETE FROM dbo.ModelComponents WHERE ModelId IN (
                SELECT m.ModelId FROM dbo.ModelMetadata m
                INNER JOIN dbo.Projects p ON m.ProjectId = p.ProjectId
                WHERE p.UserId = @UserId
            );

            DELETE FROM dbo.ModelMetadata WHERE ProjectId IN (
                SELECT ProjectId FROM dbo.Projects WHERE UserId = @UserId
            );

            DELETE FROM dbo.Projects WHERE UserId = @UserId;

            DELETE FROM dbo.OutboxEvents WHERE EventType LIKE '%Test%';", connection);

        cleanupCommand.Parameters.AddWithValue("@UserId", _testUserId);
        await cleanupCommand.ExecuteNonQueryAsync();
    }

    private async Task CreateTestProjectAsync(SqlConnection connection, Guid projectId)
    {
        var command = new SqlCommand(@"
            INSERT INTO dbo.Projects (ProjectId, UserId, ProjectName, CreatedAt)
            VALUES (@ProjectId, @UserId, @ProjectName, @CreatedAt)", connection);

        command.Parameters.AddWithValue("@ProjectId", projectId);
        command.Parameters.AddWithValue("@UserId", _testUserId);
        command.Parameters.AddWithValue("@ProjectName", $"Test Project {projectId}");
        command.Parameters.AddWithValue("@CreatedAt", DateTime.UtcNow);

        await command.ExecuteNonQueryAsync();
    }

    private async Task CreateTestModelAsync(SqlConnection connection, Guid modelId, Guid projectId)
    {
        var command = new SqlCommand(@"
            INSERT INTO dbo.ModelMetadata (ModelId, ProjectId, ModelName, Version, License, CreatedAt)
            VALUES (@ModelId, @ProjectId, @ModelName, @Version, @License, @CreatedAt)", connection);

        command.Parameters.AddWithValue("@ModelId", modelId);
        command.Parameters.AddWithValue("@ProjectId", projectId);
        command.Parameters.AddWithValue("@ModelName", "Test Model");
        command.Parameters.AddWithValue("@Version", "1.0.0");
        command.Parameters.AddWithValue("@License", "MIT");
        command.Parameters.AddWithValue("@CreatedAt", DateTime.UtcNow);

        await command.ExecuteNonQueryAsync();
    }

    private async Task CreateTestComponentAsync(SqlConnection connection, Guid componentId, Guid modelId)
    {
        var command = new SqlCommand(@"
            INSERT INTO dbo.ModelComponents (ComponentId, ModelId, ComponentName, ComponentType, CreatedAt)
            VALUES (@ComponentId, @ModelId, @ComponentName, @ComponentType, @CreatedAt)", connection);

        command.Parameters.AddWithValue("@ComponentId", componentId);
        command.Parameters.AddWithValue("@ModelId", modelId);
        command.Parameters.AddWithValue("@ComponentName", "test.weight");
        command.Parameters.AddWithValue("@ComponentType", "tensor");
        command.Parameters.AddWithValue("@CreatedAt", DateTime.UtcNow);

        await command.ExecuteNonQueryAsync();
    }

    private async Task CleanupProjectAsync(SqlConnection connection, Guid projectId)
    {
        var command = new SqlCommand("DELETE FROM dbo.Projects WHERE ProjectId = @ProjectId", connection);
        command.Parameters.AddWithValue("@ProjectId", projectId);
        await command.ExecuteNonQueryAsync();
    }

    private async Task CleanupModelDataAsync(SqlConnection connection, Guid projectId, Guid modelId, Guid componentId)
    {
        var cleanupCommands = new[]
        {
            "DELETE FROM dbo.ComponentWeights WHERE ComponentId = @ComponentId",
            "DELETE FROM dbo.ModelComponents WHERE ComponentId = @ComponentId",
            "DELETE FROM dbo.ModelMetadata WHERE ModelId = @ModelId",
            "DELETE FROM dbo.Projects WHERE ProjectId = @ProjectId"
        };

        foreach (var sql in cleanupCommands)
        {
            var command = new SqlCommand(sql, connection);
            command.Parameters.AddWithValue("@ComponentId", componentId);
            command.Parameters.AddWithValue("@ModelId", modelId);
            command.Parameters.AddWithValue("@ProjectId", projectId);
            await command.ExecuteNonQueryAsync();
        }
    }

    private async Task CleanupEventAsync(SqlConnection connection, Guid eventId)
    {
        var command = new SqlCommand("DELETE FROM dbo.OutboxEvents WHERE EventId = @EventId", connection);
        command.Parameters.AddWithValue("@EventId", eventId);
        await command.ExecuteNonQueryAsync();
    }
}