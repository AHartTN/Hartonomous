using FluentAssertions;
using Hartonomous.AgentClient.Interfaces;
using System;
using Hartonomous.AgentClient.Models;
using Hartonomous.AgentClient.Services;
using Hartonomous.Core.Interfaces;
using Hartonomous.Infrastructure.Observability.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using System.Threading.Tasks;
using Xunit;

namespace Hartonomous.AgentClient.Tests.Services;

public class TaskExecutorServiceTests : IDisposable
{
    private readonly Mock<ILogger<TaskExecutorService>> _loggerMock;
    private readonly Mock<IMetricsCollector> _metricsCollectorMock;
    private readonly Mock<IAgentRuntime> _agentRuntimeMock;
    private readonly Mock<ICurrentUserService> _currentUserServiceMock;
    private readonly AgentClientConfiguration _configuration;
    private readonly TaskExecutorService _service;

    public TaskExecutorServiceTests()
    {
        _loggerMock = new Mock<ILogger<TaskExecutorService>>();
        _metricsCollectorMock = new Mock<IMetricsCollector>();
        _agentRuntimeMock = new Mock<IAgentRuntime>();
        _currentUserServiceMock = new Mock<ICurrentUserService>();

        _configuration = new AgentClientConfiguration
        {
            MaxInstancesPerUser = 5,
            DefaultTimeoutSeconds = 300
        };

        var configurationOptions = Options.Create(_configuration);

        _service = new TaskExecutorService(
            _loggerMock.Object,
            _metricsCollectorMock.Object,
            _agentRuntimeMock.Object,
            _currentUserServiceMock.Object,
            configurationOptions);

        // Setup current user service
        _currentUserServiceMock
            .Setup(x => x.GetCurrentUserIdAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("test-user-id");

        // Setup agent runtime to return a test instance
        _agentRuntimeMock
            .Setup(x => x.ListInstancesAsync(It.IsAny<string>(), It.IsAny<AgentStatus?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { CreateTestAgentInstance() });
    }

    [Fact]
    public async Task QueueTaskAsync_WithValidTask_ReturnsQueuedTask()
    {
        // Arrange
        var task = CreateTestTask();

        // Act
        var queuedTask = await _service.QueueTaskAsync(task);

        // Assert
        queuedTask.Should().NotBeNull();
        queuedTask.Status.Should().Be(TaskStatus.Queued);
        queuedTask.UpdatedAt.Should().BeAfter(task.UpdatedAt);

        // Verify metrics were recorded
        _metricsCollectorMock.Verify(
            x => x.IncrementCounter("task.queued", It.IsAny<Dictionary<string, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task QueueTaskAsync_WithNullTask_ThrowsArgumentNullException()
    {
        // Act & Assert
        await FluentActions
            .Invoking(() => _service.QueueTaskAsync(null!))
            .Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task GetTaskAsync_WithValidTaskId_ReturnsTask()
    {
        // Arrange
        var task = CreateTestTask();
        await _service.QueueTaskAsync(task);

        // Act
        var retrievedTask = await _service.GetTaskAsync(task.TaskId);

        // Assert
        retrievedTask.Should().NotBeNull();
        retrievedTask!.TaskId.Should().Be(task.TaskId);
    }

    [Fact]
    public async Task GetTaskAsync_WithInvalidTaskId_ReturnsNull()
    {
        // Act
        var task = await _service.GetTaskAsync("non-existent-task");

        // Assert
        task.Should().BeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public async Task GetTaskAsync_WithInvalidTaskId_ThrowsArgumentNullException(string? taskId)
    {
        // Act & Assert
        await FluentActions
            .Invoking(() => _service.GetTaskAsync(taskId!))
            .Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task ListTasksAsync_ReturnsTasksForCurrentUser()
    {
        // Arrange
        var task1 = CreateTestTask("task1");
        var task2 = CreateTestTask("task2");

        await _service.QueueTaskAsync(task1);
        await _service.QueueTaskAsync(task2);

        // Act
        var tasks = await _service.ListTasksAsync();

        // Assert
        tasks.Should().HaveCount(2);
        tasks.Should().OnlyContain(t => t.UserId == "test-user-id");
    }

    [Fact]
    public async Task ListTasksAsync_WithStatusFilter_ReturnsFilteredTasks()
    {
        // Arrange
        var task = CreateTestTask();
        await _service.QueueTaskAsync(task);

        // Act
        var queuedTasks = await _service.ListTasksAsync(status: TaskStatus.Queued);
        var runningTasks = await _service.ListTasksAsync(status: TaskStatus.Running);

        // Assert
        queuedTasks.Should().HaveCount(1);
        runningTasks.Should().BeEmpty();
    }

    [Fact]
    public async Task CancelTaskAsync_WithValidTaskId_CancelsTask()
    {
        // Arrange
        var task = CreateTestTask();
        await _service.QueueTaskAsync(task);

        // Act
        var cancelledTask = await _service.CancelTaskAsync(task.TaskId, "Test cancellation");

        // Assert
        cancelledTask.Should().NotBeNull();
        cancelledTask.Status.Should().Be(TaskStatus.Cancelled);

        // Verify metrics were recorded
        _metricsCollectorMock.Verify(
            x => x.IncrementCounter("task.cancelled", It.IsAny<Dictionary<string, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task CancelTaskAsync_WithNonExistentTask_ThrowsInvalidOperationException()
    {
        // Act & Assert
        await FluentActions
            .Invoking(() => _service.CancelTaskAsync("non-existent-task"))
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*not found*");
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public async Task CancelTaskAsync_WithInvalidTaskId_ThrowsArgumentNullException(string? taskId)
    {
        // Act & Assert
        await FluentActions
            .Invoking(() => _service.CancelTaskAsync(taskId!))
            .Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task UpdateTaskProgressAsync_WithValidTaskId_UpdatesProgress()
    {
        // Arrange
        var task = CreateTestTask();
        await _service.QueueTaskAsync(task);

        // Act
        await _service.UpdateTaskProgressAsync(task.TaskId, 50.0, "Half complete");

        // Assert
        var updatedTask = await _service.GetTaskAsync(task.TaskId);
        updatedTask.Should().NotBeNull();
        updatedTask!.ProgressPercent.Should().Be(50.0);
        updatedTask.ProgressMessage.Should().Be("Half complete");

        // Verify metrics were recorded
        _metricsCollectorMock.Verify(
            x => x.RecordGauge("task.progress", 50.0, It.IsAny<Dictionary<string, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task UpdateTaskProgressAsync_WithInvalidProgress_ClampsToValidRange()
    {
        // Arrange
        var task = CreateTestTask();
        await _service.QueueTaskAsync(task);

        // Act
        await _service.UpdateTaskProgressAsync(task.TaskId, 150.0); // Over 100%

        // Assert
        var updatedTask = await _service.GetTaskAsync(task.TaskId);
        updatedTask!.ProgressPercent.Should().Be(100.0);

        // Act
        await _service.UpdateTaskProgressAsync(task.TaskId, -10.0); // Under 0%

        // Assert
        updatedTask = await _service.GetTaskAsync(task.TaskId);
        updatedTask!.ProgressPercent.Should().Be(0.0);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public async Task UpdateTaskProgressAsync_WithInvalidTaskId_ThrowsArgumentNullException(string? taskId)
    {
        // Act & Assert
        await FluentActions
            .Invoking(() => _service.UpdateTaskProgressAsync(taskId!, 50.0))
            .Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task GetQueueStatisticsAsync_ReturnsStatistics()
    {
        // Arrange
        var task1 = CreateTestTask("task1");
        var task2 = CreateTestTask("task2");

        await _service.QueueTaskAsync(task1);
        await _service.QueueTaskAsync(task2);

        // Act
        var stats = await _service.GetQueueStatisticsAsync();

        // Assert
        stats.Should().NotBeNull();
        stats.QueuedTasks.Should().Be(2);
        stats.PendingTasks.Should().Be(0);
        stats.RunningTasks.Should().Be(0);
        stats.Timestamp.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task EstimateTaskExecutionTimeAsync_WithNoHistoricalData_ReturnsDefaultEstimate()
    {
        // Arrange
        var task = CreateTestTask();

        // Act
        var estimatedTime = await _service.EstimateTaskExecutionTimeAsync(task);

        // Assert
        estimatedTime.Should().BeGreaterThan(0);
        // Default estimate should be reasonable (5 minutes in milliseconds)
        estimatedTime.Should().Be(TimeSpan.FromMinutes(5).Milliseconds);
    }

    [Fact]
    public async Task EstimateTaskExecutionTimeAsync_WithNullTask_ThrowsArgumentNullException()
    {
        // Act & Assert
        await FluentActions
            .Invoking(() => _service.EstimateTaskExecutionTimeAsync(null!))
            .Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task ExecuteTaskBatchAsync_WithValidTasks_ReturnsSuccessfulBatchResult()
    {
        // Arrange
        var tasks = new[]
        {
            CreateTestTask("task1"),
            CreateTestTask("task2")
        };

        // Act
        var batchResult = await _service.ExecuteTaskBatchAsync(tasks, parallel: false);

        // Assert
        batchResult.Should().NotBeNull();
        batchResult.BatchId.Should().NotBeNullOrEmpty();
        batchResult.TaskResults.Should().HaveCount(2);
        batchResult.StartedAt.Should().BeBefore(batchResult.CompletedAt);
        batchResult.TotalDurationMs.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task ExecuteTaskBatchAsync_WithNullTasks_ThrowsArgumentNullException()
    {
        // Act & Assert
        await FluentActions
            .Invoking(() => _service.ExecuteTaskBatchAsync(null!))
            .Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task ExecuteTaskBatchAsync_WithEmptyTasks_ThrowsArgumentException()
    {
        // Act & Assert
        await FluentActions
            .Invoking(() => _service.ExecuteTaskBatchAsync(Array.Empty<AgentTask>()))
            .Should().ThrowAsync<ArgumentException>()
            .WithMessage("*cannot be empty*");
    }

    [Fact]
    public async Task ScheduleTaskAsync_WithValidTask_ReturnsScheduledTask()
    {
        // Arrange
        var task = CreateTestTask();
        var scheduledFor = DateTimeOffset.UtcNow.AddHours(1);

        // Act
        var scheduledTask = await _service.ScheduleTaskAsync(task, scheduledFor);

        // Assert
        scheduledTask.Should().NotBeNull();
        scheduledTask.Status.Should().Be(TaskStatus.Pending);
        scheduledTask.ScheduledFor.Should().Be(scheduledFor);
    }

    [Fact]
    public async Task ScheduleTaskAsync_WithNullTask_ThrowsArgumentNullException()
    {
        // Act & Assert
        await FluentActions
            .Invoking(() => _service.ScheduleTaskAsync(null!, DateTimeOffset.UtcNow.AddHours(1)))
            .Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task GetTaskHistoryAsync_WithValidTaskId_ReturnsHistory()
    {
        // Arrange
        var task = CreateTestTask();
        await _service.QueueTaskAsync(task);

        // Act
        var history = await _service.GetTaskHistoryAsync(task.TaskId);

        // Assert
        history.Should().NotBeNull();
        // History might be empty for queued tasks, but method should not throw
    }

    [Fact]
    public async Task GetTaskLogsAsync_WithValidTaskId_ReturnsLogs()
    {
        // Arrange
        var task = CreateTestTask();
        await _service.QueueTaskAsync(task);

        // Setup agent runtime to return logs
        _agentRuntimeMock
            .Setup(x => x.GetInstanceLogsAsync(It.IsAny<string>(), It.IsAny<DateTimeOffset?>(), It.IsAny<int?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                new LogEntry
                {
                    Message = "Test log entry",
                    Level = LogLevel.Information,
                    Timestamp = DateTimeOffset.UtcNow
                }
            });

        // Act
        var logs = await _service.GetTaskLogsAsync(task.TaskId);

        // Assert
        logs.Should().NotBeNull();
        // Logs might be empty if no instance is associated, but method should not throw
    }

    [Fact]
    public void TaskStatusChanged_Event_IsDefinedAndCanBeSubscribed()
    {
        // Arrange
        var eventFired = false;

        // Act
        _service.TaskStatusChanged += (sender, args) => eventFired = true;

        // Assert
        // Event subscription should not throw
        eventFired.Should().BeFalse(); // Not fired yet
    }

    [Fact]
    public void TaskProgressUpdated_Event_IsDefinedAndCanBeSubscribed()
    {
        // Arrange
        var eventFired = false;

        // Act
        _service.TaskProgressUpdated += (sender, args) => eventFired = true;

        // Assert
        // Event subscription should not throw
        eventFired.Should().BeFalse(); // Not fired yet
    }

    [Fact]
    public void TaskCompleted_Event_IsDefinedAndCanBeSubscribed()
    {
        // Arrange
        var eventFired = false;

        // Act
        _service.TaskCompleted += (sender, args) => eventFired = true;

        // Assert
        // Event subscription should not throw
        eventFired.Should().BeFalse(); // Not fired yet
    }

    [Fact]
    public void TaskFailed_Event_IsDefinedAndCanBeSubscribed()
    {
        // Arrange
        var eventFired = false;

        // Act
        _service.TaskFailed += (sender, args) => eventFired = true;

        // Assert
        // Event subscription should not throw
        eventFired.Should().BeFalse(); // Not fired yet
    }

    private static AgentTask CreateTestTask(string? taskId = null)
    {
        return new AgentTask
        {
            TaskId = taskId ?? Guid.NewGuid().ToString(),
            Name = "Test Task",
            Description = "A test task for unit testing",
            Type = "test",
            Priority = 5,
            Status = TaskStatus.Pending,
            AgentId = "test-agent",
            Input = new Dictionary<string, object> { ["input1"] = "value1" },
            Configuration = new Dictionary<string, object> { ["config1"] = "value1" },
            TimeoutSeconds = 300,
            MaxRetries = 3,
            Dependencies = Array.Empty<string>(),
            Tags = new[] { "test" },
            UserId = "test-user-id",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            Cancellable = true
        };
    }

    private static AgentInstance CreateTestAgentInstance()
    {
        return new AgentInstance
        {
            InstanceId = Guid.NewGuid().ToString(),
            AgentId = "test-agent",
            Name = "Test Agent Instance",
            Version = "1.0.0",
            Status = AgentStatus.Running,
            WorkingDirectory = Path.GetTempPath(),
            Configuration = new Dictionary<string, object>(),
            Environment = new Dictionary<string, string>(),
            UserId = "test-user-id",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
    }

    public void Dispose()
    {
        _service.Dispose();
    }
}