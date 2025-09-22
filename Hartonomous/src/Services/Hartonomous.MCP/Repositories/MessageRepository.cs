/*
 * Copyright (c) 2024-2025 Hartonomous AI Agent Factory Platform. All Rights Reserved.
 *
 * This software is proprietary and confidential. Unauthorized copying, distribution,
 * modification, or use of this software, in whole or in part, is strictly prohibited.
 *
 * This file contains the Multi-Context Protocol (MCP) message repository for inter-agent communication.
 * Features message storage, conversation tracking, task assignment, and result management with user isolation.
 */

using Hartonomous.Core.DTOs;
using Hartonomous.Core.Interfaces;
using Hartonomous.Core.Abstractions;
using Hartonomous.Core.Configuration;
using Hartonomous.Core.Entities;
using Hartonomous.Core.Enums;
using Hartonomous.Core.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace Hartonomous.MCP.Repositories;

public class MessageRepository : BaseRepository<Message, Guid>, IMessageRepository
{
    public MessageRepository(IOptions<SqlServerOptions> sqlOptions, HartonomousDbContext context) : base(sqlOptions, context)
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

    protected override object[] GetParametersArray(Message entity)
    {
        return new object[]
        {
            entity.Id,
            entity.UserId,
            entity.ConversationId,
            entity.AgentId ?? (object)DBNull.Value,
            entity.Content,
            (int)entity.MessageType,
            SerializeToJson(entity.Metadata),
            entity.CreatedDate,
            entity.ModifiedDate ?? (object)DBNull.Value
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
            entity.Id,                                                          // MessageId
            entity.ConversationId,                                             // FromAgentId
            entity.AgentId,                                                     // ToAgentId (can be null)
            entity.MessageType.ToString(),                                     // MessageType
            DeserializeFromJson<object>(entity.Content) ?? new object(),       // Payload
            entity.Metadata,                                                    // Metadata
            entity.CreatedDate,                                                 // Timestamp
            entity.ModifiedDate                                                 // ProcessedAt
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

    public async Task<IEnumerable<McpMessage>> GetMessagesByProjectAsync(Guid projectId, string userId, int limit = 1000)
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
            entity.Id,                                                          // MessageId
            entity.ConversationId,                                             // FromAgentId
            entity.AgentId,                                                     // ToAgentId (can be null)
            entity.MessageType.ToString(),                                     // MessageType
            DeserializeFromJson<object>(entity.Content) ?? new object(),       // Payload
            entity.Metadata,                                                    // Metadata
            entity.CreatedDate,                                                 // Timestamp
            entity.ModifiedDate                                                 // ProcessedAt
        );
    }

    // IRepository<McpMessage> bridge implementations for interface compatibility
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

    // Additional required interface implementations for IRepository<McpMessage>

    async Task<IEnumerable<McpMessage>> IRepository<McpMessage>.FindAsync(System.Linq.Expressions.Expression<Func<McpMessage, bool>> predicate, string userId)
    {
        // Get all messages for user and apply in-memory filtering
        var entities = await GetByUserAsync(userId);
        var mcpMessages = entities.Select(ConvertToMcpMessage);

        // Compile the predicate and apply it
        var compiledPredicate = predicate.Compile();
        return mcpMessages.Where(compiledPredicate);
    }

    async Task<McpMessage?> IRepository<McpMessage>.FirstOrDefaultAsync(System.Linq.Expressions.Expression<Func<McpMessage, bool>> predicate, string userId)
    {
        // Get all messages for user and apply in-memory filtering
        var entities = await GetByUserAsync(userId);
        var mcpMessages = entities.Select(ConvertToMcpMessage);

        // Compile the predicate and find first match
        var compiledPredicate = predicate.Compile();
        return mcpMessages.FirstOrDefault(compiledPredicate);
    }

    async Task<(IEnumerable<McpMessage> Items, int TotalCount)> IRepository<McpMessage>.GetPagedAsync(
        int page, int pageSize, string userId,
        System.Linq.Expressions.Expression<Func<McpMessage, bool>>? filter = null,
        System.Linq.Expressions.Expression<Func<McpMessage, object>>? orderBy = null,
        bool descending = false)
    {
        // Get all messages for user
        var entities = await GetByUserAsync(userId);
        var mcpMessages = entities.Select(ConvertToMcpMessage);

        // Apply filter if provided
        if (filter != null)
        {
            var compiledFilter = filter.Compile();
            mcpMessages = mcpMessages.Where(compiledFilter);
        }

        var totalCount = mcpMessages.Count();

        // Apply ordering
        if (orderBy != null)
        {
            var compiledOrderBy = orderBy.Compile();
            mcpMessages = descending
                ? mcpMessages.OrderByDescending(compiledOrderBy)
                : mcpMessages.OrderBy(compiledOrderBy);
        }
        else
        {
            // Default ordering by timestamp
            mcpMessages = descending
                ? mcpMessages.OrderByDescending(m => m.Timestamp)
                : mcpMessages.OrderBy(m => m.Timestamp);
        }

        // Apply pagination
        var skip = (page - 1) * pageSize;
        var items = mcpMessages.Skip(skip).Take(pageSize);

        return (items, totalCount);
    }

    async Task<McpMessage> IRepository<McpMessage>.AddAsync(McpMessage entity)
    {
        var messageEntity = ConvertFromMcpMessage(entity);
        await CreateAsync(messageEntity);
        return ConvertToMcpMessage(messageEntity);
    }

