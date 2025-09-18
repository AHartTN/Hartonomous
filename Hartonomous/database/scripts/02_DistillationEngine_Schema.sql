-- Hartonomous Distillation Engine Database Schema
-- Enhanced schema supporting model distillation, circuit mapping, and agent state management

USE HartonomousDB;
GO

-- Set required options for schema operations
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

-- Create master key if it doesn't exist
IF NOT EXISTS (SELECT * FROM sys.symmetric_keys WHERE name = 'HartonomousDB_MasterKey')
BEGIN
    CREATE MASTER KEY ENCRYPTION BY PASSWORD = 'H@rt0n0m0us2025!';
END
GO

-- Enable SQL Server 2025 REST API integration
EXEC sp_configure 'external rest endpoint enabled', 1;
RECONFIGURE;
GO

-- Create filegroups for FILESTREAM data organization
ALTER DATABASE HartonomousDB
ADD FILEGROUP ActivationDataGroup CONTAINS FILESTREAM;

ALTER DATABASE HartonomousDB
ADD FILEGROUP ModelDataGroup CONTAINS FILESTREAM;

-- Add FILESTREAM data files
ALTER DATABASE HartonomousDB
ADD FILE (
    NAME = 'ActivationData_FS',
    FILENAME = 'D:\HartonomousData\ActivationData'
) TO FILEGROUP ActivationDataGroup;

ALTER DATABASE HartonomousDB
ADD FILE (
    NAME = 'ModelData_FS',
    FILENAME = 'D:\HartonomousData\ModelData'
) TO FILEGROUP ModelDataGroup;
GO

-- Foundation Models table with FILESTREAM support
CREATE TABLE dbo.FoundationModels (
    ModelId INT IDENTITY(1,1) PRIMARY KEY,
    ModelName NVARCHAR(255) NOT NULL UNIQUE,
    ModelFamily NVARCHAR(100) NOT NULL, -- 'Llama', 'Mistral', 'Gemma', etc.
    ParameterCount BIGINT NOT NULL,
    OriginalSize BIGINT NOT NULL, -- Size in bytes
    QuantizationMethod NVARCHAR(50), -- 'Q5_K_M', 'Q4_K_M', etc.
    GGUF_File VARBINARY(MAX) FILESTREAM NOT NULL,
    GGUF_FileId UNIQUEIDENTIFIER ROWGUIDCOL NOT NULL UNIQUE DEFAULT NEWID(),
    CreatedDate DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    CreatedBy NVARCHAR(128) NOT NULL,
    IsActive BIT NOT NULL DEFAULT 1,

    INDEX IX_FoundationModels_Family (ModelFamily),
    INDEX IX_FoundationModels_CreatedBy (CreatedBy)
) FILESTREAM_ON ModelDataGroup;
GO

-- Distillation Projects - tracks the overall distillation process
CREATE TABLE dbo.DistillationProjects (
    ProjectId INT IDENTITY(1,1) PRIMARY KEY,
    ProjectName NVARCHAR(255) NOT NULL,
    Description NVARCHAR(MAX),
    SourceModelId INT NOT NULL FOREIGN KEY REFERENCES dbo.FoundationModels(ModelId),
    TargetDomain NVARCHAR(100) NOT NULL, -- 'medical', 'legal', 'chess', etc.
    UserId NVARCHAR(128) NOT NULL,
    Status NVARCHAR(50) NOT NULL DEFAULT 'Created', -- 'Created', 'Analyzing', 'Distilling', 'Completed', 'Failed'
    CreatedDate DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    CompletedDate DATETIME2 NULL,

    INDEX IX_DistillationProjects_UserId (UserId),
    INDEX IX_DistillationProjects_Status (Status)
);
GO

