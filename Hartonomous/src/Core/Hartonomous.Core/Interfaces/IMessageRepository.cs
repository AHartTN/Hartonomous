using Hartonomous.Core.DTOs;

namespace Hartonomous.Core.Interfaces;

/// <summary>
/// Repository for storing and retrieving MCP messages
/// </summary>
public interface IMessageRepository : IRepository<McpMessage>
{
    /// <summary>
    /// Store a message
    /// </summary>
    Task<Guid> StoreMessageAsync(McpMessage message, string userId);
    /// <summary>
    /// Get messages for a specific agent
    /// </summary>
    Task<IEnumerable<McpMessage>> GetMessagesForAgentAsync(Guid agentId, string userId, int limit = 100);

    /// <summary>
    /// Get all messages in a project
    /// </summary>
    Task<IEnumerable<McpMessage>> GetMessagesByProjectAsync(Guid projectId, string userId, int limit = 1000);

    /// <summary>
    /// Get unread messages for an agent
    /// </summary>
    Task<IEnumerable<McpMessage>> GetUnreadMessagesAsync(Guid agentId, string userId);

    /// <summary>
    /// Mark messages as read
    /// </summary>
    Task<bool> MarkMessagesAsReadAsync(Guid agentId, IEnumerable<Guid> messageIds, string userId);

    /// <summary>
    /// Store a task assignment
    /// </summary>
    Task<bool> StoreTaskAssignmentAsync(TaskAssignment assignment, string userId);

    /// <summary>
    /// Get a task assignment
    /// </summary>
    Task<TaskAssignment?> GetTaskAssignmentAsync(Guid taskId, string userId);

    /// <summary>
    /// Get all task assignments for an agent
    /// </summary>
    Task<IEnumerable<TaskAssignment>> GetTaskAssignmentsForAgentAsync(Guid agentId, string userId);

    /// <summary>
    /// Store a task result
    /// </summary>
    Task<bool> StoreTaskResultAsync(TaskResult result, string userId);

    /// <summary>
    /// Get a task result
    /// </summary>
    Task<TaskResult?> GetTaskResultAsync(Guid taskId, string userId);
}