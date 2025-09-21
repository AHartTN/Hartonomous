/*
 * Copyright (c) 2024-2025 Hartonomous AI Agent Factory Platform. All Rights Reserved.
 *
 * This file contains the interface for the task progress tracker.
 */
using System.Threading.Tasks;
using Hartonomous.AgentClient.Models;

namespace Hartonomous.AgentClient.Interfaces
{
    public interface ITaskProgressTracker
    {
        Task StartTaskTimingAsync(string taskId);
        Task ReportProgressAsync(string taskId, int progressPercent, string? message = null);
        Task CompleteTaskTimingAsync(string taskId, Models.TaskStatus finalStatus);
        Task<double> EstimateTimeToCompleteAsync(AgentTask task);
    }
}
