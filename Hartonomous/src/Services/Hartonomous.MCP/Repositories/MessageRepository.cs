/*
 * Copyright (c) 2024-2025 Hartonomous AI Agent Factory Platform. All Rights Reserved.
 *
 * This software is proprietary and confidential. Unauthorized copying, distribution,
 * modification, or use of this software, in whole or in part, is strictly prohibited.
 *
 * This file contains the Multi-Context Protocol (MCP) message repository for inter-agent communication.
 * Features message storage, conversation tracking, task assignment, and result management with user isolation.
 */

using Dapper;
using Hartonomous.Core.DTOs;
using Hartonomous.Core.Interfaces;
using Hartonomous.Core.Abstractions;
using Hartonomous.Core.Configuration;
using Hartonomous.Core.Entities;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using System.Data;
using System.Text.Json;

namespace Hartonomous.MCP.Repositories;

public class MessageRepository : BaseRepository<Message, Guid>, IMessageRepository
{
    public MessageRepository(IOptions<SqlServerOptions> sqlOptions) : base(sqlOptions)
    {
    }

    protected override string GetTableName() => "dbo.McpMessages";

    protected override string GetSelectColumns() =>
        "MessageId as Id, UserId, FromAgentId as ConversationId, ToAgentId as AgentId, Payload as Content, MessageType, Metadata, Timestamp as CreatedDate, ProcessedAt as ModifiedDate";

    protected override (string Columns, string Parameters) GetInsertColumnsAndParameters() =>
        ("MessageId, UserId, FromAgentId, ToAgentId, Payload, MessageType, Metadata, Timestamp",
         "@Id, @UserId, @ConversationId, @AgentId, @Content, @MessageType, @Metadata, @CreatedDate");

    protected override string GetUpdateSetClause() =>
        "Payload = @Content, MessageType = @MessageType, Metadata = @Metadata, ProcessedAt = @ModifiedDate";

    protected override Message MapToEntity(dynamic row)
    {
        return new Message
        {
            Id = row.Id,
            UserId = row.UserId,
            ConversationId = row.ConversationId,
            AgentId = row.AgentId,
            Content = row.Content?.ToString() ?? string.Empty,
            MessageType = (MessageType)row.MessageType,
            Metadata = DeserializeFromJson<Dictionary<string, object>>(row.Metadata) ?? new Dictionary<string, object>(),
            CreatedDate = row.CreatedDate,
            ModifiedDate = row.ModifiedDate
        };
    }

    protected override object GetParameters(Message entity)
    {
        return new
        {
            Id = entity.Id,
            UserId = entity.UserId,
            ConversationId = entity.ConversationId,
            AgentId = entity.AgentId,
            Content = entity.Content,
            MessageType = (int)entity.MessageType,
            Metadata = SerializeToJson(entity.Metadata),
            CreatedDate = entity.CreatedDate,
            ModifiedDate = entity.ModifiedDate
        };
    }

    public async Task<Guid> StoreMessageAsync(McpMessage message, string userId)
    {
        var entity = new Message
        {
            Id = message.MessageId,
            UserId = userId,
            ConversationId = message.FromAgentId,
            AgentId = message.ToAgentId,
            Content = SerializeToJson(message.Payload),
            MessageType = (MessageType)Enum.Parse(typeof(MessageType), message.MessageType),
            Metadata = message.Metadata ?? new Dictionary<string, object>(),
            CreatedDate = message.Timestamp
        };

        return await CreateAsync(entity);
    }

    public async Task<McpMessage?> GetMessageAsync(Guid messageId, string userId)
    {
        var entity = await GetByIdAsync(messageId);
        if (entity?.UserId != userId) return null;

        return new McpMessage(
            entity.Id,
            entity.ConversationId,
            entity.AgentId ?? Guid.Empty,
            entity.MessageType.ToString(),
            DeserializeFromJson<object>(entity.Content) ?? new object(),
            entity.Metadata,
            entity.CreatedDate
        );
    }

    public async Task<IEnumerable<McpMessage>> GetMessagesForAgentAsync(Guid agentId, string userId, int limit = 100)
    {
        var entities = await GetByUserAsync(userId);
        return entities.Where(e => e.AgentId == agentId || e.ConversationId == agentId)
                      .Take(limit)
                      .Select(ConvertToMcpMessage);
    }

    public async Task<IEnumerable<McpMessage>> GetConversationAsync(Guid fromAgentId, Guid toAgentId, string userId, int limit = 100)
    {
        var entities = await GetByUserAsync(userId);
        return entities.Where(e => (e.ConversationId == fromAgentId && e.AgentId == toAgentId) ||
                                   (e.ConversationId == toAgentId && e.AgentId == fromAgentId))
                      .Take(limit)
                      .Select(ConvertToMcpMessage);
    }

    public async Task<bool> MarkMessageProcessedAsync(Guid messageId, string userId)
    {
        var entity = await GetByIdAsync(messageId);
        if (entity?.UserId != userId) return false;

        entity.ModifiedDate = DateTime.UtcNow;
        return await UpdateAsync(entity);
    }

    public async Task<IEnumerable<McpMessage>> GetUnprocessedMessagesAsync(Guid agentId, string userId)
    {
        var entities = await GetByUserAsync(userId);
        return entities.Where(e => e.AgentId == agentId && e.ModifiedDate == null)
                      .Select(ConvertToMcpMessage);
    }

