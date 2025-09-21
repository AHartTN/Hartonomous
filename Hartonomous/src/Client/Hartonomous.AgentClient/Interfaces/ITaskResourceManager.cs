/*
 * Copyright (c) 2024-2025 Hartonomous AI Agent Factory Platform. All Rights Reserved.
 *
 * This file contains the interface for the task resource manager.
 */
using System.Threading.Tasks;
using Hartonomous.AgentClient.Models;

namespace Hartonomous.AgentClient.Interfaces
{
    public interface ITaskResourceManager
    {
        Task AcquireResourcesAsync(AgentTask task);
        Task ReleaseResourcesAsync(AgentTask task);
    }
}
