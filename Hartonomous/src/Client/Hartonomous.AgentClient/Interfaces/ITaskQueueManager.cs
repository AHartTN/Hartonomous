/*
 * Copyright (c) 2024-2025 Hartonomous AI Agent Factory Platform. All Rights Reserved.
 *
 * This file contains the interface for the task queue manager.
 */
using System.Collections.Generic;
using System.Threading.Tasks;
using Hartonomous.AgentClient.Models;

namespace Hartonomous.AgentClient.Interfaces
{
    public interface ITaskQueueManager
    {
        Task EnqueueTaskAsync(AgentTask task);
        Task<AgentTask?> DequeueTaskAsync();
        IEnumerable<AgentTask> GetTasks(string? userId = null, Models.TaskStatus? status = null);
        AgentTask? GetTaskById(string taskId);
        bool UpdateTaskStatus(string taskId, Models.TaskStatus status, string? message = null);
    }
}
