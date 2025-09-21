/*
 * Copyright (c) 2024-2025 Hartonomous AI Agent Factory Platform. All Rights Reserved.
 *
 * This software is proprietary and confidential. Unauthorized copying, distribution,
 * modification, or use of this software, in whole or in part, is strictly prohibited.
 *
 * This file contains the purpose-built task queue manager for focused queue operations.
 * Features task scheduling, priority management, and queue statistics.
 */

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Hartonomous.AgentClient.Interfaces;
using Hartonomous.AgentClient.Models;
using Hartonomous.Infrastructure.Observability.Interfaces;
using Microsoft.Extensions.Logging;

namespace Hartonomous.AgentClient.Services
{
    public class TaskQueueManager : ITaskQueueManager
    {
        private readonly ILogger<TaskQueueManager> _logger;
        private readonly IMetricsCollector _metricsCollector;
        private readonly ConcurrentQueue<AgentTask> _taskQueue = new();
        private readonly ConcurrentDictionary<string, AgentTask> _tasks = new();

        public TaskQueueManager(ILogger<TaskQueueManager> logger, IMetricsCollector metricsCollector)
        {
            _logger = logger;
            _metricsCollector = metricsCollector;
        }

        public Task EnqueueTaskAsync(AgentTask task)
        {
            _taskQueue.Enqueue(task);
            _tasks.TryAdd(task.TaskId, task);
            return Task.CompletedTask;
        }

        public Task<AgentTask?> DequeueTaskAsync()
        {
            _taskQueue.TryDequeue(out var task);
            return Task.FromResult(task);
        }

        public AgentTask? GetTaskById(string taskId)
        {
            _tasks.TryGetValue(taskId, out var task);
            return task;
        }

        public IEnumerable<AgentTask> GetTasks(string? userId = null, Models.TaskStatus? status = null)
        {
            var query = _tasks.Values.AsEnumerable();
            if (userId != null)
            {
                query = query.Where(t => t.UserId == userId);
            }
            if (status != null)
            {
                query = query.Where(t => t.Status == status);
            }
            return query;
        }

        public bool UpdateTaskStatus(string taskId, Models.TaskStatus status, string? message = null)
        {
            if (_tasks.TryGetValue(taskId, out var task))
            {
                task.Status = status;
                task.ProgressMessage = message;
                return true;
            }
            return false;
        }

        public QueueStatistics GetQueueStatistics()
        {
            return new QueueStatistics
            {
                QueuedTasks = _taskQueue.Count,
                TotalTasks = _tasks.Count,
            };
        }
    }

    public class QueueStatistics
    {
        public int QueuedTasks { get; set; }
        public int TotalTasks { get; set; }
    }
}
