using Hartonomous.MCP.DTOs;

namespace Hartonomous.MCP.Interfaces;

/// <summary>
/// Repository interface for MCP message management
/// </summary>
public interface IMessageRepository
{
    /// <summary>
    /// Store a message with user scope
    /// </summary>
    Task<Guid> StoreMessageAsync(McpMessage message, string userId);

    /// <summary>
    /// Get message by ID with user scope validation
    /// </summary>
    Task<McpMessage?> GetMessageAsync(Guid messageId, string userId);

    /// <summary>
    /// Get messages for a specific agent with user scope
    /// </summary>
    Task<IEnumerable<McpMessage>> GetMessagesForAgentAsync(Guid agentId, string userId, int limit = 100);

    /// <summary>
    /// Get conversation between two agents with user scope
    /// </summary>
    Task<IEnumerable<McpMessage>> GetConversationAsync(Guid fromAgentId, Guid toAgentId, string userId, int limit = 100);

    /// <summary>
    /// Mark message as processed
    /// </summary>
    Task<bool> MarkMessageProcessedAsync(Guid messageId, string userId);

    /// <summary>
    /// Get unprocessed messages for an agent
    /// </summary>
    Task<IEnumerable<McpMessage>> GetUnprocessedMessagesAsync(Guid agentId, string userId);

    /// <summary>
    /// Store task assignment
    /// </summary>
    Task<Guid> StoreTaskAssignmentAsync(TaskAssignment task, string userId);

    /// <summary>
    /// Get task assignment by ID with user scope
    /// </summary>
    Task<TaskAssignment?> GetTaskAssignmentAsync(Guid taskId, string userId);

    /// <summary>
    /// Get pending tasks for an agent
    /// </summary>
    Task<IEnumerable<TaskAssignment>> GetPendingTasksForAgentAsync(Guid agentId, string userId);

    /// <summary>
    /// Store task result
    /// </summary>
    Task<bool> StoreTaskResultAsync(TaskResult result, string userId);

    /// <summary>
    /// Get task result by task ID
    /// </summary>
    Task<TaskResult?> GetTaskResultAsync(Guid taskId, string userId);
}