-- Training Datasets for distillation
CREATE TABLE dbo.TrainingDatasets (
    DatasetId INT IDENTITY(1,1) PRIMARY KEY,
    DatasetName NVARCHAR(255) NOT NULL,
    Description NVARCHAR(MAX),
    Domain NVARCHAR(100) NOT NULL,
    DatasetContent VARBINARY(MAX) FILESTREAM NOT NULL,
    DatasetContentId UNIQUEIDENTIFIER ROWGUIDCOL NOT NULL UNIQUE DEFAULT NEWID(),
    SampleCount INT NOT NULL,
    TotalTokens BIGINT,
    UserId NVARCHAR(128) NOT NULL,
    CreatedDate DATETIME2 NOT NULL DEFAULT GETUTCDATE(),

    INDEX IX_TrainingDatasets_Domain (Domain),
    INDEX IX_TrainingDatasets_UserId (UserId)
) FILESTREAM_ON ActivationDataGroup;
GO

-- Activation Capture Sessions - tracks model inference runs for analysis
CREATE TABLE dbo.ActivationCaptureSessions (
    SessionId BIGINT IDENTITY(1,1) PRIMARY KEY,
    ProjectId INT NOT NULL FOREIGN KEY REFERENCES dbo.DistillationProjects(ProjectId),
    ModelId INT NOT NULL FOREIGN KEY REFERENCES dbo.FoundationModels(ModelId),
    DatasetId INT NOT NULL FOREIGN KEY REFERENCES dbo.TrainingDatasets(DatasetId),
    TargetLayers NVARCHAR(500), -- JSON array of layer indices [12, 24, 36]
    SessionStatus NVARCHAR(50) NOT NULL DEFAULT 'Started', -- 'Started', 'Processing', 'Completed', 'Failed'
    StartTime DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    EndTime DATETIME2 NULL,
    TotalSamples INT DEFAULT 0,
    ProcessedSamples INT DEFAULT 0,

    INDEX IX_ActivationCapture_Project (ProjectId),
    INDEX IX_ActivationCapture_Status (SessionStatus)
);
GO

-- Raw activation data storage with FILESTREAM
CREATE TABLE dbo.ActivationData (
    ActivationId BIGINT IDENTITY(1,1) PRIMARY KEY,
    SessionId BIGINT NOT NULL FOREIGN KEY REFERENCES dbo.ActivationCaptureSessions(SessionId),
    LayerIndex INT NOT NULL,
    TokenPosition INT NOT NULL,
    SampleIndex INT NOT NULL,
    InputText NVARCHAR(MAX), -- The input that generated this activation
    ActivationVector VARBINARY(MAX) FILESTREAM NOT NULL,
    ActivationVectorId UNIQUEIDENTIFIER ROWGUIDCOL NOT NULL UNIQUE DEFAULT NEWID(),
    VectorDimension INT NOT NULL,
    CaptureTimestamp DATETIME2 NOT NULL DEFAULT GETUTCDATE(),

    INDEX IX_ActivationData_Session_Layer (SessionId, LayerIndex),
    INDEX IX_ActivationData_Sample (SampleIndex)
) FILESTREAM_ON ActivationDataGroup;
GO

-- Skip Transcoder Models - stores trained feature extractors
CREATE TABLE dbo.SkipTranscoderModels (
    TranscoderId INT IDENTITY(1,1) PRIMARY KEY,
    SessionId BIGINT NOT NULL FOREIGN KEY REFERENCES dbo.ActivationCaptureSessions(SessionId),
    LayerIndex INT NOT NULL,
    InputDimension INT NOT NULL,
    LatentDimension INT NOT NULL, -- Sparse dimension
    SparsityLevel FLOAT NOT NULL, -- Average sparsity achieved
    ReconstructionLoss FLOAT NOT NULL,
    EncoderWeights VARBINARY(MAX) FILESTREAM NOT NULL,
    DecoderWeights VARBINARY(MAX) FILESTREAM NOT NULL,
    SkipWeights VARBINARY(MAX) FILESTREAM NOT NULL, -- Ax + b parameters
    EncoderWeightsId UNIQUEIDENTIFIER ROWGUIDCOL NOT NULL UNIQUE DEFAULT NEWID(),
    TrainingCompleted DATETIME2 NOT NULL DEFAULT GETUTCDATE(),

    INDEX IX_SkipTranscoder_Session_Layer (SessionId, LayerIndex)
) FILESTREAM_ON ActivationDataGroup;
GO

