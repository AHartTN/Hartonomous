/*
 * Copyright (c) 2024-2025 Hartonomous AI Agent Factory Platform. All Rights Reserved.
 *
 * This software is proprietary and confidential. Unauthorized copying, distribution,
 * modification, or use of this software, in whole or in part, is strictly prohibited.
 *
 * This file contains the purpose-built task progress tracker for monitoring and metrics.
 * Features real-time progress updates, execution metrics, and performance analytics.
 */

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Hartonomous.AgentClient.Interfaces;
using Hartonomous.AgentClient.Models;
using Hartonomous.Infrastructure.Observability.Interfaces;
using Microsoft.Extensions.Logging;

namespace Hartonomous.AgentClient.Services
{
    public class TaskProgressTracker : ITaskProgressTracker
    {
        private readonly ILogger<TaskProgressTracker> _logger;
        private readonly IMetricsCollector _metricsCollector;
        private readonly ITaskQueueManager _queueManager;
        private readonly ConcurrentDictionary<string, Stopwatch> _taskTimers = new();
        private readonly ConcurrentDictionary<string, List<ProgressEvent>> _progressHistory = new();

        public TaskProgressTracker(
            ILogger<TaskProgressTracker> logger,
            IMetricsCollector metricsCollector,
            ITaskQueueManager queueManager)
        {
            _logger = logger;
            _metricsCollector = metricsCollector;
            _queueManager = queueManager;
        }

        public Task StartTaskTimingAsync(string taskId)
        {
            var stopwatch = Stopwatch.StartNew();
            _taskTimers.TryAdd(taskId, stopwatch);
            return Task.CompletedTask;
        }

        public Task ReportProgressAsync(string taskId, int progressPercent, string? message = null)
        {
            var task = _queueManager.GetTaskById(taskId);
            if (task != null)
            {
                task.ProgressPercent = progressPercent;
                task.ProgressMessage = message;
                _progressHistory.AddOrUpdate(taskId,
                    new List<ProgressEvent> { new ProgressEvent { TaskId = taskId, ProgressPercent = progressPercent, Message = message, Timestamp = DateTimeOffset.UtcNow } },
                    (key, list) => { list.Add(new ProgressEvent { TaskId = taskId, ProgressPercent = progressPercent, Message = message, Timestamp = DateTimeOffset.UtcNow }); return list; });
            }
            return Task.CompletedTask;
        }

        public Task CompleteTaskTimingAsync(string taskId, Models.TaskStatus finalStatus)
        {
            if (_taskTimers.TryRemove(taskId, out var stopwatch))
            {
                stopwatch.Stop();
                var task = _queueManager.GetTaskById(taskId);
                if (task != null)
                {
                    task.CompletedAt = DateTimeOffset.UtcNow;
                }
            }
            return Task.CompletedTask;
        }

        public async Task<double> EstimateTimeToCompleteAsync(AgentTask task)
        {
            var similarTasks = _queueManager.GetTasks()
                .Where(t => t.Type == task.Type && t.Status == Models.TaskStatus.Completed && t.CompletedAt.HasValue && t.StartedAt.HasValue)
                .ToList();

            if (similarTasks.Any())
            {
                return similarTasks.Average(t => (t.CompletedAt.Value - t.StartedAt.Value).TotalMilliseconds);
            }
            return TimeSpan.FromMinutes(5).TotalMilliseconds;
        }

        public List<ProgressEvent> GetTaskProgressHistory(string taskId)
        {
            return _progressHistory.TryGetValue(taskId, out var history) ? history : new List<ProgressEvent>();
        }

        public TaskExecutionMetrics GetExecutionMetrics()
        {
            var allTasks = _queueManager.GetTasks().ToList();
            var completedTasks = allTasks.Where(t => t.Status == Models.TaskStatus.Completed && t.CompletedAt.HasValue && t.StartedAt.HasValue).ToList();
            var failedTasks = allTasks.Where(t => t.Status == Models.TaskStatus.Failed).ToList();

            return new TaskExecutionMetrics
            {
                SuccessfulTasks = completedTasks.Count,
                FailedTasks = failedTasks.Count,
                AverageExecutionTime = completedTasks.Any() ? TimeSpan.FromMilliseconds(completedTasks.Average(t => (t.CompletedAt.Value - t.StartedAt.Value).TotalMilliseconds)) : TimeSpan.Zero,
            };
        }
    }

    public class ProgressEvent
    {
        public string TaskId { get; set; }
        public double ProgressPercent { get; set; }
        public string? Message { get; set; }
        public DateTimeOffset Timestamp { get; set; }
    }

    public class TaskExecutionMetrics
    {
        public int SuccessfulTasks { get; set; }
        public int FailedTasks { get; set; }
        public TimeSpan AverageExecutionTime { get; set; }
    }
}