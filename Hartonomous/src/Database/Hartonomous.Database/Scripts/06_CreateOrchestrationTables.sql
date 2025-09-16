-- Module 6: Orchestration Service Database Schema
-- Create tables for workflow orchestration functionality

-- Workflow Definitions Table
CREATE TABLE dbo.WorkflowDefinitions (
    WorkflowId UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
    UserId NVARCHAR(256) NOT NULL,
    Name NVARCHAR(256) NOT NULL,
    Description NVARCHAR(1000) NOT NULL,
    WorkflowDefinitionJson NVARCHAR(MAX) NOT NULL,
    Category NVARCHAR(100) NULL,
    ParametersJson NVARCHAR(MAX) NULL,
    TagsJson NVARCHAR(MAX) NULL,
    CreatedAt DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    UpdatedAt DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    CreatedBy NVARCHAR(256) NOT NULL,
    Version INT NOT NULL DEFAULT 1,
    Status INT NOT NULL DEFAULT 0, -- 0=Draft, 1=Active, 2=Inactive, 3=Deprecated, 4=Archived

    INDEX IX_WorkflowDefinitions_UserId (UserId),
    INDEX IX_WorkflowDefinitions_Status (Status),
    INDEX IX_WorkflowDefinitions_Category (Category),
    INDEX IX_WorkflowDefinitions_CreatedAt (CreatedAt),
    INDEX IX_WorkflowDefinitions_Name_UserId (Name, UserId)
);

-- Workflow Executions Table
CREATE TABLE dbo.WorkflowExecutions (
    ExecutionId UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
    WorkflowId UNIQUEIDENTIFIER NOT NULL,
    UserId NVARCHAR(256) NOT NULL,
    ExecutionName NVARCHAR(256) NULL,
    InputJson NVARCHAR(MAX) NULL,
    OutputJson NVARCHAR(MAX) NULL,
    ConfigurationJson NVARCHAR(MAX) NULL,
    Status INT NOT NULL DEFAULT 0, -- 0=Pending, 1=Running, 2=Paused, 3=Completed, 4=Failed, 5=Cancelled, 6=TimedOut
    StartedAt DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    CompletedAt DATETIME2 NULL,
    ErrorMessage NVARCHAR(MAX) NULL,
    StartedBy NVARCHAR(256) NOT NULL,
    Priority INT NOT NULL DEFAULT 0,
    StateJson NVARCHAR(MAX) NULL,
    MetadataJson NVARCHAR(MAX) NULL,

    FOREIGN KEY (WorkflowId) REFERENCES dbo.WorkflowDefinitions(WorkflowId) ON DELETE CASCADE,
    INDEX IX_WorkflowExecutions_WorkflowId (WorkflowId),
    INDEX IX_WorkflowExecutions_UserId (UserId),
    INDEX IX_WorkflowExecutions_Status (Status),
    INDEX IX_WorkflowExecutions_StartedAt (StartedAt),
    INDEX IX_WorkflowExecutions_CompletedAt (CompletedAt),
    INDEX IX_WorkflowExecutions_Priority (Priority),
    INDEX IX_WorkflowExecutions_UserId_Status (UserId, Status)
);

-- Node Executions Table (for tracking individual node execution within workflows)
CREATE TABLE dbo.NodeExecutions (
    NodeExecutionId UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
    ExecutionId UNIQUEIDENTIFIER NOT NULL,
    NodeId NVARCHAR(256) NOT NULL,
    NodeType NVARCHAR(100) NOT NULL,
    NodeName NVARCHAR(256) NOT NULL,
    InputJson NVARCHAR(MAX) NULL,
    OutputJson NVARCHAR(MAX) NULL,
    Status INT NOT NULL DEFAULT 0, -- 0=Pending, 1=Running, 2=Completed, 3=Failed, 4=Skipped, 5=Cancelled, 6=TimedOut
    StartedAt DATETIME2 NULL,
    CompletedAt DATETIME2 NULL,
    ErrorMessage NVARCHAR(MAX) NULL,
    RetryCount INT NOT NULL DEFAULT 0,
    MetadataJson NVARCHAR(MAX) NULL,

    FOREIGN KEY (ExecutionId) REFERENCES dbo.WorkflowExecutions(ExecutionId) ON DELETE CASCADE,
    INDEX IX_NodeExecutions_ExecutionId (ExecutionId),
    INDEX IX_NodeExecutions_NodeId (NodeId),
    INDEX IX_NodeExecutions_Status (Status),
    INDEX IX_NodeExecutions_StartedAt (StartedAt),
    INDEX IX_NodeExecutions_ExecutionId_NodeId (ExecutionId, NodeId)
);

