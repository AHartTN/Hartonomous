-- HartonomousDB Database Schema Creation Script
-- This script assumes FILESTREAM is enabled at the SQL Server instance level.

IF NOT EXISTS (SELECT * FROM sys.databases WHERE name = 'HartonomousDB')
    CREATE DATABASE HartonomousDB;
GO

ALTER DATABASE HartonomousDB SET RECOVERY FULL;
GO

-- Add FILESTREAM Filegroup if it doesn't exist
IF NOT EXISTS (SELECT * FROM sys.filegroups WHERE name = 'HartonomousFileStreamGroup')
    ALTER DATABASE HartonomousDB ADD FILEGROUP HartonomousFileStreamGroup CONTAINS FILESTREAM;
GO

-- Add FILESTREAM File if it doesn't exist
IF NOT EXISTS (SELECT * FROM HartonomousDB.sys.database_files WHERE name = 'HartonomousModelWeightsFS')
    ALTER DATABASE HartonomousDB ADD FILE (
        NAME = 'HartonomousModelWeightsFS',
        FILENAME = 'D:\HartonomousData\ModelWeightsFS'
    ) TO FILEGROUP HartonomousFileStreamGroup;
GO

USE HartonomousDB;
GO

-- Define Tables
CREATE TABLE dbo.Projects (
    ProjectId UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
    UserId NVARCHAR(128) NOT NULL,
    ProjectName NVARCHAR(256) NOT NULL,
    CreatedAt DATETIME2 NOT NULL DEFAULT GETUTCDATE()
);

CREATE TABLE dbo.ModelMetadata (
    ModelId UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
    ProjectId UNIQUEIDENTIFIER NOT NULL FOREIGN KEY REFERENCES dbo.Projects(ProjectId),
    ModelName NVARCHAR(256) NOT NULL,
    Version NVARCHAR(50) NOT NULL,
    License NVARCHAR(100) NOT NULL,
    MetadataJson NVARCHAR(MAX),
    CreatedAt DATETIME2 NOT NULL DEFAULT GETUTCDATE()
);

CREATE TABLE dbo.ModelComponents (
    ComponentId UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
    ModelId UNIQUEIDENTIFIER NOT NULL FOREIGN KEY REFERENCES dbo.ModelMetadata(ModelId),
    ComponentName NVARCHAR(512) NOT NULL,
    ComponentType NVARCHAR(128) NOT NULL,
    CreatedAt DATETIME2 NOT NULL DEFAULT GETUTCDATE()
);

-- Create history table for temporal data (system versioning)
CREATE TABLE dbo.ModelComponentsHistory (
    ComponentId UNIQUEIDENTIFIER NOT NULL,
    ModelId UNIQUEIDENTIFIER NOT NULL,
    ComponentName NVARCHAR(512) NOT NULL,
    ComponentType NVARCHAR(128) NOT NULL,
    CreatedAt DATETIME2 NOT NULL,
    SysStartTime DATETIME2 GENERATED ALWAYS AS ROW START NOT NULL,
    SysEndTime DATETIME2 GENERATED ALWAYS AS ROW END NOT NULL,
    PERIOD FOR SYSTEM_TIME (SysStartTime, SysEndTime)
);

-- Model relationships table (simplified from graph syntax)
CREATE TABLE dbo.ModelStructure (
    RelationshipId UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
    FromComponentId UNIQUEIDENTIFIER NOT NULL FOREIGN KEY REFERENCES dbo.ModelComponents(ComponentId),
    ToComponentId UNIQUEIDENTIFIER NOT NULL FOREIGN KEY REFERENCES dbo.ModelComponents(ComponentId),
    RelationshipType NVARCHAR(128) NOT NULL,
    CreatedAt DATETIME2 NOT NULL DEFAULT GETUTCDATE()
);

CREATE TABLE dbo.ComponentWeights (
    WeightId UNIQUEIDENTIFIER ROWGUIDCOL PRIMARY KEY DEFAULT NEWID(),
    ComponentId UNIQUEIDENTIFIER NOT NULL FOREIGN KEY REFERENCES dbo.ModelComponents(ComponentId) ON DELETE CASCADE,
    WeightData VARBINARY(MAX) FILESTREAM NULL
);

CREATE TABLE dbo.ComponentEmbeddings (
    EmbeddingId UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
    ComponentId UNIQUEIDENTIFIER NOT NULL FOREIGN KEY REFERENCES dbo.ModelComponents(ComponentId) ON DELETE CASCADE,
    EmbeddingVector VARCHAR(8000) NOT NULL
);

CREATE TABLE dbo.OutboxEvents (
    EventId UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
    EventType NVARCHAR(255) NOT NULL,
    Payload NVARCHAR(MAX) NOT NULL,
    CreatedAt DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    ProcessedAt DATETIME2 NULL
);
GO

-- Create JSON Index (with proper SET options)
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_ModelMetadata_JsonValue')
BEGIN
    ALTER TABLE dbo.ModelMetadata ADD MetadataAuthor AS JSON_VALUE(MetadataJson, '$.author') PERSISTED;
    CREATE INDEX IX_ModelMetadata_JsonValue ON dbo.ModelMetadata(MetadataAuthor);
END
GO