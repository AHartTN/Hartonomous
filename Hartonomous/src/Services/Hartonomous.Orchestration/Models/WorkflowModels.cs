/*
 * Copyright (c) 2024-2025 Hartonomous AI Agent Factory Platform. All Rights Reserved.
 *
 * This software is proprietary and confidential. Unauthorized copying, distribution,
 * modification, or use of this software, in whole or in part, is strictly prohibited.
 *
 * This file contains the WorkflowModels for Multi-Context Protocol (MCP) orchestration,
 * enabling complex agent workflow definitions, execution tracking, and distributed coordination.
 */

using Hartonomous.Core.Abstractions;

namespace Hartonomous.Orchestration.Models;

/// <summary>
/// Workflow definition domain model
/// </summary>
public class WorkflowDefinition : IEntityBase<Guid>
{
    // IEntityBase<Guid> properties
    public Guid Id { get; set; }
    public required string UserId { get; set; }
    public DateTime CreatedDate { get; set; } = DateTime.UtcNow;
    public DateTime? ModifiedDate { get; set; }

    // Business identifier (separate from primary key Id)
    public Guid WorkflowId { get; set; }

    // Workflow-specific properties
    public required string Name { get; set; }
    public required string Description { get; set; }
    public required string WorkflowDefinitionJson { get; set; }
    public string? Category { get; set; }
    public string? ParametersJson { get; set; }
    public string? TagsJson { get; set; }
    public int Version { get; set; }
    public WorkflowStatus Status { get; set; }

    // Navigation properties
    public virtual ICollection<WorkflowExecution> Executions { get; set; } = new List<WorkflowExecution>();
}

/// <summary>
/// Workflow execution domain model
/// </summary>
public class WorkflowExecution
{
    public Guid ExecutionId { get; set; }
    public Guid WorkflowId { get; set; }
    public required string UserId { get; set; }
    public string? ExecutionName { get; set; }
    public string? InputJson { get; set; }
    public string? OutputJson { get; set; }
    public string? ConfigurationJson { get; set; }
    public WorkflowExecutionStatus Status { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? ErrorMessage { get; set; }
    public required string StartedBy { get; set; }
    public int Priority { get; set; }
    public string? StateJson { get; set; }
    public string? MetadataJson { get; set; }

    // Navigation properties
    public virtual WorkflowDefinition? Workflow { get; set; }
    public virtual ICollection<NodeExecution> NodeExecutions { get; set; } = new List<NodeExecution>();
    public virtual ICollection<WorkflowEvent> Events { get; set; } = new List<WorkflowEvent>();
}

/// <summary>
/// Node execution within a workflow
/// </summary>
public class NodeExecution
{
    public Guid NodeExecutionId { get; set; }
    public Guid ExecutionId { get; set; }
    public required string NodeId { get; set; }
    public required string NodeType { get; set; }
    public required string NodeName { get; set; }
    public string? InputJson { get; set; }
    public string? OutputJson { get; set; }
    public NodeExecutionStatus Status { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? ErrorMessage { get; set; }
    public int RetryCount { get; set; }
    public string? MetadataJson { get; set; }

    // Navigation properties
    public virtual WorkflowExecution? Execution { get; set; }
}

/// <summary>
/// Workflow execution event for auditing and debugging
/// </summary>
public class WorkflowEvent
{
    public Guid EventId { get; set; }
    public Guid ExecutionId { get; set; }
    public required string EventType { get; set; }
    public string? NodeId { get; set; }
    public DateTime Timestamp { get; set; }
    public string? DataJson { get; set; }
    public string? Message { get; set; }
    public string? Level { get; set; }

    // Navigation properties
    public virtual WorkflowExecution? Execution { get; set; }
}

/// <summary>
/// Workflow template for reusable workflows
/// </summary>
public class WorkflowTemplate
{
    public Guid TemplateId { get; set; }
    public required string UserId { get; set; }
    public required string Name { get; set; }
    public required string Description { get; set; }
    public required string Category { get; set; }
    public required string TemplateDefinitionJson { get; set; }
    public required string ParametersJson { get; set; }
    public string? TagsJson { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public required string CreatedBy { get; set; }
    public int UsageCount { get; set; }
    public bool IsPublic { get; set; }
    public bool IsActive { get; set; }
}

/// <summary>
/// Workflow state snapshot for recovery and debugging
/// </summary>
public class WorkflowState
{
    public Guid StateId { get; set; }
    public Guid ExecutionId { get; set; }
    public required string StateJson { get; set; }
    public required string CurrentNode { get; set; }
    public string? CompletedNodesJson { get; set; }
    public string? PendingNodesJson { get; set; }
    public DateTime CreatedAt { get; set; }
    public int Version { get; set; }

    // Navigation properties
    public virtual WorkflowExecution? Execution { get; set; }
}

/// <summary>
/// Workflow breakpoint for debugging
/// </summary>
public class WorkflowBreakpoint
{
    public Guid BreakpointId { get; set; }
    public Guid ExecutionId { get; set; }
    public required string NodeId { get; set; }
    public string? Condition { get; set; }
    public bool IsEnabled { get; set; }
    public DateTime CreatedAt { get; set; }
    public required string CreatedBy { get; set; }

    // Navigation properties
    public virtual WorkflowExecution? Execution { get; set; }
}

/// <summary>
/// Workflow execution metrics for monitoring
/// </summary>
public class WorkflowExecutionMetrics
{
    public Guid MetricsId { get; set; }
    public Guid ExecutionId { get; set; }
    public string? NodeId { get; set; }
    public required string MetricName { get; set; }
    public required string MetricType { get; set; }
    public double MetricValue { get; set; }
    public string? Unit { get; set; }
    public DateTime Timestamp { get; set; }
    public string? TagsJson { get; set; }

    // Navigation properties
    public virtual WorkflowExecution? Execution { get; set; }
}

/// <summary>
/// Workflow status enumeration
/// </summary>
public enum WorkflowStatus
{
    Draft = 0,
    Active = 1,
    Inactive = 2,
    Deprecated = 3,
    Archived = 4
}

/// <summary>
/// Workflow execution status enumeration
/// </summary>
public enum WorkflowExecutionStatus
{
    Pending = 0,
    Running = 1,
    Paused = 2,
    Completed = 3,
    Failed = 4,
    Cancelled = 5,
    TimedOut = 6
}

/// <summary>
/// Node execution status enumeration
/// </summary>
public enum NodeExecutionStatus
{
    Pending = 0,
    Running = 1,
    Completed = 2,
    Failed = 3,
    Skipped = 4,
    Cancelled = 5,
    TimedOut = 6
}