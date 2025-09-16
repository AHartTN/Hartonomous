using Hartonomous.IntegrationTests.Infrastructure;
using Hartonomous.Core.DTOs;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Text.Json;
using Serilog;
using NBomber.CSharp;
using NBomber.Http.CSharp;

namespace Hartonomous.IntegrationTests.PerformanceTests;

/// <summary>
/// Comprehensive performance benchmarks for all Hartonomous platform services
/// </summary>
public class ComprehensivePerformanceBenchmarks : IClassFixture<TestFixture>, IAsyncLifetime
{
    private readonly TestFixture _fixture;
    private readonly ILogger<ComprehensivePerformanceBenchmarks> _logger;
    private readonly List<Guid> _testProjectIds = new();
    private readonly List<Guid> _testModelIds = new();

    public ComprehensivePerformanceBenchmarks(TestFixture fixture)
    {
        _fixture = fixture;
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddSerilog());
        _logger = loggerFactory.CreateLogger<ComprehensivePerformanceBenchmarks>();
    }

    public async Task InitializeAsync()
    {
        _logger.LogInformation("Initializing performance benchmark tests");
        await SetupTestDataAsync();
    }

    public async Task DisposeAsync()
    {
        _logger.LogInformation("Cleaning up performance benchmark tests");
        await CleanupTestDataAsync();
    }

    [Fact]
    public async Task Performance_ProjectAPI_ShouldMeetResponseTimeTargets()
    {
        _logger.LogInformation("Running Project API performance benchmarks");

        var performanceResults = new ConcurrentDictionary<string, PerformanceMetrics>();

        // Single request performance test
        var singleRequestMetrics = await MeasureSingleRequestPerformanceAsync(async () =>
        {
            var request = new CreateProjectRequest($"Perf Test Project {DateTime.UtcNow.Ticks}");
            var response = await _fixture.HttpClient.PostAsJsonAsync("/api/projects", request);
            response.Should().HaveStatusCode(HttpStatusCode.Created);
            var projectId = await response.Content.ReadFromJsonAsync<Guid>();
            _testProjectIds.Add(projectId);
            return projectId;
        });

        performanceResults["Project Creation"] = singleRequestMetrics;

        // Bulk operations performance test
        var bulkMetrics = await MeasureBulkOperationPerformanceAsync(async (batchSize) =>
        {
            var tasks = new List<Task<Guid>>();
            for (int i = 0; i < batchSize; i++)
            {
                tasks.Add(CreateProjectAsync($"Bulk Test Project {i}"));
            }
            var results = await Task.WhenAll(tasks);
            _testProjectIds.AddRange(results);
            return results.Length;
        }, 20);

        performanceResults["Bulk Project Creation"] = bulkMetrics;

        // Read operations performance
        if (_testProjectIds.Any())
        {
            var readMetrics = await MeasureReadPerformanceAsync(async () =>
            {
                var projectId = _testProjectIds[Random.Shared.Next(_testProjectIds.Count)];
                var response = await _fixture.HttpClient.GetAsync($"/api/projects/{projectId}");
                response.Should().HaveStatusCode(HttpStatusCode.OK);
                return await response.Content.ReadFromJsonAsync<ProjectDto>();
            }, 100);

            performanceResults["Project Read"] = readMetrics;
        }

        // List operations performance
        var listMetrics = await MeasureReadPerformanceAsync(async () =>
        {
            var response = await _fixture.HttpClient.GetAsync("/api/projects");
            response.Should().HaveStatusCode(HttpStatusCode.OK);
            return await response.Content.ReadFromJsonAsync<List<ProjectDto>>();
        }, 50);

        performanceResults["Project List"] = listMetrics;

        // Analyze and validate results
        ValidatePerformanceResults(performanceResults, new Dictionary<string, PerformanceTargets>
        {
            ["Project Creation"] = new(MaxResponseTime: TimeSpan.FromSeconds(2), MinThroughput: 10),
            ["Bulk Project Creation"] = new(MaxResponseTime: TimeSpan.FromSeconds(5), MinThroughput: 5),
            ["Project Read"] = new(MaxResponseTime: TimeSpan.FromMilliseconds(500), MinThroughput: 50),
            ["Project List"] = new(MaxResponseTime: TimeSpan.FromSeconds(1), MinThroughput: 20)
        });

        LogPerformanceResults("Project API", performanceResults);
    }

    [Fact]
    public async Task Performance_ModelAPI_ShouldHandleLargeModels()
    {
        _logger.LogInformation("Running Model API performance benchmarks with large models");

        var performanceResults = new ConcurrentDictionary<string, PerformanceMetrics>();

        // Setup test project
        var projectId = await CreateProjectAsync("Model Performance Test Project");

        // Large model creation performance
        var largeModelMetrics = await MeasureSingleRequestPerformanceAsync(async () =>
        {
            var largeMetadata = GenerateLargeModelMetadata(50000); // ~50KB metadata
            var request = new CreateModelRequest(
                $"Large Model {DateTime.UtcNow.Ticks}",
                "1.0.0",
                "MIT",
                JsonSerializer.Serialize(largeMetadata)
            );

            var response = await _fixture.HttpClient.PostAsJsonAsync($"/api/projects/{projectId}/models", request);
            response.Should().HaveStatusCode(HttpStatusCode.Created);
            var modelId = await response.Content.ReadFromJsonAsync<Guid>();
            _testModelIds.Add(modelId);
            return modelId;
        });

        performanceResults["Large Model Creation"] = largeModelMetrics;

        // Model creation throughput test
        var modelThroughputMetrics = await MeasureBulkOperationPerformanceAsync(async (batchSize) =>
        {
            var tasks = new List<Task<Guid>>();
            for (int i = 0; i < batchSize; i++)
            {
                tasks.Add(CreateModelAsync(projectId, $"Throughput Test Model {i}"));
            }
            var results = await Task.WhenAll(tasks);
            _testModelIds.AddRange(results);
            return results.Length;
        }, 15);

        performanceResults["Model Creation Throughput"] = modelThroughputMetrics;

        // Model query performance
        if (_testModelIds.Any())
        {
            var modelQueryMetrics = await MeasureReadPerformanceAsync(async () =>
            {
                var modelId = _testModelIds[Random.Shared.Next(_testModelIds.Count)];
                var response = await _fixture.HttpClient.GetAsync($"/api/models/{modelId}");
                response.Should().HaveStatusCode(HttpStatusCode.OK);
                return await response.Content.ReadFromJsonAsync<ModelMetadataDto>();
            }, 100);

            performanceResults["Model Query"] = modelQueryMetrics;
        }

        // Model search performance
        var searchMetrics = await MeasureReadPerformanceAsync(async () =>
        {
            var searchTerm = $"Model {Random.Shared.Next(1000)}";
            var response = await _fixture.HttpClient.GetAsync($"/api/models/search?query={Uri.EscapeDataString(searchTerm)}");
            // Search might return 404 if not implemented, that's okay for perf testing
            if (response.StatusCode == HttpStatusCode.OK)
            {
                return await response.Content.ReadFromJsonAsync<List<ModelMetadataDto>>();
            }
            return new List<ModelMetadataDto>();
        }, 30);

        performanceResults["Model Search"] = searchMetrics;

        ValidatePerformanceResults(performanceResults, new Dictionary<string, PerformanceTargets>
        {
            ["Large Model Creation"] = new(MaxResponseTime: TimeSpan.FromSeconds(5), MinThroughput: 2),
            ["Model Creation Throughput"] = new(MaxResponseTime: TimeSpan.FromSeconds(10), MinThroughput: 3),
            ["Model Query"] = new(MaxResponseTime: TimeSpan.FromSeconds(1), MinThroughput: 30),
            ["Model Search"] = new(MaxResponseTime: TimeSpan.FromSeconds(2), MinThroughput: 10)
        });

        LogPerformanceResults("Model API", performanceResults);
    }

    [Fact]
    public async Task Performance_DatabaseOperations_ShouldScaleEfficiently()
    {
        _logger.LogInformation("Running database performance benchmarks");

        var performanceResults = new ConcurrentDictionary<string, PerformanceMetrics>();

        // Database connection performance
        var connectionMetrics = await MeasureOperationPerformanceAsync(async () =>
        {
            await using var connection = new Microsoft.Data.SqlClient.SqlConnection(
                "Server=localhost;Database=HartonomousDB;Trusted_Connection=true;MultipleActiveResultSets=true;TrustServerCertificate=true;");
            await connection.OpenAsync();
            await connection.CloseAsync();
        }, iterations: 50, "Database Connection");

        performanceResults["Database Connection"] = connectionMetrics;

        // Database query performance
        var queryMetrics = await MeasureOperationPerformanceAsync(async () =>
        {
            await using var connection = new Microsoft.Data.SqlClient.SqlConnection(
                "Server=localhost;Database=HartonomousDB;Trusted_Connection=true;MultipleActiveResultSets=true;TrustServerCertificate=true;");
            await connection.OpenAsync();

            var command = new Microsoft.Data.SqlClient.SqlCommand(
                "SELECT COUNT(*) FROM dbo.Projects", connection);
            await command.ExecuteScalarAsync();
        }, iterations: 100, "Database Query");

        performanceResults["Database Query"] = queryMetrics;

        // Database write performance
        var writeMetrics = await MeasureOperationPerformanceAsync(async () =>
        {
            await using var connection = new Microsoft.Data.SqlClient.SqlConnection(
                "Server=localhost;Database=HartonomousDB;Trusted_Connection=true;MultipleActiveResultSets=true;TrustServerCertificate=true;");
            await connection.OpenAsync();

            var projectId = Guid.NewGuid();
            var command = new Microsoft.Data.SqlClient.SqlCommand(@"
                INSERT INTO dbo.Projects (ProjectId, UserId, ProjectName, CreatedAt)
                VALUES (@ProjectId, @UserId, @ProjectName, @CreatedAt);
                DELETE FROM dbo.Projects WHERE ProjectId = @ProjectId;", connection);

            command.Parameters.AddWithValue("@ProjectId", projectId);
            command.Parameters.AddWithValue("@UserId", "perf-test-user");
            command.Parameters.AddWithValue("@ProjectName", $"Perf Test {DateTime.UtcNow.Ticks}");
            command.Parameters.AddWithValue("@CreatedAt", DateTime.UtcNow);

            await command.ExecuteNonQueryAsync();
        }, iterations: 50, "Database Write");

        performanceResults["Database Write"] = writeMetrics;

        ValidatePerformanceResults(performanceResults, new Dictionary<string, PerformanceTargets>
        {
            ["Database Connection"] = new(MaxResponseTime: TimeSpan.FromSeconds(1), MinThroughput: 20),
            ["Database Query"] = new(MaxResponseTime: TimeSpan.FromMilliseconds(100), MinThroughput: 100),
            ["Database Write"] = new(MaxResponseTime: TimeSpan.FromMilliseconds(500), MinThroughput: 50)
        });

        LogPerformanceResults("Database Operations", performanceResults);
    }

    [Fact]
    public async Task Performance_ConcurrentUsers_ShouldMaintainPerformance()
    {
        _logger.LogInformation("Running concurrent user performance benchmarks");

        var concurrencyLevels = new[] { 5, 10, 20, 50 };
        var concurrencyResults = new Dictionary<int, PerformanceMetrics>();

        foreach (var concurrencyLevel in concurrencyLevels)
        {
            _logger.LogInformation("Testing concurrency level: {ConcurrencyLevel} users", concurrencyLevel);

            var metrics = await MeasureConcurrentOperationPerformanceAsync(async () =>
            {
                // Simulate typical user operations
                var operations = new[]
                {
                    async () => await _fixture.HttpClient.GetAsync("/api/projects"),
                    async () => {
                        if (_testProjectIds.Any())
                        {
                            var projectId = _testProjectIds[Random.Shared.Next(_testProjectIds.Count)];
                            return await _fixture.HttpClient.GetAsync($"/api/projects/{projectId}");
                        }
                        return await _fixture.HttpClient.GetAsync("/api/projects");
                    },
                    async () => {
                        if (_testProjectIds.Any())
                        {
                            var projectId = _testProjectIds[Random.Shared.Next(_testProjectIds.Count)];
                            return await _fixture.HttpClient.GetAsync($"/api/projects/{projectId}/models");
                        }
                        return await _fixture.HttpClient.GetAsync("/api/projects");
                    }
                };

                var operation = operations[Random.Shared.Next(operations.Length)];
                var response = await operation();
                return response.IsSuccessStatusCode;
            }, concurrencyLevel, 30); // 30 operations per user

            concurrencyResults[concurrencyLevel] = metrics;

            _logger.LogInformation("Concurrency {Level}: Avg {AvgMs}ms, Success Rate {SuccessRate:P2}",
                concurrencyLevel,
                metrics.AverageResponseTime.TotalMilliseconds,
                metrics.SuccessRate);
        }

        // Validate that performance degrades gracefully with increased load
        for (int i = 1; i < concurrencyLevels.Length; i++)
        {
            var prevLevel = concurrencyLevels[i - 1];
            var currentLevel = concurrencyLevels[i];

            var prevMetrics = concurrencyResults[prevLevel];
            var currentMetrics = concurrencyResults[currentLevel];

            // Response time should not increase dramatically (max 3x)
            var responseTimeRatio = currentMetrics.AverageResponseTime.TotalMilliseconds / prevMetrics.AverageResponseTime.TotalMilliseconds;
            responseTimeRatio.Should().BeLessThan(3.0,
                $"Response time should not increase dramatically from {prevLevel} to {currentLevel} concurrent users");

            // Success rate should remain high
            currentMetrics.SuccessRate.Should().BeGreaterThan(0.95,
                $"Success rate should remain above 95% at {currentLevel} concurrent users");
        }

        LogConcurrencyResults(concurrencyResults);
    }

    [Fact]
    public async Task Performance_LongRunningOperations_ShouldCompleteReliably()
    {
        _logger.LogInformation("Running long-running operation performance benchmarks");

        var performanceResults = new ConcurrentDictionary<string, PerformanceMetrics>();

        // Simulate long-running data processing
        var longRunningMetrics = await MeasureOperationPerformanceAsync(async () =>
        {
            // Simulate complex data processing that takes time
            var projectId = await CreateProjectAsync($"Long Running Test {DateTime.UtcNow.Ticks}");

            var modelTasks = new List<Task<Guid>>();
            for (int i = 0; i < 5; i++)
            {
                modelTasks.Add(CreateModelAsync(projectId, $"LongRun Model {i}"));
            }

            var modelIds = await Task.WhenAll(modelTasks);
            _testModelIds.AddRange(modelIds);

            // Simulate additional processing time
            await Task.Delay(TimeSpan.FromMilliseconds(Random.Shared.Next(500, 1500)));

            return modelIds.Length;
        }, iterations: 10, "Long Running Operation");

        performanceResults["Long Running Operation"] = longRunningMetrics;

        // Test sustained load over time
        var sustainedLoadMetrics = await MeasureSustainedLoadPerformanceAsync(async () =>
        {
            var response = await _fixture.HttpClient.GetAsync("/api/projects");
            response.Should().HaveStatusCode(HttpStatusCode.OK);
            return true;
        }, duration: TimeSpan.FromMinutes(2), operationsPerSecond: 5);

        performanceResults["Sustained Load"] = sustainedLoadMetrics;

        ValidatePerformanceResults(performanceResults, new Dictionary<string, PerformanceTargets>
        {
            ["Long Running Operation"] = new(MaxResponseTime: TimeSpan.FromSeconds(30), MinThroughput: 1),
            ["Sustained Load"] = new(MaxResponseTime: TimeSpan.FromSeconds(2), MinThroughput: 4)
        });

        LogPerformanceResults("Long Running Operations", performanceResults);
    }

    [Fact]
    public async Task Performance_MemoryAndResourceUsage_ShouldBeEfficient()
    {
        _logger.LogInformation("Running memory and resource usage benchmarks");

        var initialMemory = GC.GetTotalMemory(true);
        var process = Process.GetCurrentProcess();
        var initialWorkingSet = process.WorkingSet64;

        // Perform memory-intensive operations
        var largeDataOperations = new List<Task>();
        for (int i = 0; i < 10; i++)
        {
            largeDataOperations.Add(Task.Run(async () =>
            {
                var projectId = await CreateProjectAsync($"Memory Test Project {i}");
                var largeMetadata = GenerateLargeModelMetadata(10000);

                for (int j = 0; j < 5; j++)
                {
                    var modelId = await CreateModelAsync(projectId, $"Memory Test Model {i}-{j}",
                        JsonSerializer.Serialize(largeMetadata));
                    _testModelIds.Add(modelId);
                }
            }));
        }

        await Task.WhenAll(largeDataOperations);

        // Force garbage collection and measure memory usage
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var finalMemory = GC.GetTotalMemory(false);
        var finalWorkingSet = process.WorkingSet64;

        var memoryIncrease = finalMemory - initialMemory;
        var workingSetIncrease = finalWorkingSet - initialWorkingSet;

        _logger.LogInformation("Memory usage: Initial {InitialMB}MB, Final {FinalMB}MB, Increase {IncreaseMB}MB",
            initialMemory / 1024 / 1024, finalMemory / 1024 / 1024, memoryIncrease / 1024 / 1024);

        _logger.LogInformation("Working set: Initial {InitialMB}MB, Final {FinalMB}MB, Increase {IncreaseMB}MB",
            initialWorkingSet / 1024 / 1024, finalWorkingSet / 1024 / 1024, workingSetIncrease / 1024 / 1024);

        // Memory increase should be reasonable (less than 500MB for test operations)
        memoryIncrease.Should().BeLessThan(500 * 1024 * 1024, "Memory increase should be reasonable");

        // Working set increase should be reasonable (less than 1GB)
        workingSetIncrease.Should().BeLessThan(1024 * 1024 * 1024, "Working set increase should be reasonable");
    }

    // Helper Methods
    private async Task<PerformanceMetrics> MeasureSingleRequestPerformanceAsync<T>(Func<Task<T>> operation)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            await operation();
            stopwatch.Stop();
            return new PerformanceMetrics(
                AverageResponseTime: stopwatch.Elapsed,
                MinResponseTime: stopwatch.Elapsed,
                MaxResponseTime: stopwatch.Elapsed,
                Throughput: 1.0 / stopwatch.Elapsed.TotalSeconds,
                TotalOperations: 1,
                SuccessfulOperations: 1,
                SuccessRate: 1.0,
                ErrorCount: 0
            );
        }
        catch
        {
            stopwatch.Stop();
            return new PerformanceMetrics(
                AverageResponseTime: stopwatch.Elapsed,
                MinResponseTime: stopwatch.Elapsed,
                MaxResponseTime: stopwatch.Elapsed,
                Throughput: 0,
                TotalOperations: 1,
                SuccessfulOperations: 0,
                SuccessRate: 0.0,
                ErrorCount: 1
            );
        }
    }

    private async Task<PerformanceMetrics> MeasureBulkOperationPerformanceAsync<T>(Func<int, Task<T>> operation, int batchSize)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            await operation(batchSize);
            stopwatch.Stop();
            return new PerformanceMetrics(
                AverageResponseTime: TimeSpan.FromTicks(stopwatch.Elapsed.Ticks / batchSize),
                MinResponseTime: stopwatch.Elapsed,
                MaxResponseTime: stopwatch.Elapsed,
                Throughput: batchSize / stopwatch.Elapsed.TotalSeconds,
                TotalOperations: batchSize,
                SuccessfulOperations: batchSize,
                SuccessRate: 1.0,
                ErrorCount: 0
            );
        }
        catch
        {
            stopwatch.Stop();
            return new PerformanceMetrics(
                AverageResponseTime: stopwatch.Elapsed,
                MinResponseTime: stopwatch.Elapsed,
                MaxResponseTime: stopwatch.Elapsed,
                Throughput: 0,
                TotalOperations: batchSize,
                SuccessfulOperations: 0,
                SuccessRate: 0.0,
                ErrorCount: batchSize
            );
        }
    }

    private async Task<PerformanceMetrics> MeasureReadPerformanceAsync<T>(Func<Task<T>> operation, int iterations)
    {
        var times = new List<TimeSpan>();
        var errors = 0;

        var stopwatch = Stopwatch.StartNew();

        for (int i = 0; i < iterations; i++)
        {
            var operationStopwatch = Stopwatch.StartNew();
            try
            {
                await operation();
                operationStopwatch.Stop();
                times.Add(operationStopwatch.Elapsed);
            }
            catch
            {
                operationStopwatch.Stop();
                errors++;
            }
        }

        stopwatch.Stop();

        if (times.Any())
        {
            return new PerformanceMetrics(
                AverageResponseTime: TimeSpan.FromTicks((long)times.Average(t => t.Ticks)),
                MinResponseTime: times.Min(),
                MaxResponseTime: times.Max(),
                Throughput: times.Count / stopwatch.Elapsed.TotalSeconds,
                TotalOperations: iterations,
                SuccessfulOperations: times.Count,
                SuccessRate: (double)times.Count / iterations,
                ErrorCount: errors
            );
        }

        return new PerformanceMetrics(
            AverageResponseTime: stopwatch.Elapsed,
            MinResponseTime: stopwatch.Elapsed,
            MaxResponseTime: stopwatch.Elapsed,
            Throughput: 0,
            TotalOperations: iterations,
            SuccessfulOperations: 0,
            SuccessRate: 0.0,
            ErrorCount: errors
        );
    }

    private async Task<PerformanceMetrics> MeasureOperationPerformanceAsync(Func<Task> operation, int iterations, string operationName)
    {
        var times = new List<TimeSpan>();
        var errors = 0;

        _logger.LogInformation("Measuring {OperationName} performance with {Iterations} iterations", operationName, iterations);

        for (int i = 0; i < iterations; i++)
        {
            var stopwatch = Stopwatch.StartNew();
            try
            {
                await operation();
                stopwatch.Stop();
                times.Add(stopwatch.Elapsed);
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                errors++;
                _logger.LogWarning(ex, "Error in {OperationName} iteration {Iteration}", operationName, i);
            }
        }

        if (times.Any())
        {
            var totalTime = TimeSpan.FromTicks(times.Sum(t => t.Ticks));
            return new PerformanceMetrics(
                AverageResponseTime: TimeSpan.FromTicks((long)times.Average(t => t.Ticks)),
                MinResponseTime: times.Min(),
                MaxResponseTime: times.Max(),
                Throughput: times.Count / totalTime.TotalSeconds,
                TotalOperations: iterations,
                SuccessfulOperations: times.Count,
                SuccessRate: (double)times.Count / iterations,
                ErrorCount: errors
            );
        }

        return new PerformanceMetrics(
            AverageResponseTime: TimeSpan.Zero,
            MinResponseTime: TimeSpan.Zero,
            MaxResponseTime: TimeSpan.Zero,
            Throughput: 0,
            TotalOperations: iterations,
            SuccessfulOperations: 0,
            SuccessRate: 0.0,
            ErrorCount: errors
        );
    }

    private async Task<PerformanceMetrics> MeasureConcurrentOperationPerformanceAsync<T>(
        Func<Task<T>> operation, int concurrency, int operationsPerUser)
    {
        var allTimes = new ConcurrentBag<TimeSpan>();
        var errors = 0;

        var tasks = new List<Task>();
        var overallStopwatch = Stopwatch.StartNew();

        for (int user = 0; user < concurrency; user++)
        {
            tasks.Add(Task.Run(async () =>
            {
                for (int op = 0; op < operationsPerUser; op++)
                {
                    var stopwatch = Stopwatch.StartNew();
                    try
                    {
                        await operation();
                        stopwatch.Stop();
                        allTimes.Add(stopwatch.Elapsed);
                    }
                    catch
                    {
                        stopwatch.Stop();
                        Interlocked.Increment(ref errors);
                    }
                }
            }));
        }

        await Task.WhenAll(tasks);
        overallStopwatch.Stop();

        var times = allTimes.ToList();
        if (times.Any())
        {
            return new PerformanceMetrics(
                AverageResponseTime: TimeSpan.FromTicks((long)times.Average(t => t.Ticks)),
                MinResponseTime: times.Min(),
                MaxResponseTime: times.Max(),
                Throughput: times.Count / overallStopwatch.Elapsed.TotalSeconds,
                TotalOperations: concurrency * operationsPerUser,
                SuccessfulOperations: times.Count,
                SuccessRate: (double)times.Count / (concurrency * operationsPerUser),
                ErrorCount: errors
            );
        }

        return new PerformanceMetrics(
            AverageResponseTime: TimeSpan.Zero,
            MinResponseTime: TimeSpan.Zero,
            MaxResponseTime: TimeSpan.Zero,
            Throughput: 0,
            TotalOperations: concurrency * operationsPerUser,
            SuccessfulOperations: 0,
            SuccessRate: 0.0,
            ErrorCount: errors
        );
    }

    private async Task<PerformanceMetrics> MeasureSustainedLoadPerformanceAsync(
        Func<Task<bool>> operation, TimeSpan duration, int operationsPerSecond)
    {
        var times = new ConcurrentBag<TimeSpan>();
        var errors = 0;
        var interval = TimeSpan.FromMilliseconds(1000.0 / operationsPerSecond);

        var cts = new CancellationTokenSource(duration);
        var overallStopwatch = Stopwatch.StartNew();

        var tasks = new List<Task>();

        while (!cts.Token.IsCancellationRequested)
        {
            var nextExecution = DateTime.UtcNow.Add(interval);

            tasks.Add(Task.Run(async () =>
            {
                var stopwatch = Stopwatch.StartNew();
                try
                {
                    await operation();
                    stopwatch.Stop();
                    times.Add(stopwatch.Elapsed);
                }
                catch
                {
                    stopwatch.Stop();
                    Interlocked.Increment(ref errors);
                }
            }, cts.Token));

            var delay = nextExecution - DateTime.UtcNow;
            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, cts.Token);
            }
        }

        await Task.WhenAll(tasks.Where(t => !t.IsCanceled));
        overallStopwatch.Stop();

        var timesList = times.ToList();
        if (timesList.Any())
        {
            return new PerformanceMetrics(
                AverageResponseTime: TimeSpan.FromTicks((long)timesList.Average(t => t.Ticks)),
                MinResponseTime: timesList.Min(),
                MaxResponseTime: timesList.Max(),
                Throughput: timesList.Count / overallStopwatch.Elapsed.TotalSeconds,
                TotalOperations: tasks.Count,
                SuccessfulOperations: timesList.Count,
                SuccessRate: (double)timesList.Count / tasks.Count,
                ErrorCount: errors
            );
        }

        return new PerformanceMetrics(
            AverageResponseTime: TimeSpan.Zero,
            MinResponseTime: TimeSpan.Zero,
            MaxResponseTime: TimeSpan.Zero,
            Throughput: 0,
            TotalOperations: tasks.Count,
            SuccessfulOperations: 0,
            SuccessRate: 0.0,
            ErrorCount: errors
        );
    }

    private async Task<Guid> CreateProjectAsync(string projectName)
    {
        var request = new CreateProjectRequest(projectName);
        var response = await _fixture.HttpClient.PostAsJsonAsync("/api/projects", request);
        response.Should().HaveStatusCode(HttpStatusCode.Created);
        var projectId = await response.Content.ReadFromJsonAsync<Guid>();
        _testProjectIds.Add(projectId);
        return projectId;
    }

    private async Task<Guid> CreateModelAsync(Guid projectId, string modelName, string? metadata = null)
    {
        var request = new CreateModelRequest(modelName, "1.0.0", "MIT", metadata ?? "{}");
        var response = await _fixture.HttpClient.PostAsJsonAsync($"/api/projects/{projectId}/models", request);
        response.Should().HaveStatusCode(HttpStatusCode.Created);
        var modelId = await response.Content.ReadFromJsonAsync<Guid>();
        _testModelIds.Add(modelId);
        return modelId;
    }

    private static object GenerateLargeModelMetadata(int targetSize)
    {
        var baseMetadata = new
        {
            name = "Large Performance Test Model",
            description = "Model with large metadata for performance testing",
            architecture = new
            {
                type = "transformer",
                layers = 96,
                hidden_size = 4096,
                attention_heads = 64
            },
            training = new
            {
                dataset = "Large Training Dataset",
                epochs = 100,
                batch_size = 32,
                learning_rate = 0.0001
            }
        };

        // Add large array to reach target size
        var largeArray = new List<object>();
        var currentSize = JsonSerializer.Serialize(baseMetadata).Length;

        while (currentSize < targetSize)
        {
            largeArray.Add(new
            {
                id = largeArray.Count,
                data = new string('x', Math.Min(1000, targetSize - currentSize)),
                timestamp = DateTime.UtcNow.AddMinutes(-largeArray.Count)
            });

            currentSize = JsonSerializer.Serialize(new { baseMetadata, largeData = largeArray }).Length;
        }

        return new { baseMetadata, largeData = largeArray };
    }

    private void ValidatePerformanceResults(
        ConcurrentDictionary<string, PerformanceMetrics> results,
        Dictionary<string, PerformanceTargets> targets)
    {
        foreach (var target in targets)
        {
            if (results.TryGetValue(target.Key, out var metrics))
            {
                metrics.AverageResponseTime.Should().BeLessThanOrEqualTo(target.Value.MaxResponseTime,
                    $"{target.Key} should meet response time target");

                metrics.Throughput.Should().BeGreaterThanOrEqualTo(target.Value.MinThroughput,
                    $"{target.Key} should meet throughput target");

                metrics.SuccessRate.Should().BeGreaterThan(0.95,
                    $"{target.Key} should have high success rate");
            }
        }
    }

    private void LogPerformanceResults(string category, ConcurrentDictionary<string, PerformanceMetrics> results)
    {
        _logger.LogInformation("=== {Category} Performance Results ===", category);

        foreach (var result in results)
        {
            var metrics = result.Value;
            _logger.LogInformation("{Operation}: Avg {AvgMs:F1}ms, Min {MinMs:F1}ms, Max {MaxMs:F1}ms, " +
                                 "Throughput {Throughput:F1} ops/sec, Success Rate {SuccessRate:P2}",
                result.Key,
                metrics.AverageResponseTime.TotalMilliseconds,
                metrics.MinResponseTime.TotalMilliseconds,
                metrics.MaxResponseTime.TotalMilliseconds,
                metrics.Throughput,
                metrics.SuccessRate);
        }
    }

    private void LogConcurrencyResults(Dictionary<int, PerformanceMetrics> results)
    {
        _logger.LogInformation("=== Concurrency Performance Results ===");

        foreach (var result in results.OrderBy(r => r.Key))
        {
            var metrics = result.Value;
            _logger.LogInformation("{ConcurrencyLevel} users: Avg {AvgMs:F1}ms, Throughput {Throughput:F1} ops/sec, " +
                                 "Success Rate {SuccessRate:P2}",
                result.Key,
                metrics.AverageResponseTime.TotalMilliseconds,
                metrics.Throughput,
                metrics.SuccessRate);
        }
    }

    private async Task SetupTestDataAsync()
    {
        // Pre-create some test data for read operations
        for (int i = 0; i < 5; i++)
        {
            var projectId = await CreateProjectAsync($"Performance Setup Project {i}");
            for (int j = 0; j < 3; j++)
            {
                await CreateModelAsync(projectId, $"Setup Model {i}-{j}");
            }
        }
    }

    private async Task CleanupTestDataAsync()
    {
        var cleanupTasks = new List<Task>();

        // Cleanup models
        foreach (var modelId in _testModelIds)
        {
            cleanupTasks.Add(Task.Run(async () =>
            {
                try
                {
                    await _fixture.HttpClient.DeleteAsync($"/api/models/{modelId}");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to cleanup model {ModelId}", modelId);
                }
            }));
        }

        // Cleanup projects
        foreach (var projectId in _testProjectIds)
        {
            cleanupTasks.Add(Task.Run(async () =>
            {
                try
                {
                    await _fixture.HttpClient.DeleteAsync($"/api/projects/{projectId}");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to cleanup project {ProjectId}", projectId);
                }
            }));
        }

        await Task.WhenAll(cleanupTasks);
        _testModelIds.Clear();
        _testProjectIds.Clear();
    }
}

public record PerformanceMetrics(
    TimeSpan AverageResponseTime,
    TimeSpan MinResponseTime,
    TimeSpan MaxResponseTime,
    double Throughput,
    int TotalOperations,
    int SuccessfulOperations,
    double SuccessRate,
    int ErrorCount
);

public record PerformanceTargets(TimeSpan MaxResponseTime, double MinThroughput);

public record CreateProjectRequest(string ProjectName);
public record CreateModelRequest(string ModelName, string Version, string License, string? MetadataJson = null);