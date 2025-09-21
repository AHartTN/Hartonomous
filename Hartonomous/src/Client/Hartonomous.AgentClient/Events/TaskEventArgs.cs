/*
 * Copyright (c) 2024-2025 Hartonomous AI Agent Factory Platform. All Rights Reserved.
 *
 * This file contains the event argument classes for task execution events.
 */
using System;
using Hartonomous.AgentClient.Models;

namespace Hartonomous.AgentClient.Events
{
    /// <summary>
    /// Base class for task-related event arguments.
    /// </summary>
    public class TaskEventArgs : EventArgs
    {
        public string TaskId { get; }
        public DateTimeOffset Timestamp { get; }

        public TaskEventArgs(string taskId)
        {
            TaskId = taskId;
            Timestamp = DateTimeOffset.UtcNow;
        }
    }

    /// <summary>
    /// Event arguments for when a task's state changes.
    /// </summary>
    public class TaskStateChangeEventArgs : TaskEventArgs
    {
        public Models.TaskStatus OldStatus { get; }
        public Models.TaskStatus NewStatus { get; }
        public string? Message { get; }

        public TaskStateChangeEventArgs(string taskId, Models.TaskStatus oldStatus, Models.TaskStatus newStatus, string? message = null)
            : base(taskId)
        {
            OldStatus = oldStatus;
            NewStatus = newStatus;
            Message = message;
        }
    }

    /// <summary>
    /// Event arguments for task progress updates.
    /// </summary>
    public class TaskProgressEventArgs : TaskEventArgs
    {
        public int ProgressPercentage { get; }
        public string? Message { get; }

        public TaskProgressEventArgs(string taskId, int progressPercentage, string? message = null)
            : base(taskId)
        {
            ProgressPercentage = progressPercentage;
            Message = message;
        }
    }

    /// <summary>
    /// Event arguments for when a task completes successfully.
    /// </summary>
    public class TaskResultEventArgs : TaskEventArgs
    {
        public TaskResult Result { get; }

        public TaskResultEventArgs(string taskId, TaskResult result)
            : base(taskId)
        {
            Result = result;
        }
    }

    /// <summary>
    /// Event arguments for when a task fails.
    /// </summary>
    public class TaskErrorEventArgs : TaskEventArgs
    {
        public Exception Exception { get; }
        public string ErrorMessage { get; }

        public TaskErrorEventArgs(string taskId, Exception exception)
            : base(taskId)
        {
            Exception = exception;
            ErrorMessage = exception.Message;
        }
    }
}
