/*
 * Copyright (c) 2024-2025 Hartonomous AI Agent Factory Platform. All Rights Reserved.
 *
 * This file contains the interface for the task routing service.
 */
using Hartonomous.Core.Entities;
using System.Threading.Tasks;

namespace Hartonomous.AgentClient.Interfaces
{
    public interface ITaskRoutingService
    {
        Task<Agent> SelectAgentForTaskAsync(AgentTask task);
    }
}
