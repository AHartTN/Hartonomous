-- Create database tables for model ingestion
-- Run this against your local SQL Server instance

USE Hartonomous;
GO

-- Foundation models table
CREATE TABLE IF NOT EXISTS FoundationModels (
    ModelId UNIQUEIDENTIFIER PRIMARY KEY,
    ModelName NVARCHAR(255) NOT NULL,
    ModelFormat NVARCHAR(50) NOT NULL DEFAULT 'GGUF',
    FilePath NVARCHAR(500) NOT NULL,
    ModelSizeBytes BIGINT NOT NULL,
    ParameterCount BIGINT NULL,
    Metadata NVARCHAR(MAX) NULL,
    Architecture NVARCHAR(100) NULL,
    ContextLength INT NULL,
    UserId NVARCHAR(128) NOT NULL,
    CreatedAt DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    ProcessedAt DATETIME2 NULL,
    Status NVARCHAR(50) NOT NULL DEFAULT 'Processing',
    ErrorMessage NVARCHAR(MAX) NULL,

    INDEX IX_FoundationModels_UserId (UserId),
    INDEX IX_FoundationModels_Status (Status),
    INDEX IX_FoundationModels_Architecture (Architecture)
);

-- Model components table
CREATE TABLE IF NOT EXISTS ModelComponents (
    ComponentId UNIQUEIDENTIFIER PRIMARY KEY,
    ModelId UNIQUEIDENTIFIER NOT NULL,
    ComponentName NVARCHAR(255) NOT NULL,
    ComponentType NVARCHAR(100) NOT NULL,
    LayerName NVARCHAR(100) NULL,
    LayerIndex INT NULL,
    Shape NVARCHAR(MAX) NULL,
    DataType NVARCHAR(50) NULL,
    ParameterCount BIGINT NULL,
    RelevanceScore FLOAT NULL,
    FunctionalDescription NVARCHAR(MAX) NULL,
    UserId NVARCHAR(128) NOT NULL,
    CreatedAt DATETIME2 NOT NULL DEFAULT GETUTCDATE(),

    FOREIGN KEY (ModelId) REFERENCES FoundationModels(ModelId) ON DELETE CASCADE,
    INDEX IX_ModelComponents_ModelId_UserId (ModelId, UserId),
    INDEX IX_ModelComponents_ComponentType (ComponentType),
    INDEX IX_ModelComponents_LayerIndex (LayerIndex),
    INDEX IX_ModelComponents_RelevanceScore (RelevanceScore DESC)
);

-- Component embeddings table with VECTOR support
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'ComponentEmbeddings')
BEGIN
    CREATE TABLE ComponentEmbeddings (
        EmbeddingId UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
        ComponentId UNIQUEIDENTIFIER NOT NULL,
        ModelId UNIQUEIDENTIFIER NOT NULL,
        UserId NVARCHAR(128) NOT NULL,
        ComponentType NVARCHAR(100) NOT NULL,
        Description NVARCHAR(MAX),
        EmbeddingVector VECTOR(1536) NOT NULL,
        CreatedAt DATETIME2 DEFAULT GETUTCDATE(),

        FOREIGN KEY (ComponentId) REFERENCES ModelComponents(ComponentId) ON DELETE CASCADE,
        INDEX IX_ComponentEmbeddings_ComponentId (ComponentId),
        INDEX IX_ComponentEmbeddings_ModelId_UserId (ModelId, UserId),
        INDEX IX_ComponentEmbeddings_ComponentType (ComponentType)
    );

    -- Create vector index for similarity search
    CREATE INDEX IX_ComponentEmbeddings_Vector
    ON ComponentEmbeddings(EmbeddingVector)
    USING VECTOR;
END
GO

-- Sample queries to verify setup
SELECT
    TABLE_NAME,
    COLUMN_NAME,
    DATA_TYPE,
    IS_NULLABLE
FROM INFORMATION_SCHEMA.COLUMNS
WHERE TABLE_NAME IN ('FoundationModels', 'ModelComponents', 'ComponentEmbeddings')
ORDER BY TABLE_NAME, ORDINAL_POSITION;

PRINT 'Database tables created successfully!';
PRINT 'You can now run model ingestion tests.';
GO