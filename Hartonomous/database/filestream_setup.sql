-- =============================================================================
-- FILESTREAM Configuration for Large Model Storage
-- SQL Server FILESTREAM setup for multi-GB model file handling
-- =============================================================================

USE master;
GO

-- Enable FILESTREAM at instance level (if not already enabled)
-- This requires SQL Server restart and should be done by DBA
-- sp_configure 'filestream access level', 2;
-- RECONFIGURE;

-- Create or modify Hartonomous database with FILESTREAM filegroup
IF NOT EXISTS (SELECT name FROM sys.databases WHERE name = 'Hartonomous')
BEGIN
    CREATE DATABASE Hartonomous
    ON
    ( NAME = 'Hartonomous_Data',
      FILENAME = 'C:\Program Files\Microsoft SQL Server\MSSQL16.MSSQLSERVER\MSSQL\DATA\Hartonomous.mdf',
      SIZE = 1GB,
      MAXSIZE = 100GB,
      FILEGROWTH = 512MB ),

    FILEGROUP ModelFileStream_FG CONTAINS FILESTREAM
    ( NAME = 'ModelFileStream',
      FILENAME = 'C:\Program Files\Microsoft SQL Server\MSSQL16.MSSQLSERVER\MSSQL\DATA\ModelFileStreamData' )

    LOG ON
    ( NAME = 'Hartonomous_Log',
      FILENAME = 'C:\Program Files\Microsoft SQL Server\MSSQL16.MSSQLSERVER\MSSQL\DATA\Hartonomous.ldf',
      SIZE = 512MB,
      MAXSIZE = 10GB,
      FILEGROWTH = 256MB );
END
ELSE
BEGIN
    -- Add FILESTREAM filegroup to existing database
    IF NOT EXISTS (SELECT name FROM sys.filegroups WHERE name = 'ModelFileStream_FG')
    BEGIN
        ALTER DATABASE Hartonomous
        ADD FILEGROUP ModelFileStream_FG CONTAINS FILESTREAM;

        ALTER DATABASE Hartonomous
        ADD FILE
        ( NAME = 'ModelFileStream',
          FILENAME = 'C:\Program Files\Microsoft SQL Server\MSSQL16.MSSQLSERVER\MSSQL\DATA\ModelFileStreamData' )
        TO FILEGROUP ModelFileStream_FG;
    END
END
GO

USE Hartonomous;
GO

-- =============================================================================
-- FILESTREAM-enabled Tables for Model Storage
-- =============================================================================

-- Main model files table with FILESTREAM support
IF EXISTS (SELECT * FROM sys.tables WHERE name = 'ModelFiles')
BEGIN
    DROP TABLE ModelFiles;
END
GO

CREATE TABLE ModelFiles (
    -- Primary identifier
    ModelId UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),

    -- File metadata
    ModelFormat NVARCHAR(50) NOT NULL DEFAULT 'GGUF',
    ModelName NVARCHAR(255) NOT NULL,
    Version NVARCHAR(100) NOT NULL DEFAULT '1.0',
    Description NVARCHAR(MAX) NULL,
    Properties NVARCHAR(MAX) NULL, -- JSON metadata

    -- User and access control
    UserId NVARCHAR(128) NOT NULL,

    -- File integrity and tracking
    FileHash NVARCHAR(64) NOT NULL, -- SHA-256 hash
    FileSizeBytes BIGINT NOT NULL,
    Status NVARCHAR(50) NOT NULL DEFAULT 'Uploading',

    -- Timestamps
    CreatedAt DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    LastAccessedAt DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    CompletedAt DATETIME2 NULL,

    -- FILESTREAM column for actual model data
    ModelData VARBINARY(MAX) FILESTREAM NOT NULL,

    -- Required for FILESTREAM - unique identifier for row
    FileStreamGuid UNIQUEIDENTIFIER ROWGUIDCOL NOT NULL UNIQUE DEFAULT NEWID(),

    -- Indexes for performance
    INDEX IX_ModelFiles_UserId (UserId),
    INDEX IX_ModelFiles_Status (Status),
    INDEX IX_ModelFiles_ModelFormat (ModelFormat),
    INDEX IX_ModelFiles_CreatedAt (CreatedAt DESC),
    INDEX IX_ModelFiles_FileHash (FileHash),

    -- Computed column for FILESTREAM path access
    FileStreamPath AS ModelData.PathName()

) FILESTREAM_ON ModelFileStream_FG;
GO

