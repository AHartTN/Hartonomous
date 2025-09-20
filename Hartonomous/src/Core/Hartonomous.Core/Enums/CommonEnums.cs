/*
 * Copyright (c) 2024-2025 Hartonomous AI Agent Factory Platform. All Rights Reserved.
 *
 * This software is proprietary and confidential. Unauthorized copying, distribution,
 * modification, or use of this software, in whole or in part, is strictly prohibited.
 *
 * Consolidated enums for the Hartonomous platform to eliminate duplication.
 */

namespace Hartonomous.Core.Enums;

/// <summary>
/// Agent status enumeration - consolidated from multiple definitions
/// Represents the operational status of agents in the system
/// </summary>
public enum AgentStatus
{
    // Common operational states
    Draft,
    Connecting,
    Online,
    Busy,
    Idle,
    Offline,

    // Distillation states (for DistilledAgent)
    Training,
    Testing,
    Ready,
    Deployed,
    Deprecated,

    // Error states
    Error
}

/// <summary>
/// Message type enumeration
/// Represents different types of messages in the system
/// </summary>
public enum MessageType
{
    Text,
    Command,
    Response,
    Error,
    Task,
    Heartbeat
}

/// <summary>
/// Workflow status enumeration - consolidated from multiple definitions
/// Represents the execution status of workflows
/// </summary>
public enum WorkflowStatus
{
    Draft,
    Active,
    Paused,
    Running,
    Completed,
    Failed,
    Cancelled
}

/// <summary>
/// Task result status enumeration
/// Represents the completion status of tasks
/// </summary>
public enum TaskResultStatus
{
    Pending,
    Success,
    Completed,
    Failed,
    Cancelled,
    Timeout
}