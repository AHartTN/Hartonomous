/*
 * Copyright (c) 2024-2025 Hartonomous AI Agent Factory Platform. All Rights Reserved.
 *
 * This file contains the concrete implementation for the task routing service.
 */
using System;
using System.Threading.Tasks;
using Hartonomous.Core.Entities;
using Hartonomous.AgentClient.Interfaces;
using Hartonomous.AgentClient.Models;
using Microsoft.Extensions.Logging;

namespace Hartonomous.AgentClient.Services
{
    public class TaskRoutingService : ITaskRoutingService
    {
        private readonly ILogger<TaskRoutingService> _logger;
        private readonly AgentRegistryService _agentRegistry;

        public TaskRoutingService(ILogger<TaskRoutingService> logger, AgentRegistryService agentRegistry)
        {
            _logger = logger;
            _agentRegistry = agentRegistry;
        }

        public async Task<Agent> SelectAgentForTaskAsync(AgentTask task)
        {
            // This is a simplified routing strategy. A real implementation would
            // involve more complex logic based on agent capabilities, load, etc.
            var availableAgents = await _agentRegistry.GetAvailableAgentsAsync();
            
            if (availableAgents.Count == 0)
            {
                throw new InvalidOperationException("No available agents to handle the task.");
            }

            // For now, just return the first available agent.
            var selectedAgent = availableAgents[0];
            
            _logger.LogInformation("Selected agent {AgentId} for task {TaskId}", selectedAgent.AgentId, task.TaskId);

            return selectedAgent;
        }
    }
}