-- Model processing cache for frequently accessed components
CREATE TABLE ModelProcessingCache (
    CacheId UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
    ModelId UNIQUEIDENTIFIER NOT NULL,
    ComponentPath NVARCHAR(500) NOT NULL,
    ComponentType NVARCHAR(100) NOT NULL,
    UserId NVARCHAR(128) NOT NULL,

    -- Cached component data (smaller, frequently accessed pieces)
    ComponentData VARBINARY(MAX) FILESTREAM NULL,
    ComponentMetadata NVARCHAR(MAX) NULL, -- JSON

    -- Cache management
    AccessCount BIGINT NOT NULL DEFAULT 0,
    LastAccessedAt DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    CreatedAt DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    ExpiresAt DATETIME2 NULL,

    -- Required for FILESTREAM
    CacheGuid UNIQUEIDENTIFIER ROWGUIDCOL NOT NULL UNIQUE DEFAULT NEWID(),

    FOREIGN KEY (ModelId) REFERENCES ModelFiles(ModelId) ON DELETE CASCADE,
    INDEX IX_ModelProcessingCache_ModelId_UserId (ModelId, UserId),
    INDEX IX_ModelProcessingCache_ComponentPath (ComponentPath),
    INDEX IX_ModelProcessingCache_LastAccessed (LastAccessedAt DESC),
    INDEX IX_ModelProcessingCache_ExpiresAt (ExpiresAt),

    UNIQUE (ModelId, ComponentPath, UserId)

) FILESTREAM_ON ModelFileStream_FG;
GO

-- Model streaming sessions for active processing
CREATE TABLE ModelStreamingSessions (
    SessionId UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
    ModelId UNIQUEIDENTIFIER NOT NULL,
    UserId NVARCHAR(128) NOT NULL,

    -- Session state
    SessionType NVARCHAR(100) NOT NULL, -- 'Parsing', 'Extraction', 'Tracing'
    Status NVARCHAR(50) NOT NULL DEFAULT 'Active',
    Progress DECIMAL(5,2) NOT NULL DEFAULT 0.00, -- Percentage complete

    -- Resource management
    MemoryUsageMB BIGINT NULL,
    ProcessingTimeMs BIGINT NULL,
    LastActivityAt DATETIME2 NOT NULL DEFAULT GETUTCDATE(),

    -- Session data and state
    SessionState NVARCHAR(MAX) NULL, -- JSON state information
    TempData VARBINARY(MAX) FILESTREAM NULL, -- Temporary processing data

    -- Session lifecycle
    CreatedAt DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    ExpiresAt DATETIME2 NOT NULL DEFAULT DATEADD(HOUR, 4, GETUTCDATE()),

    -- Required for FILESTREAM
    SessionGuid UNIQUEIDENTIFIER ROWGUIDCOL NOT NULL UNIQUE DEFAULT NEWID(),

    FOREIGN KEY (ModelId) REFERENCES ModelFiles(ModelId) ON DELETE CASCADE,
    INDEX IX_ModelStreamingSessions_ModelId_UserId (ModelId, UserId),
    INDEX IX_ModelStreamingSessions_Status (Status),
    INDEX IX_ModelStreamingSessions_ExpiresAt (ExpiresAt),
    INDEX IX_ModelStreamingSessions_LastActivity (LastActivityAt DESC)

) FILESTREAM_ON ModelFileStream_FG;
GO

-- =============================================================================
-- Stored Procedures for FILESTREAM Operations
-- =============================================================================

-- Initialize model storage with FILESTREAM placeholder
CREATE OR ALTER PROCEDURE sp_InitializeModelStorage
    @ModelId UNIQUEIDENTIFIER,
    @ModelName NVARCHAR(255),
    @ModelFormat NVARCHAR(50) = 'GGUF',
    @Version NVARCHAR(100) = '1.0',
    @Description NVARCHAR(MAX) = NULL,
    @Properties NVARCHAR(MAX) = NULL,
    @UserId NVARCHAR(128),
    @ExpectedSizeBytes BIGINT
AS
BEGIN
    SET NOCOUNT ON;

    BEGIN TRY
        BEGIN TRANSACTION;

        -- Insert model record with empty FILESTREAM
        INSERT INTO ModelFiles (
            ModelId, ModelFormat, ModelName, Version, Description, Properties,
            UserId, FileHash, FileSizeBytes, Status, ModelData
        ) VALUES (
            @ModelId, @ModelFormat, @ModelName, @Version, @Description, @Properties,
            @UserId, '', @ExpectedSizeBytes, 'Initializing', 0x
        );

        -- Return FILESTREAM path for client access
        SELECT
            ModelData.PathName() AS FileStreamPath,
            GET_FILESTREAM_TRANSACTION_CONTEXT() AS TransactionContext
        FROM ModelFiles
        WHERE ModelId = @ModelId;

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0
            ROLLBACK TRANSACTION;
        THROW;
    END CATCH
END
GO