-- Discovered Features - interpretable features extracted by transcoders
CREATE TABLE dbo.DiscoveredFeatures (
    FeatureId BIGINT IDENTITY(1,1) PRIMARY KEY,
    TranscoderId INT NOT NULL FOREIGN KEY REFERENCES dbo.SkipTranscoderModels(TranscoderId),
    FeatureIndex INT NOT NULL, -- Index in the sparse latent space
    FeatureName NVARCHAR(255), -- Human-readable name
    Description NVARCHAR(MAX), -- Detailed description of what this feature represents
    AverageActivation FLOAT NOT NULL,
    SparsityScore FLOAT NOT NULL,
    InterpretabilityScore FLOAT, -- Automated interpretability metric
    ExampleActivations NVARCHAR(MAX), -- JSON array of example inputs that activate this feature

    UNIQUE (TranscoderId, FeatureIndex),
    INDEX IX_DiscoveredFeatures_Transcoder (TranscoderId),
    INDEX IX_DiscoveredFeatures_Name (FeatureName)
);
GO

-- Causal Relationships - discovered through activation patching
CREATE TABLE dbo.CausalRelationships (
    RelationshipId BIGINT IDENTITY(1,1) PRIMARY KEY,
    SourceFeatureId BIGINT NOT NULL FOREIGN KEY REFERENCES dbo.DiscoveredFeatures(FeatureId),
    TargetFeatureId BIGINT NOT NULL FOREIGN KEY REFERENCES dbo.DiscoveredFeatures(FeatureId),
    CausalStrength FLOAT NOT NULL, -- Strength of causal influence
    ConfidenceScore FLOAT NOT NULL, -- Statistical confidence in the relationship
    DiscoveryMethod NVARCHAR(100) NOT NULL, -- 'activation_patching', 'intervention', etc.
    ExampleInputs NVARCHAR(MAX), -- JSON array of inputs that demonstrate this relationship
    DiscoveredDate DATETIME2 NOT NULL DEFAULT GETUTCDATE(),

    INDEX IX_CausalRelationships_Source (SourceFeatureId),
    INDEX IX_CausalRelationships_Target (TargetFeatureId),
    INDEX IX_CausalRelationships_Strength (CausalStrength DESC)
);
GO

-- Computational Circuits - groups of features that work together
CREATE TABLE dbo.ComputationalCircuits (
    CircuitId INT IDENTITY(1,1) PRIMARY KEY,
    ProjectId INT NOT NULL FOREIGN KEY REFERENCES dbo.DistillationProjects(ProjectId),
    CircuitName NVARCHAR(255) NOT NULL,
    Description NVARCHAR(MAX),
    Domain NVARCHAR(100) NOT NULL,
    CircuitType NVARCHAR(100), -- 'factual_recall', 'reasoning', 'language_modeling', etc.
    Importance FLOAT NOT NULL, -- Overall importance score for the target domain
    Neo4jNodeId NVARCHAR(255), -- Reference to Neo4j circuit node
    DiscoveredDate DATETIME2 NOT NULL DEFAULT GETUTCDATE(),

    INDEX IX_ComputationalCircuits_Project (ProjectId),
    INDEX IX_ComputationalCircuits_Domain (Domain),
    INDEX IX_ComputationalCircuits_Importance (Importance DESC)
);
GO

-- Circuit Features - mapping between circuits and their constituent features
CREATE TABLE dbo.CircuitFeatures (
    CircuitId INT NOT NULL FOREIGN KEY REFERENCES dbo.ComputationalCircuits(CircuitId),
    FeatureId BIGINT NOT NULL FOREIGN KEY REFERENCES dbo.DiscoveredFeatures(FeatureId),
    Role NVARCHAR(100), -- 'input', 'intermediate', 'output', 'inhibitory'
    Importance FLOAT NOT NULL, -- Importance of this feature within the circuit

    PRIMARY KEY (CircuitId, FeatureId),
    INDEX IX_CircuitFeatures_Circuit (CircuitId),
    INDEX IX_CircuitFeatures_Feature (FeatureId)
);
GO

