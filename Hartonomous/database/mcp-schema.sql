-- MCP (Multi-Agent Context Protocol) Schema Extension
-- This extends the existing HartonomousDB with MCP-specific tables

USE HartonomousDB;
GO

-- Agents table for MCP system
CREATE TABLE dbo.Agents (
    AgentId UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
    UserId NVARCHAR(128) NOT NULL,
    AgentName NVARCHAR(256) NOT NULL,
    AgentType NVARCHAR(128) NOT NULL,
    ConnectionId NVARCHAR(256) NOT NULL,
    Capabilities NVARCHAR(MAX) NOT NULL, -- JSON array of capabilities
    Description NVARCHAR(1000) NULL,
    Configuration NVARCHAR(MAX) NULL, -- JSON configuration object
    Status INT NOT NULL DEFAULT 1, -- 0=Connecting, 1=Online, 2=Busy, 3=Idle, 4=Offline, 5=Error
    RegisteredAt DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    LastHeartbeat DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    Metrics NVARCHAR(MAX) NULL -- JSON metrics object
);

-- MCP Messages table for agent communication
CREATE TABLE dbo.McpMessages (
    MessageId UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
    UserId NVARCHAR(128) NOT NULL,
    FromAgentId UNIQUEIDENTIFIER NOT NULL FOREIGN KEY REFERENCES dbo.Agents(AgentId),
    ToAgentId UNIQUEIDENTIFIER NULL FOREIGN KEY REFERENCES dbo.Agents(AgentId),
    MessageType NVARCHAR(128) NOT NULL,
    Payload NVARCHAR(MAX) NOT NULL, -- JSON payload
    Metadata NVARCHAR(MAX) NULL, -- JSON metadata
    Timestamp DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    ProcessedAt DATETIME2 NULL
);

-- Workflow Definitions
CREATE TABLE dbo.WorkflowDefinitions (
    WorkflowId UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
    UserId NVARCHAR(128) NOT NULL,
    WorkflowName NVARCHAR(256) NOT NULL,
    Description NVARCHAR(1000) NOT NULL,
    Steps NVARCHAR(MAX) NOT NULL, -- JSON array of workflow steps
    Parameters NVARCHAR(MAX) NULL, -- JSON parameters object
    CreatedAt DATETIME2 NOT NULL DEFAULT GETUTCDATE()
);

-- Workflow Executions
CREATE TABLE dbo.WorkflowExecutions (
    ExecutionId UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
    WorkflowId UNIQUEIDENTIFIER NOT NULL FOREIGN KEY REFERENCES dbo.WorkflowDefinitions(WorkflowId),
    ProjectId UNIQUEIDENTIFIER NOT NULL FOREIGN KEY REFERENCES dbo.Projects(ProjectId),
    UserId NVARCHAR(128) NOT NULL,
    Status INT NOT NULL DEFAULT 0, -- 0=Pending, 1=Running, 2=Completed, 3=Failed, 4=Cancelled
    StartedAt DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    CompletedAt DATETIME2 NULL,
    ErrorMessage NVARCHAR(MAX) NULL
);

-- Step Executions within workflows
CREATE TABLE dbo.StepExecutions (
    StepExecutionId UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
    ExecutionId UNIQUEIDENTIFIER NOT NULL FOREIGN KEY REFERENCES dbo.WorkflowExecutions(ExecutionId),
    StepId UNIQUEIDENTIFIER NOT NULL,
    StepName NVARCHAR(256) NOT NULL,
    AgentType NVARCHAR(128) NOT NULL,
    AssignedAgentId UNIQUEIDENTIFIER NULL FOREIGN KEY REFERENCES dbo.Agents(AgentId),
    Status INT NOT NULL DEFAULT 0, -- 0=Pending, 1=Running, 2=Completed, 3=Failed, 4=Skipped
    Input NVARCHAR(MAX) NULL, -- JSON input data
    Output NVARCHAR(MAX) NULL, -- JSON output data
    StartedAt DATETIME2 NULL,
    CompletedAt DATETIME2 NULL,
    ErrorMessage NVARCHAR(MAX) NULL
);

-- Task Assignments for agents
CREATE TABLE dbo.TaskAssignments (
    TaskId UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
    UserId NVARCHAR(128) NOT NULL,
    AgentId UNIQUEIDENTIFIER NOT NULL FOREIGN KEY REFERENCES dbo.Agents(AgentId),
    TaskType NVARCHAR(128) NOT NULL,
    TaskData NVARCHAR(MAX) NOT NULL, -- JSON task data
    Priority INT NOT NULL DEFAULT 0,
    DueDate DATETIME2 NULL,
    Metadata NVARCHAR(MAX) NULL, -- JSON metadata
    CreatedAt DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    AssignedAt DATETIME2 NULL
);

-- Task Results from agents
CREATE TABLE dbo.TaskResults (
    ResultId UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
    TaskId UNIQUEIDENTIFIER NOT NULL FOREIGN KEY REFERENCES dbo.TaskAssignments(TaskId),
    AgentId UNIQUEIDENTIFIER NOT NULL FOREIGN KEY REFERENCES dbo.Agents(AgentId),
    UserId NVARCHAR(128) NOT NULL,
    Status INT NOT NULL, -- 0=Success, 1=Failed, 2=Cancelled, 3=Timeout
    Result NVARCHAR(MAX) NULL, -- JSON result data
    ErrorMessage NVARCHAR(MAX) NULL,
    Metrics NVARCHAR(MAX) NULL, -- JSON metrics
    CompletedAt DATETIME2 NOT NULL DEFAULT GETUTCDATE()
);

-- Create indexes for performance
CREATE INDEX IX_Agents_UserId ON dbo.Agents(UserId);
CREATE INDEX IX_Agents_Status ON dbo.Agents(Status);
CREATE INDEX IX_Agents_ConnectionId ON dbo.Agents(ConnectionId);
CREATE INDEX IX_Agents_AgentType ON dbo.Agents(AgentType);

CREATE INDEX IX_McpMessages_UserId ON dbo.McpMessages(UserId);
CREATE INDEX IX_McpMessages_FromAgentId ON dbo.McpMessages(FromAgentId);
CREATE INDEX IX_McpMessages_ToAgentId ON dbo.McpMessages(ToAgentId);
CREATE INDEX IX_McpMessages_Timestamp ON dbo.McpMessages(Timestamp);

CREATE INDEX IX_WorkflowDefinitions_UserId ON dbo.WorkflowDefinitions(UserId);

CREATE INDEX IX_WorkflowExecutions_UserId ON dbo.WorkflowExecutions(UserId);
CREATE INDEX IX_WorkflowExecutions_ProjectId ON dbo.WorkflowExecutions(ProjectId);
CREATE INDEX IX_WorkflowExecutions_Status ON dbo.WorkflowExecutions(Status);

CREATE INDEX IX_StepExecutions_ExecutionId ON dbo.StepExecutions(ExecutionId);
CREATE INDEX IX_StepExecutions_Status ON dbo.StepExecutions(Status);
CREATE INDEX IX_StepExecutions_AssignedAgentId ON dbo.StepExecutions(AssignedAgentId);

CREATE INDEX IX_TaskAssignments_UserId ON dbo.TaskAssignments(UserId);
CREATE INDEX IX_TaskAssignments_AgentId ON dbo.TaskAssignments(AgentId);
CREATE INDEX IX_TaskAssignments_DueDate ON dbo.TaskAssignments(DueDate);

CREATE INDEX IX_TaskResults_TaskId ON dbo.TaskResults(TaskId);
CREATE INDEX IX_TaskResults_UserId ON dbo.TaskResults(UserId);
GO