-- Complete model storage and update metadata
CREATE OR ALTER PROCEDURE sp_CompleteModelStorage
    @ModelId UNIQUEIDENTIFIER,
    @FileHash NVARCHAR(64),
    @ActualSizeBytes BIGINT
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE ModelFiles
    SET
        FileHash = @FileHash,
        FileSizeBytes = @ActualSizeBytes,
        Status = 'Completed',
        CompletedAt = GETUTCDATE(),
        LastAccessedAt = GETUTCDATE()
    WHERE ModelId = @ModelId;

    SELECT @@ROWCOUNT AS RowsUpdated;
END
GO

-- Get FILESTREAM access for model processing
CREATE OR ALTER PROCEDURE sp_GetModelFileStreamAccess
    @ModelId UNIQUEIDENTIFIER,
    @UserId NVARCHAR(128),
    @AccessType NVARCHAR(50) = 'Read' -- 'Read' or 'ReadWrite'
AS
BEGIN
    SET NOCOUNT ON;

    -- Update last accessed timestamp
    UPDATE ModelFiles
    SET LastAccessedAt = GETUTCDATE()
    WHERE ModelId = @ModelId AND UserId = @UserId;

    -- Return access information
    SELECT
        ModelId,
        ModelName,
        ModelFormat,
        FileSizeBytes,
        FileHash,
        Status,
        ModelData.PathName() AS FileStreamPath,
        CASE
            WHEN @AccessType = 'ReadWrite' THEN GET_FILESTREAM_TRANSACTION_CONTEXT()
            ELSE NULL
        END AS TransactionContext
    FROM ModelFiles
    WHERE ModelId = @ModelId AND UserId = @UserId AND Status = 'Completed';
END
GO

-- Clean up expired sessions and cache
CREATE OR ALTER PROCEDURE sp_CleanupModelStreaming
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @CleanupCount INT = 0;

    -- Clean up expired sessions
    DELETE FROM ModelStreamingSessions
    WHERE ExpiresAt < GETUTCDATE() OR Status = 'Completed';

    SET @CleanupCount = @@ROWCOUNT;

    -- Clean up old cache entries (older than 24 hours with low access)
    DELETE FROM ModelProcessingCache
    WHERE ExpiresAt < GETUTCDATE()
       OR (CreatedAt < DATEADD(HOUR, -24, GETUTCDATE()) AND AccessCount < 5);

    SET @CleanupCount = @CleanupCount + @@ROWCOUNT;

    SELECT @CleanupCount AS RecordsCleaned;
END
GO

-- =============================================================================
-- Functions for Model Analysis
-- =============================================================================

-- Get model storage statistics
CREATE OR ALTER FUNCTION fn_GetModelStorageStats(@UserId NVARCHAR(128))
RETURNS TABLE
AS
RETURN
(
    SELECT
        COUNT(*) AS TotalModels,
        SUM(FileSizeBytes) AS TotalStorageBytes,
        AVG(FileSizeBytes) AS AverageModelSize,
        COUNT(CASE WHEN Status = 'Completed' THEN 1 END) AS CompletedModels,
        COUNT(CASE WHEN Status = 'Processing' THEN 1 END) AS ProcessingModels,
        MAX(CreatedAt) AS LastModelAdded
    FROM ModelFiles
    WHERE UserId = @UserId
);
GO

-- =============================================================================
-- Indexes and Performance Optimization
-- =============================================================================

-- Create additional performance indexes
CREATE NONCLUSTERED INDEX IX_ModelFiles_Composite_Performance
ON ModelFiles (UserId, Status, ModelFormat)
INCLUDE (ModelId, ModelName, FileSizeBytes, CreatedAt);
GO

CREATE NONCLUSTERED INDEX IX_ModelProcessingCache_Performance
ON ModelProcessingCache (ModelId, UserId, LastAccessedAt)
INCLUDE (ComponentPath, ComponentType, AccessCount);
GO

-- =============================================================================
-- FILESTREAM Cleanup and Maintenance Jobs
-- =============================================================================

-- Schedule cleanup job (to be run periodically)
-- EXEC sp_CleanupModelStreaming;

-- Verify FILESTREAM configuration
SELECT
    name,
    physical_name,
    type_desc,
    state_desc
FROM sys.database_files
WHERE type_desc = 'FILESTREAM';

-- Check FILESTREAM settings
SELECT
    SERVERPROPERTY('FilestreamConfiguredLevel') AS ConfiguredLevel,
    SERVERPROPERTY('FilestreamEffectiveLevel') AS EffectiveLevel,
    SERVERPROPERTY('FilestreamShareName') AS ShareName;

PRINT 'FILESTREAM configuration completed successfully!';
PRINT 'Tables: ModelFiles, ModelProcessingCache, ModelStreamingSessions';
PRINT 'Procedures: sp_InitializeModelStorage, sp_CompleteModelStorage, sp_GetModelFileStreamAccess, sp_CleanupModelStreaming';
PRINT 'Ready for large model file storage and streaming operations.';
GO