-- Workflow Events Table (for auditing and debugging)
CREATE TABLE dbo.WorkflowEvents (
    EventId UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
    ExecutionId UNIQUEIDENTIFIER NOT NULL,
    EventType NVARCHAR(100) NOT NULL,
    NodeId NVARCHAR(256) NULL,
    Timestamp DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    DataJson NVARCHAR(MAX) NULL,
    Message NVARCHAR(MAX) NULL,
    Level NVARCHAR(50) NOT NULL DEFAULT 'Info', -- Info, Warning, Error, Debug

    FOREIGN KEY (ExecutionId) REFERENCES dbo.WorkflowExecutions(ExecutionId) ON DELETE CASCADE,
    INDEX IX_WorkflowEvents_ExecutionId (ExecutionId),
    INDEX IX_WorkflowEvents_EventType (EventType),
    INDEX IX_WorkflowEvents_Timestamp (Timestamp),
    INDEX IX_WorkflowEvents_Level (Level),
    INDEX IX_WorkflowEvents_ExecutionId_Timestamp (ExecutionId, Timestamp)
);

-- Workflow Templates Table (for reusable workflow templates)
CREATE TABLE dbo.WorkflowTemplates (
    TemplateId UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
    UserId NVARCHAR(256) NOT NULL,
    Name NVARCHAR(256) NOT NULL,
    Description NVARCHAR(1000) NOT NULL,
    Category NVARCHAR(100) NOT NULL,
    TemplateDefinitionJson NVARCHAR(MAX) NOT NULL,
    ParametersJson NVARCHAR(MAX) NOT NULL,
    TagsJson NVARCHAR(MAX) NULL,
    CreatedAt DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    UpdatedAt DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    CreatedBy NVARCHAR(256) NOT NULL,
    UsageCount INT NOT NULL DEFAULT 0,
    IsPublic BIT NOT NULL DEFAULT 0,
    IsActive BIT NOT NULL DEFAULT 1,

    INDEX IX_WorkflowTemplates_UserId (UserId),
    INDEX IX_WorkflowTemplates_Category (Category),
    INDEX IX_WorkflowTemplates_IsPublic (IsPublic),
    INDEX IX_WorkflowTemplates_IsActive (IsActive),
    INDEX IX_WorkflowTemplates_UsageCount (UsageCount DESC),
    INDEX IX_WorkflowTemplates_Name_UserId (Name, UserId)
);

-- Workflow States Table (for state snapshots and recovery)
CREATE TABLE dbo.WorkflowStates (
    StateId UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
    ExecutionId UNIQUEIDENTIFIER NOT NULL,
    StateJson NVARCHAR(MAX) NOT NULL,
    CurrentNode NVARCHAR(256) NOT NULL,
    CompletedNodesJson NVARCHAR(MAX) NULL,
    PendingNodesJson NVARCHAR(MAX) NULL,
    CreatedAt DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    Version INT NOT NULL DEFAULT 1,

    FOREIGN KEY (ExecutionId) REFERENCES dbo.WorkflowExecutions(ExecutionId) ON DELETE CASCADE,
    INDEX IX_WorkflowStates_ExecutionId (ExecutionId),
    INDEX IX_WorkflowStates_CreatedAt (CreatedAt),
    INDEX IX_WorkflowStates_Version (Version),
    INDEX IX_WorkflowStates_ExecutionId_Version (ExecutionId, Version)
);

-- Workflow Breakpoints Table (for debugging)
CREATE TABLE dbo.WorkflowBreakpoints (
    BreakpointId UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
    ExecutionId UNIQUEIDENTIFIER NOT NULL,
    NodeId NVARCHAR(256) NOT NULL,
    Condition NVARCHAR(MAX) NULL,
    IsEnabled BIT NOT NULL DEFAULT 1,
    CreatedAt DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    CreatedBy NVARCHAR(256) NOT NULL,

    FOREIGN KEY (ExecutionId) REFERENCES dbo.WorkflowExecutions(ExecutionId) ON DELETE CASCADE,
    INDEX IX_WorkflowBreakpoints_ExecutionId (ExecutionId),
    INDEX IX_WorkflowBreakpoints_NodeId (NodeId),
    INDEX IX_WorkflowBreakpoints_IsEnabled (IsEnabled),
    INDEX IX_WorkflowBreakpoints_CreatedBy (CreatedBy)
);

