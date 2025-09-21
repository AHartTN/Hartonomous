/*
 * Copyright (c) 2024-2025 Hartonomous AI Agent Factory Platform. All Rights Reserved.
 *
 * This software is proprietary and confidential. Unauthorized copying, distribution,
 * modification, or use of this software, in whole or in part, is strictly prohibited.
 *
 * This file contains the core task execution engine - the heart of the refactored TaskExecutorService.
 * Features real, functional task execution with clean separation of concerns.
 */
using System;
using System.Threading;
using System.Threading.Tasks;
using Hartonomous.AgentClient.Events;
using Hartonomous.AgentClient.Interfaces;
using Hartonomous.AgentClient.Models;
using Hartonomous.Infrastructure.Observability.Interfaces;
using Microsoft.Extensions.Logging;

namespace Hartonomous.AgentClient.Services
{
    public class TaskExecutorCore
    {
        private readonly ILogger<TaskExecutorCore> _logger;
        private readonly ITaskQueueManager _queueManager;
        private readonly ITaskRoutingService _routingService;
        private readonly ITaskRetryHandler _retryHandler;
        private readonly ITaskProgressTracker _progressTracker;
        private readonly ITaskResourceManager _resourceManager;
        private readonly IMetricsCollector _metrics;

        public event EventHandler<TaskStateChangeEventArgs>? TaskStateChanged;
        public event EventHandler<TaskProgressEventArgs>? TaskProgress;
        public event EventHandler<TaskResultEventArgs>? TaskCompleted;
        public event EventHandler<TaskErrorEventArgs>? TaskFailed;

        public TaskExecutorCore(
            ILogger<TaskExecutorCore> logger,
            ITaskQueueManager queueManager,
            ITaskRoutingService routingService,
            ITaskRetryHandler retryHandler,
            ITaskProgressTracker progressTracker,
            ITaskResourceManager resourceManager,
            IMetricsCollector metrics)
        {
            _logger = logger;
            _queueManager = queueManager;
            _routingService = routingService;
            _retryHandler = retryHandler;
            _progressTracker = progressTracker;
            _resourceManager = resourceManager;
            _metrics = metrics;
        }

        public async Task ExecuteTaskAsync(AgentTask task, CancellationToken cancellationToken)
        {
            if (task == null) throw new ArgumentNullException(nameof(task));

            var originalStatus = task.Status;
            _queueManager.UpdateTaskStatus(task.TaskId, Models.TaskStatus.Running, "Starting execution.");
            OnTaskStateChanged(task.TaskId, originalStatus, Models.TaskStatus.Running, "Starting execution.");

            try
            {
                await _progressTracker.StartTaskTimingAsync(task.TaskId);
                await _resourceManager.AcquireResourcesAsync(task);

                var agent = await _routingService.SelectAgentForTaskAsync(task);
                
                _logger.LogInformation("Executing task {TaskId} on agent {AgentId}", task.TaskId, agent.AgentId);

                var result = await agent.ExecuteTaskAsync(task, cancellationToken);

                await _resourceManager.ReleaseResourcesAsync(task);
                await _progressTracker.CompleteTaskTimingAsync(task.TaskId, Models.TaskStatus.Completed);

                originalStatus = task.Status;
                _queueManager.UpdateTaskStatus(task.TaskId, Models.TaskStatus.Completed, "Execution finished successfully.");
                OnTaskStateChanged(task.TaskId, originalStatus, Models.TaskStatus.Completed, "Execution finished successfully.");
                OnTaskCompleted(task.TaskId, result);

                _metrics.IncrementCounter("task.execution.success");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Error executing task {TaskId}", task.TaskId);
                await _resourceManager.ReleaseResourcesAsync(task);
                await _progressTracker.CompleteTaskTimingAsync(task.TaskId, Models.TaskStatus.Failed);

                bool shouldRetry = await _retryHandler.ShouldRetryAsync(task, ex);
                if (shouldRetry)
                {
                    await _retryHandler.ScheduleRetryAsync(task, ex);
                    originalStatus = task.Status;
                    _queueManager.UpdateTaskStatus(task.TaskId, Models.TaskStatus.Pending, $"Task failed, retry scheduled: {ex.Message}");
                    OnTaskStateChanged(task.TaskId, originalStatus, Models.TaskStatus.Pending, $"Task failed, retry scheduled: {ex.Message}");
                }
                else
                {
                    originalStatus = task.Status;
                    _queueManager.UpdateTaskStatus(task.TaskId, Models.TaskStatus.Failed, $"Task failed permanently: {ex.Message}");
                    OnTaskStateChanged(task.TaskId, originalStatus, Models.TaskStatus.Failed, $"Task failed permanently: {ex.Message}");
                    OnTaskFailed(task.TaskId, ex);
                }
                _metrics.IncrementCounter("task.execution.failure");
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("Task {TaskId} was canceled.", task.TaskId);
                await _resourceManager.ReleaseResourcesAsync(task);
                await _progressTracker.CompleteTaskTimingAsync(task.TaskId, Models.TaskStatus.Cancelled);
                originalStatus = task.Status;
                _queueManager.UpdateTaskStatus(task.TaskId, Models.TaskStatus.Cancelled, "Task was canceled.");
                OnTaskStateChanged(task.TaskId, originalStatus, Models.TaskStatus.Cancelled, "Task was canceled.");
                _metrics.IncrementCounter("task.execution.canceled");
            }
        }

        protected virtual void OnTaskStateChanged(string taskId, Models.TaskStatus oldStatus, Models.TaskStatus newStatus, string? message)
        {
            TaskStateChanged?.Invoke(this, new TaskStateChangeEventArgs(taskId, oldStatus, newStatus, message));
        }

        protected virtual void OnTaskProgress(string taskId, int progress, string? message)
        {
            TaskProgress?.Invoke(this, new TaskProgressEventArgs(taskId, progress, message));
        }

        protected virtual void OnTaskCompleted(string taskId, TaskResult result)
        {
            TaskCompleted?.Invoke(this, new TaskResultEventArgs(taskId, result));
        }

        protected virtual void OnTaskFailed(string taskId, Exception ex)
        {
            TaskFailed?.Invoke(this, new TaskErrorEventArgs(taskId, ex));
        }
    }
}