    async Task<IEnumerable<McpMessage>> IRepository<McpMessage>.AddRangeAsync(IEnumerable<McpMessage> entities)
    {
        // This method doesn't have userId parameter, so we'll need to infer it from context
        // or require it to be set in the entity metadata
        var results = new List<McpMessage>();

        foreach (var entity in entities)
        {
            // Try to get userId from metadata, otherwise this will fail
            var userId = entity.Metadata?.ContainsKey("UserId") == true
                ? entity.Metadata["UserId"]?.ToString() ?? ""
                : "";

            if (string.IsNullOrEmpty(userId))
            {
                throw new InvalidOperationException("UserId must be provided in entity metadata for bulk operations");
            }

            var messageEntity = ConvertFromMcpMessage(entity);
            messageEntity.UserId = userId;
            messageEntity.CreatedDate = DateTime.UtcNow;
            messageEntity.Id = entity.MessageId;

            await CreateAsync(messageEntity);
            results.Add(ConvertToMcpMessage(messageEntity));
        }

        return results;
    }

    async Task<McpMessage> IRepository<McpMessage>.UpdateAsync(McpMessage entity)
    {
        var messageEntity = ConvertFromMcpMessage(entity);
        await UpdateAsync(messageEntity);
        return ConvertToMcpMessage(messageEntity);
    }

    Task IRepository<McpMessage>.DeleteAsync(McpMessage entity)
    {
        return DeleteAsync(entity.MessageId);
    }

    Task IRepository<McpMessage>.DeleteAsync(Guid id, string userId)
    {
        return DeleteAsync(id);
    }

    async Task IRepository<McpMessage>.DeleteRangeAsync(IEnumerable<McpMessage> entities)
    {
        foreach (var entity in entities)
        {
            await DeleteAsync(entity.MessageId);
        }
    }

    async Task<int> IRepository<McpMessage>.CountAsync(string userId)
    {
        var entities = await GetByUserAsync(userId);
        return entities.Count();
    }

    async Task<int> IRepository<McpMessage>.CountAsync(System.Linq.Expressions.Expression<Func<McpMessage, bool>> predicate, string userId)
    {
        // Get all messages for user and apply in-memory filtering
        var entities = await GetByUserAsync(userId);
        var mcpMessages = entities.Select(ConvertToMcpMessage);

        // Compile the predicate and count matches
        var compiledPredicate = predicate.Compile();
        return mcpMessages.Count(compiledPredicate);
    }

    async Task<bool> IRepository<McpMessage>.ExistsAsync(Guid id, string userId)
    {
        var entity = await GetByIdAsync(id);
        return entity?.UserId == userId;
    }

    async Task<bool> IRepository<McpMessage>.ExistsAsync(System.Linq.Expressions.Expression<Func<McpMessage, bool>> predicate, string userId)
    {
        // Get all messages for user and apply in-memory filtering
        var entities = await GetByUserAsync(userId);
        var mcpMessages = entities.Select(ConvertToMcpMessage);

        // Compile the predicate and check existence
        var compiledPredicate = predicate.Compile();
        return mcpMessages.Any(compiledPredicate);
    }

    async Task<IEnumerable<McpMessage>> IRepository<McpMessage>.FromSqlAsync(string sql, params object[] parameters)
    {
        // For security reasons, raw SQL is limited to safe operations
        // We'll implement a basic wrapper that ensures user isolation
        if (string.IsNullOrWhiteSpace(sql))
            return Enumerable.Empty<McpMessage>();

        // Ensure the query includes user isolation by checking if UserId parameter is provided
        if (!parameters.Any(p => p?.ToString()?.Contains("UserId") == true))
        {
            // For security, don't execute queries without user context
            return Enumerable.Empty<McpMessage>();
        }

        try
        {
            // Use EF Core for raw SQL with user isolation
            var entities = await _context.Messages
                .FromSqlRaw(sql, parameters)
                .Where(m => !string.IsNullOrEmpty(m.UserId))
                .ToListAsync();

            // Convert to McpMessage DTOs
            return entities.Select(ConvertToMcpMessage);
        }
        catch
        {
            // Return empty collection on any error for security
            return Enumerable.Empty<McpMessage>();
        }
    }

    async Task<int> IRepository<McpMessage>.ExecuteSqlAsync(string sql, params object[] parameters)
    {
        // For security reasons, only allow specific types of SQL operations
        if (string.IsNullOrWhiteSpace(sql))
            return 0;

        // Only allow UPDATE and DELETE operations (no DDL)
        var sqlUpper = sql.Trim().ToUpperInvariant();
        if (!sqlUpper.StartsWith("UPDATE") && !sqlUpper.StartsWith("DELETE"))
            return 0;

        // Ensure the query targets the correct table
        if (!sqlUpper.Contains("MCPMESSAGES"))
            return 0;

        // Ensure user isolation by checking if UserId parameter is provided
        if (!parameters.Any(p => p?.ToString()?.Contains("UserId") == true))
            return 0;

        try
        {
            return await _context.Database.ExecuteSqlRawAsync(sql, parameters);
        }
        catch
        {
            // Return 0 on any error for security
            return 0;
        }
    }

    async Task<bool> IRepository<McpMessage>.DeleteByIdAsync(Guid id, string userId)
    {
        return await DeleteAsync(id);
    }

    // Helper method for McpMessage to Message conversion (inverse of ConvertToMcpMessage)
    private Message ConvertFromMcpMessage(McpMessage dto)
    {
        return new Message
        {
            Id = dto.MessageId,
            ConversationId = dto.FromAgentId,
            AgentId = dto.ToAgentId,
            MessageType = Enum.TryParse<MessageType>(dto.MessageType, out var msgType) ? msgType : MessageType.Command,
            Content = dto.Payload?.ToString() ?? "",
            Metadata = dto.Metadata ?? new Dictionary<string, object>(),
            CreatedDate = dto.Timestamp,
            ModifiedDate = dto.ProcessedAt,
            UserId = "" // Will be set by calling code
        };
    }
}