-- Workflow Execution Metrics Table (for monitoring and performance tracking)
CREATE TABLE dbo.WorkflowExecutionMetrics (
    MetricsId UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
    ExecutionId UNIQUEIDENTIFIER NOT NULL,
    NodeId NVARCHAR(256) NULL,
    MetricName NVARCHAR(256) NOT NULL,
    MetricType NVARCHAR(100) NOT NULL, -- gauge, counter, histogram, timer
    MetricValue FLOAT NOT NULL,
    Unit NVARCHAR(50) NULL,
    Timestamp DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    TagsJson NVARCHAR(MAX) NULL,

    FOREIGN KEY (ExecutionId) REFERENCES dbo.WorkflowExecutions(ExecutionId) ON DELETE CASCADE,
    INDEX IX_WorkflowExecutionMetrics_ExecutionId (ExecutionId),
    INDEX IX_WorkflowExecutionMetrics_MetricName (MetricName),
    INDEX IX_WorkflowExecutionMetrics_Timestamp (Timestamp),
    INDEX IX_WorkflowExecutionMetrics_MetricType (MetricType),
    INDEX IX_WorkflowExecutionMetrics_NodeId (NodeId)
);

-- Create views for common queries
GO

-- View for active workflow executions with workflow details
CREATE VIEW dbo.vw_ActiveWorkflowExecutions
AS
SELECT
    e.ExecutionId,
    e.WorkflowId,
    w.Name AS WorkflowName,
    w.Category AS WorkflowCategory,
    e.ExecutionName,
    e.Status,
    e.StartedAt,
    e.StartedBy,
    e.Priority,
    e.UserId,
    DATEDIFF(SECOND, e.StartedAt, GETUTCDATE()) AS RuntimeSeconds
FROM dbo.WorkflowExecutions e
INNER JOIN dbo.WorkflowDefinitions w ON e.WorkflowId = w.WorkflowId
WHERE e.Status IN (0, 1, 2) -- Pending, Running, Paused
AND e.CompletedAt IS NULL;

GO

-- View for workflow execution statistics
CREATE VIEW dbo.vw_WorkflowExecutionStats
AS
SELECT
    w.WorkflowId,
    w.Name AS WorkflowName,
    w.Category,
    w.UserId,
    COUNT(e.ExecutionId) AS TotalExecutions,
    SUM(CASE WHEN e.Status = 3 THEN 1 ELSE 0 END) AS SuccessfulExecutions,
    SUM(CASE WHEN e.Status = 4 THEN 1 ELSE 0 END) AS FailedExecutions,
    SUM(CASE WHEN e.Status IN (0, 1, 2) THEN 1 ELSE 0 END) AS ActiveExecutions,
    CASE
        WHEN COUNT(e.ExecutionId) > 0
        THEN ROUND(SUM(CASE WHEN e.Status = 3 THEN 1.0 ELSE 0 END) / COUNT(e.ExecutionId) * 100, 2)
        ELSE 0
    END AS SuccessRate,
    AVG(CASE
        WHEN e.CompletedAt IS NOT NULL
        THEN DATEDIFF(SECOND, e.StartedAt, e.CompletedAt)
        ELSE NULL
    END) AS AvgExecutionTimeSeconds,
    MAX(e.StartedAt) AS LastExecutionTime
FROM dbo.WorkflowDefinitions w
LEFT JOIN dbo.WorkflowExecutions e ON w.WorkflowId = e.WorkflowId
GROUP BY w.WorkflowId, w.Name, w.Category, w.UserId;

GO

-- View for node execution performance
CREATE VIEW dbo.vw_NodeExecutionPerformance
AS
SELECT
    ne.NodeType,
    ne.NodeId,
    COUNT(*) AS TotalExecutions,
    SUM(CASE WHEN ne.Status = 2 THEN 1 ELSE 0 END) AS SuccessfulExecutions,
    SUM(CASE WHEN ne.Status = 3 THEN 1 ELSE 0 END) AS FailedExecutions,
    AVG(CASE
        WHEN ne.CompletedAt IS NOT NULL
        THEN DATEDIFF(MILLISECOND, ne.StartedAt, ne.CompletedAt)
        ELSE NULL
    END) AS AvgExecutionTimeMs,
    AVG(CAST(ne.RetryCount AS FLOAT)) AS AvgRetryCount
FROM dbo.NodeExecutions ne
WHERE ne.StartedAt IS NOT NULL
GROUP BY ne.NodeType, ne.NodeId;

GO

-- Create stored procedures for common operations
CREATE PROCEDURE dbo.sp_GetUserWorkflowDashboard
    @UserId NVARCHAR(256)
