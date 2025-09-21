/*
 * Copyright (c) 2024-2025 Hartonomous AI Agent Factory Platform. All Rights Reserved.
 *
 * This file contains the interface for the task retry handler.
 */
using System;
using System.Threading.Tasks;
using Hartonomous.AgentClient.Models;

namespace Hartonomous.AgentClient.Interfaces
{
    public interface ITaskRetryHandler
    {
        Task<bool> ShouldRetryAsync(AgentTask task, Exception exception);
        Task ScheduleRetryAsync(AgentTask task, Exception exception);
    }
}
