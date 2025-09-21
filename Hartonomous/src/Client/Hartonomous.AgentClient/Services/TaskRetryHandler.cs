/*
 * Copyright (c) 2024-2025 Hartonomous AI Agent Factory Platform. All Rights Reserved.
 *
 * This software is proprietary and confidential. Unauthorized copying, distribution,
 * modification, or use of this software, in whole or in part, is strictly prohibited.
 *
 * This file contains the purpose-built task retry handler for robust error recovery.
 * Features exponential backoff, retry policies, and failure analysis.
 */

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Hartonomous.AgentClient.Interfaces;
using Hartonomous.AgentClient.Models;
using Hartonomous.Infrastructure.Observability.Interfaces;
using Microsoft.Extensions.Logging;

namespace Hartonomous.AgentClient.Services
{
    public class TaskRetryHandler : ITaskRetryHandler
    {
        private readonly ILogger<TaskRetryHandler> _logger;
        private readonly IMetricsCollector _metricsCollector;
        private readonly ITaskQueueManager _queueManager;

        public TaskRetryHandler(
            ILogger<TaskRetryHandler> logger,
            IMetricsCollector metricsCollector,
            ITaskQueueManager queueManager)
        {
            _logger = logger;
            _metricsCollector = metricsCollector;
            _queueManager = queueManager;
        }

        public Task<bool> ShouldRetryAsync(AgentTask task, Exception exception)
        {
            if (task.RetryCount >= task.MaxRetries)
            {
                return Task.FromResult(false);
            }
            return Task.FromResult(IsRetryableException(exception));
        }

        public async Task ScheduleRetryAsync(AgentTask task, Exception exception)
        {
            task.RetryCount++;
            var delay = TimeSpan.FromSeconds(Math.Pow(2, task.RetryCount));
            _logger.LogInformation("Scheduling retry for task {TaskId} in {Delay}", task.TaskId, delay);
            await Task.Delay(delay);
            await _queueManager.EnqueueTaskAsync(task);
        }

        private static bool IsRetryableException(Exception exception)
        {
            return exception is TimeoutException || exception is HttpRequestException;
        }

        public IEnumerable<AgentTask> GetTasksReadyForRetry()
        {
            return _queueManager.GetTasks(status: Models.TaskStatus.Pending)
                .Where(t => t.ScheduledFor.HasValue && t.ScheduledFor.Value <= DateTimeOffset.UtcNow);
        }

        public RetryStatistics GetRetryStatistics()
        {
            var retriedTasks = _queueManager.GetTasks().Where(t => t.RetryCount > 0).ToList();
            return new RetryStatistics
            {
                TotalTasksWithRetries = retriedTasks.Count,
                AverageRetryCount = retriedTasks.Any() ? retriedTasks.Average(t => t.RetryCount) : 0,
            };
        }
    }

    public class RetryStatistics
    {
        public int TotalTasksWithRetries { get; set; }
        public double AverageRetryCount { get; set; }
    }
}