-- Distilled Agents - final output of the distillation process
CREATE TABLE dbo.DistilledAgents (
    AgentId INT IDENTITY(1,1) PRIMARY KEY,
    ProjectId INT NOT NULL FOREIGN KEY REFERENCES dbo.DistillationProjects(ProjectId),
    AgentName NVARCHAR(255) NOT NULL,
    Description NVARCHAR(MAX),
    SourceModelId INT NOT NULL FOREIGN KEY REFERENCES dbo.FoundationModels(ModelId),
    TargetDomain NVARCHAR(100) NOT NULL,
    DistillationMethod NVARCHAR(100) NOT NULL, -- 'attribution_pruning', 'circuit_distillation'
    PruningPercentage FLOAT, -- Percentage of original model parameters removed
    RetainedCircuits NVARCHAR(MAX), -- JSON array of circuit IDs retained
    DistilledModel VARBINARY(MAX) FILESTREAM NOT NULL,
    DistilledModelId UNIQUEIDENTIFIER ROWGUIDCOL NOT NULL UNIQUE DEFAULT NEWID(),
    ModelSize BIGINT NOT NULL, -- Size of distilled model in bytes
    PerformanceMetrics NVARCHAR(MAX), -- JSON object with benchmark results
    CreatedDate DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    UserId NVARCHAR(128) NOT NULL,
    IsActive BIT NOT NULL DEFAULT 1,

    INDEX IX_DistilledAgents_Project (ProjectId),
    INDEX IX_DistilledAgents_Domain (TargetDomain),
    INDEX IX_DistilledAgents_UserId (UserId)
) FILESTREAM_ON ModelDataGroup;
GO

-- Agent Execution Sessions - tracks agent runtime behavior
CREATE TABLE dbo.AgentExecutionSessions (
    SessionId BIGINT IDENTITY(1,1) PRIMARY KEY,
    AgentId INT NOT NULL FOREIGN KEY REFERENCES dbo.DistilledAgents(AgentId),
    UserId NVARCHAR(128) NOT NULL,
    SessionName NVARCHAR(255),
    StartTime DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    EndTime DATETIME2 NULL,
    Status NVARCHAR(50) NOT NULL DEFAULT 'Active', -- 'Active', 'Paused', 'Completed', 'Failed'
    ConversationState NVARCHAR(MAX), -- Serialized LangGraph state
    TotalTokensProcessed BIGINT DEFAULT 0,
    TotalInferences INT DEFAULT 0,

    INDEX IX_AgentExecution_Agent (AgentId),
    INDEX IX_AgentExecution_User (UserId),
    INDEX IX_AgentExecution_Status (Status)
);
GO

-- Agent Messages - individual interactions within sessions
CREATE TABLE dbo.AgentMessages (
    MessageId BIGINT IDENTITY(1,1) PRIMARY KEY,
    SessionId BIGINT NOT NULL FOREIGN KEY REFERENCES dbo.AgentExecutionSessions(SessionId),
    MessageType NVARCHAR(50) NOT NULL, -- 'user', 'assistant', 'system', 'function'
    Content NVARCHAR(MAX) NOT NULL,
    Metadata NVARCHAR(MAX), -- JSON metadata about the message
    ActiveCircuits NVARCHAR(MAX), -- JSON array of circuit IDs active during this message
    TokenCount INT,
    InferenceTimeMs INT,
    Timestamp DATETIME2 NOT NULL DEFAULT GETUTCDATE(),

    INDEX IX_AgentMessages_Session (SessionId),
    INDEX IX_AgentMessages_Type (MessageType),
    INDEX IX_AgentMessages_Timestamp (Timestamp)
);
GO

-- Neo4j Integration Configuration
CREATE TABLE dbo.Neo4jConfiguration (
    ConfigId INT IDENTITY(1,1) PRIMARY KEY,
    ServerUri NVARCHAR(255) NOT NULL,
    DatabaseName NVARCHAR(100) NOT NULL DEFAULT 'neo4j',
    Username NVARCHAR(100) NOT NULL,
    PasswordHash NVARCHAR(255) NOT NULL, -- Encrypted password
    IsActive BIT NOT NULL DEFAULT 1,
    CreatedDate DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    LastTestedDate DATETIME2,
    TestResult NVARCHAR(MAX) -- JSON test results
);
GO