AS
BEGIN
    SET NOCOUNT ON;

    -- Get summary statistics
    SELECT
        COUNT(DISTINCT w.WorkflowId) AS TotalWorkflows,
        SUM(CASE WHEN w.Status = 1 THEN 1 ELSE 0 END) AS ActiveWorkflows,
        COUNT(DISTINCT e.ExecutionId) AS TotalExecutions,
        SUM(CASE WHEN e.Status IN (0, 1, 2) THEN 1 ELSE 0 END) AS ActiveExecutions,
        SUM(CASE WHEN e.Status = 3 THEN 1 ELSE 0 END) AS SuccessfulExecutions,
        SUM(CASE WHEN e.Status = 4 THEN 1 ELSE 0 END) AS FailedExecutions
    FROM dbo.WorkflowDefinitions w
    LEFT JOIN dbo.WorkflowExecutions e ON w.WorkflowId = e.WorkflowId
    WHERE w.UserId = @UserId;

    -- Get recent executions
    SELECT TOP 10
        e.ExecutionId,
        e.WorkflowId,
        w.Name AS WorkflowName,
        e.ExecutionName,
        e.Status,
        e.StartedAt,
        e.CompletedAt,
        CASE
            WHEN e.CompletedAt IS NOT NULL
            THEN DATEDIFF(SECOND, e.StartedAt, e.CompletedAt)
            ELSE DATEDIFF(SECOND, e.StartedAt, GETUTCDATE())
        END AS DurationSeconds
    FROM dbo.WorkflowExecutions e
    INNER JOIN dbo.WorkflowDefinitions w ON e.WorkflowId = w.WorkflowId
    WHERE e.UserId = @UserId
    ORDER BY e.StartedAt DESC;

    -- Get workflows by category
    SELECT
        ISNULL(w.Category, 'Uncategorized') AS Category,
        COUNT(*) AS WorkflowCount
    FROM dbo.WorkflowDefinitions w
    WHERE w.UserId = @UserId
    GROUP BY w.Category;
END;

GO

-- Create procedure to clean up old workflow data
CREATE PROCEDURE dbo.sp_CleanupWorkflowData
    @RetentionDays INT = 30
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @CutoffDate DATETIME2 = DATEADD(DAY, -@RetentionDays, GETUTCDATE());

    -- Delete old workflow states (cascading deletes will handle related records)
    DELETE FROM dbo.WorkflowStates
    WHERE CreatedAt < @CutoffDate
    AND ExecutionId IN (
        SELECT ExecutionId
        FROM dbo.WorkflowExecutions
        WHERE CompletedAt < @CutoffDate
        AND Status IN (3, 4, 5) -- Completed, Failed, Cancelled
    );

    -- Delete old workflow events
    DELETE FROM dbo.WorkflowEvents
    WHERE Timestamp < @CutoffDate
    AND ExecutionId IN (
        SELECT ExecutionId
        FROM dbo.WorkflowExecutions
        WHERE CompletedAt < @CutoffDate
        AND Status IN (3, 4, 5)
    );

    -- Delete old execution metrics
    DELETE FROM dbo.WorkflowExecutionMetrics
    WHERE Timestamp < @CutoffDate
    AND ExecutionId IN (
        SELECT ExecutionId
        FROM dbo.WorkflowExecutions
        WHERE CompletedAt < @CutoffDate
        AND Status IN (3, 4, 5)
    );

    SELECT @@ROWCOUNT AS RecordsDeleted;
END;

GO

-- Create function to calculate workflow health score
CREATE FUNCTION dbo.fn_CalculateWorkflowHealthScore(@WorkflowId UNIQUEIDENTIFIER)
RETURNS FLOAT
AS
BEGIN
    DECLARE @HealthScore FLOAT = 100;
    DECLARE @RecentExecutions INT;
    DECLARE @SuccessRate FLOAT;
    DECLARE @RecentFailures INT;

    -- Get recent execution data (last 30 days)
    SELECT
        @RecentExecutions = COUNT(*),
        @SuccessRate = CASE
            WHEN COUNT(*) > 0
            THEN SUM(CASE WHEN Status = 3 THEN 1.0 ELSE 0 END) / COUNT(*) * 100
            ELSE 100
        END,
        @RecentFailures = SUM(CASE WHEN Status = 4 AND StartedAt > DATEADD(DAY, -1, GETUTCDATE()) THEN 1 ELSE 0 END)
    FROM dbo.WorkflowExecutions
    WHERE WorkflowId = @WorkflowId
    AND StartedAt > DATEADD(DAY, -30, GETUTCDATE());

    -- Adjust health score based on success rate
    SET @HealthScore = @SuccessRate;

    -- Penalize for recent failures
    IF @RecentFailures > 5
        SET @HealthScore = @HealthScore - 20;
    IF @RecentFailures > 10
        SET @HealthScore = @HealthScore - 30;

    -- Ensure score is between 0 and 100
    SET @HealthScore = CASE
        WHEN @HealthScore < 0 THEN 0
        WHEN @HealthScore > 100 THEN 100
        ELSE @HealthScore
    END;

    RETURN @HealthScore;
END;

GO

PRINT 'Orchestration Service database schema created successfully';