    public async Task<IEnumerable<McpMessage>> GetMessagesByProjectAsync(Guid projectId, string userId, int limit = 100)
    {
        var entities = await GetByUserAsync(userId);
        return entities.Take(limit).Select(ConvertToMcpMessage);
    }

    public Task<IEnumerable<McpMessage>> GetUnreadMessagesAsync(Guid agentId, string userId) =>
        GetUnprocessedMessagesAsync(agentId, userId);

    public async Task<bool> MarkMessagesAsReadAsync(Guid agentId, IEnumerable<Guid> messageIds, string userId)
    {
        var entities = await GetByUserAsync(userId);
        var toUpdate = entities.Where(e => e.AgentId == agentId && messageIds.Contains(e.Id));

        var results = new List<bool>();
        foreach (var entity in toUpdate)
        {
            entity.ModifiedDate = DateTime.UtcNow;
            results.Add(await UpdateAsync(entity));
        }

        return results.All(r => r);
    }

    public async Task<bool> StoreTaskAssignmentAsync(TaskAssignment task, string userId)
    {
        // Store as message for now - would need separate entity/repository for proper implementation
        var entity = new Message
        {
            Id = task.TaskId,
            UserId = userId,
            AgentId = task.AgentId,
            Content = SerializeToJson(task.TaskData),
            MessageType = MessageType.Task,
            Metadata = task.Metadata ?? new Dictionary<string, object>(),
            CreatedDate = DateTime.UtcNow
        };

        await CreateAsync(entity);
        return true;
    }

    public async Task<TaskAssignment?> GetTaskAssignmentAsync(Guid taskId, string userId)
    {
        var entity = await GetByIdAsync(taskId);
        if (entity?.UserId != userId || entity.MessageType != MessageType.Task) return null;

        return new TaskAssignment(
            entity.Id,
            entity.AgentId ?? Guid.Empty,
            "default",
            DeserializeFromJson<object>(entity.Content) ?? new object(),
            1,
            DateTime.UtcNow.AddDays(1),
            entity.Metadata
        );
    }

    public async Task<IEnumerable<TaskAssignment>> GetPendingTasksForAgentAsync(Guid agentId, string userId)
    {
        var entities = await GetByUserAsync(userId);
        return entities.Where(e => e.AgentId == agentId && e.MessageType == MessageType.Task && e.ModifiedDate == null)
                      .Select(e => new TaskAssignment(
                          e.Id,
                          e.AgentId ?? Guid.Empty,
                          "default",
                          DeserializeFromJson<object>(e.Content) ?? new object(),
                          1,
                          DateTime.UtcNow.AddDays(1),
                          e.Metadata
                      ));
    }

    public async Task<bool> StoreTaskResultAsync(TaskResult result, string userId)
    {
        var entity = await GetByIdAsync(result.TaskId);
        if (entity?.UserId != userId) return false;

        entity.Content = SerializeToJson(result.Result);
        entity.ModifiedDate = DateTime.UtcNow;
        return await UpdateAsync(entity);
    }

    public async Task<TaskResult?> GetTaskResultAsync(Guid taskId, string userId)
    {
        var entity = await GetByIdAsync(taskId);
        if (entity?.UserId != userId) return null;

        return new TaskResult(
            entity.Id,
            entity.AgentId ?? Guid.Empty,
            entity.ModifiedDate.HasValue ? TaskResultStatus.Completed : TaskResultStatus.Pending,
            DeserializeFromJson<object>(entity.Content),
            null,
            entity.Metadata
        );
    }

    public Task<IEnumerable<TaskAssignment>> GetTaskAssignmentsForAgentAsync(Guid agentId, string userId) =>
        GetPendingTasksForAgentAsync(agentId, userId);

    private McpMessage ConvertToMcpMessage(Message entity)
    {
        return new McpMessage(
            entity.Id,
            entity.ConversationId,
            entity.AgentId ?? Guid.Empty,
            entity.MessageType.ToString(),
            DeserializeFromJson<object>(entity.Content) ?? new object(),
            entity.Metadata,
            entity.CreatedDate
        );
    }

    // IRepository<McpMessage> bridge implementations
    async Task<McpMessage?> IRepository<McpMessage>.GetByIdAsync(Guid id, string userId)
    {
        var entity = await GetByIdAsync(id);
        return entity?.UserId == userId ? ConvertToMcpMessage(entity) : null;
    }

    async Task<IEnumerable<McpMessage>> IRepository<McpMessage>.GetAllAsync(string userId)
    {
        var entities = await GetByUserAsync(userId);
        return entities.Select(ConvertToMcpMessage);
    }

    async Task<Guid> IRepository<McpMessage>.CreateAsync(McpMessage entity, string userId)
    {
        return await StoreMessageAsync(entity, userId);
    }

    async Task<bool> IRepository<McpMessage>.UpdateAsync(McpMessage entity, string userId)
    {
        var message = new Message
        {
            Id = entity.MessageId,
            UserId = userId,
            ConversationId = entity.FromAgentId,
            AgentId = entity.ToAgentId,
            Content = SerializeToJson(entity.Payload),
            MessageType = Enum.Parse<MessageType>(entity.MessageType),
            Metadata = entity.Metadata ?? new Dictionary<string, object>(),
            CreatedDate = entity.Timestamp,
            ModifiedDate = DateTime.UtcNow
        };
        return await UpdateAsync(message);
    }

    async Task<bool> IRepository<McpMessage>.DeleteAsync(Guid id, string userId)
    {
        return await DeleteAsync(id);
    }
}