-- Create security credentials for external API calls
CREATE DATABASE SCOPED CREDENTIAL Neo4jCredential
WITH IDENTITY = 'neo4j',
SECRET = 'neo4j_password_placeholder'; -- Replace with actual password
GO

-- Row-Level Security for multi-tenancy
CREATE FUNCTION dbo.fn_UserAccessPredicate(@UserId NVARCHAR(128))
RETURNS TABLE
WITH SCHEMABINDING
AS
RETURN SELECT 1 AS fn_securitypredicate_result
WHERE @UserId = USER_NAME() OR IS_MEMBER('db_admin') = 1;
GO

-- Apply RLS to user-scoped tables (excluding FILESTREAM tables)
CREATE SECURITY POLICY dbo.UserAccessPolicy
ADD FILTER PREDICATE dbo.fn_UserAccessPredicate(UserId) ON dbo.DistillationProjects,
ADD FILTER PREDICATE dbo.fn_UserAccessPredicate(CreatedBy) ON dbo.FoundationModels,
ADD FILTER PREDICATE dbo.fn_UserAccessPredicate(UserId) ON dbo.AgentExecutionSessions;

ALTER SECURITY POLICY dbo.UserAccessPolicy WITH (STATE = ON);
GO

-- Indexes for performance optimization
CREATE NONCLUSTERED INDEX IX_ActivationData_LayerIndex_TokenPosition
ON dbo.ActivationData (LayerIndex, TokenPosition)
INCLUDE (VectorDimension, CaptureTimestamp);

CREATE NONCLUSTERED INDEX IX_CausalRelationships_Strength_Confidence
ON dbo.CausalRelationships (CausalStrength DESC, ConfidenceScore DESC);

CREATE NONCLUSTERED INDEX IX_AgentMessages_SessionId_Timestamp
ON dbo.AgentMessages (SessionId, Timestamp DESC)
INCLUDE (MessageType, TokenCount);
GO

-- Create computed columns for efficient querying
ALTER TABLE dbo.DistillationProjects
ADD DurationHours AS DATEDIFF(HOUR, CreatedDate, ISNULL(CompletedDate, GETUTCDATE()));

ALTER TABLE dbo.ActivationCaptureSessions
ADD CompletionPercentage AS
    CASE WHEN TotalSamples > 0
    THEN (ProcessedSamples * 100.0 / TotalSamples)
    ELSE 0 END PERSISTED;
GO

-- Views for common queries
CREATE VIEW dbo.vw_ActiveDistillationProjects AS
SELECT
    p.ProjectId,
    p.ProjectName,
    p.TargetDomain,
    p.UserId,
    p.Status,
    fm.ModelName AS SourceModelName,
    fm.ParameterCount,
    p.CreatedDate,
    p.DurationHours
FROM dbo.DistillationProjects p
INNER JOIN dbo.FoundationModels fm ON p.SourceModelId = fm.ModelId
WHERE p.Status IN ('Created', 'Analyzing', 'Distilling');
GO

CREATE VIEW dbo.vw_DistilledAgentSummary AS
SELECT
    da.AgentId,
    da.AgentName,
    da.TargetDomain,
    da.DistillationMethod,
    da.PruningPercentage,
    da.ModelSize,
    fm.ModelName AS SourceModelName,
    fm.ParameterCount AS SourceParameterCount,
    da.CreatedDate,
    da.UserId,
    COUNT(aes.SessionId) AS TotalSessions,
    SUM(aes.TotalTokensProcessed) AS TotalTokensProcessed
FROM dbo.DistilledAgents da
INNER JOIN dbo.FoundationModels fm ON da.SourceModelId = fm.ModelId
LEFT JOIN dbo.AgentExecutionSessions aes ON da.AgentId = aes.AgentId
WHERE da.IsActive = 1
GROUP BY da.AgentId, da.AgentName, da.TargetDomain, da.DistillationMethod,
         da.PruningPercentage, da.ModelSize, fm.ModelName, fm.ParameterCount,
         da.CreatedDate, da.UserId;
GO

PRINT 'Hartonomous Distillation Engine schema created successfully.';
GO