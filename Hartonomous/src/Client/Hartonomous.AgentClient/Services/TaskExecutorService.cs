/*
 * Copyright (c) 2024-2025 Hartonomous AI Agent Factory Platform. All Rights Reserved.
 *
 * This software is proprietary and confidential. Unauthorized copying, distribution,
 * modification, or use of this software, in whole or in part, is strictly prohibited.
 *
 * This file contains the refactored TaskExecutorService, which now acts as a facade
 * to coordinate the various purpose-built task execution components.
 */
using System;
using System.Threading;
using System.Threading.Tasks;
using Hartonomous.AgentClient.Interfaces;
using Hartonomous.AgentClient.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Hartonomous.AgentClient.Services
{
    public class TaskExecutorService : BackgroundService
    {
        private readonly ILogger<TaskExecutorService> _logger;
        private readonly ITaskQueueManager _queueManager;
        private readonly TaskExecutorCore _taskExecutorCore;

        public TaskExecutorService(
            ILogger<TaskExecutorService> logger,
            ITaskQueueManager queueManager,
            TaskExecutorCore taskExecutorCore)
        {
            _logger = logger;
            _queueManager = queueManager;
            _taskExecutorCore = taskExecutorCore;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("TaskExecutorService is starting.");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var task = await _queueManager.DequeueTaskAsync();
                    if (task != null)
                    {
                        _logger.LogInformation("Dequeued task {TaskId} for execution.", task.TaskId);
                        // Do not await this, to allow for concurrent task execution.
                        _ = _taskExecutorCore.ExecuteTaskAsync(task, stoppingToken);
                    }
                    else
                    {
                        // No tasks in the queue, wait for a bit.
                        await Task.Delay(1000, stoppingToken);
                    }
                }
                catch (OperationCanceledException)
                {
                    // When the stopping token is triggered, Task.Delay will throw this exception.
                    _logger.LogInformation("TaskExecutorService is stopping.");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "An unexpected error occurred in the TaskExecutorService execution loop.");
                    // Wait a bit before retrying, to avoid fast failure loops.
                    await Task.Delay(5000, stoppingToken);
                }
            }
        }

        public async Task<AgentTask> SubmitTaskAsync(AgentTask task)
        {
            await _queueManager.EnqueueTaskAsync(task);
            _logger.LogInformation("Submitted task {TaskId} to the queue.", task.TaskId);
            return task;
        }
    }
}
