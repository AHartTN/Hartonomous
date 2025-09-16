-- Model Query Engine Database Schema
-- This script creates the necessary tables for the Neural Map and Model Query functionality

-- Model Architectures table
CREATE TABLE dbo.ModelArchitectures (
    ModelId UNIQUEIDENTIFIER NOT NULL,
    ArchitectureName NVARCHAR(255) NOT NULL,
    Framework NVARCHAR(100) NOT NULL,
    Configuration NVARCHAR(MAX) NOT NULL, -- JSON
    Hyperparameters NVARCHAR(MAX) NOT NULL, -- JSON
    CreatedAt DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    UpdatedAt DATETIME2 NULL,
    CONSTRAINT PK_ModelArchitectures PRIMARY KEY (ModelId),
    CONSTRAINT FK_ModelArchitectures_Models FOREIGN KEY (ModelId) REFERENCES dbo.ModelMetadata(ModelId) ON DELETE CASCADE
);

-- Model Layers table
CREATE TABLE dbo.ModelLayers (
    LayerId UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID(),
    ModelId UNIQUEIDENTIFIER NOT NULL,
    LayerName NVARCHAR(255) NOT NULL,
    LayerType NVARCHAR(100) NOT NULL,
    LayerIndex INT NOT NULL,
    Configuration NVARCHAR(MAX) NOT NULL, -- JSON
    CreatedAt DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    UpdatedAt DATETIME2 NULL,
    CONSTRAINT PK_ModelLayers PRIMARY KEY (LayerId),
    CONSTRAINT FK_ModelLayers_Models FOREIGN KEY (ModelId) REFERENCES dbo.ModelMetadata(ModelId) ON DELETE CASCADE,
    CONSTRAINT UK_ModelLayers_ModelLayerName UNIQUE (ModelId, LayerName)
);

-- Model Weights table
CREATE TABLE dbo.ModelWeights (
    WeightId UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID(),
    ModelId UNIQUEIDENTIFIER NOT NULL,
    LayerName NVARCHAR(255) NOT NULL,
    WeightName NVARCHAR(255) NOT NULL,
    DataType NVARCHAR(50) NOT NULL,
    Shape NVARCHAR(500) NOT NULL, -- JSON array of dimensions
    SizeBytes BIGINT NOT NULL,
    StoragePath NVARCHAR(1000) NOT NULL,
    ChecksumSha256 NVARCHAR(64) NOT NULL,
    CreatedAt DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    UpdatedAt DATETIME2 NULL,
    CONSTRAINT PK_ModelWeights PRIMARY KEY (WeightId),
    CONSTRAINT FK_ModelWeights_Models FOREIGN KEY (ModelId) REFERENCES dbo.ModelMetadata(ModelId) ON DELETE CASCADE,
    CONSTRAINT UK_ModelWeights_ModelLayerWeight UNIQUE (ModelId, LayerName, WeightName)
);

-- Model Versions table
CREATE TABLE dbo.ModelVersions (
    VersionId UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID(),
    ModelId UNIQUEIDENTIFIER NOT NULL,
    Version NVARCHAR(50) NOT NULL,
    Description NVARCHAR(1000) NULL,
    Changes NVARCHAR(MAX) NOT NULL, -- JSON
    ParentVersion NVARCHAR(50) NULL,
    CreatedAt DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    CreatedBy NVARCHAR(450) NOT NULL,
    CONSTRAINT PK_ModelVersions PRIMARY KEY (VersionId),
    CONSTRAINT FK_ModelVersions_Models FOREIGN KEY (ModelId) REFERENCES dbo.ModelMetadata(ModelId) ON DELETE CASCADE,
    CONSTRAINT UK_ModelVersions_ModelVersion UNIQUE (ModelId, Version)
);

-- Create indexes for performance
CREATE INDEX IX_ModelLayers_ModelId ON dbo.ModelLayers(ModelId);
CREATE INDEX IX_ModelLayers_LayerType ON dbo.ModelLayers(LayerType);
CREATE INDEX IX_ModelWeights_ModelId ON dbo.ModelWeights(ModelId);
CREATE INDEX IX_ModelWeights_LayerName ON dbo.ModelWeights(LayerName);
CREATE INDEX IX_ModelWeights_DataType ON dbo.ModelWeights(DataType);
CREATE INDEX IX_ModelVersions_ModelId ON dbo.ModelVersions(ModelId);
CREATE INDEX IX_ModelVersions_CreatedAt ON dbo.ModelVersions(CreatedAt);

-- Add FileStream support for large weight storage (optional, for very large models)
-- This would require additional configuration at the database level
-- ALTER DATABASE Hartonomous ADD FILEGROUP ModelWeightFileStream CONTAINS FILESTREAM;
-- ALTER DATABASE Hartonomous ADD FILE (NAME = 'ModelWeightFileStream', FILENAME = 'C:\ModelStorage\ModelWeightFileStream') TO FILEGROUP ModelWeightFileStream;

-- Add computed columns for easier querying
ALTER TABLE dbo.ModelWeights ADD ParameterCount AS (
    CASE
        WHEN ISJSON(Shape) = 1 THEN
            (SELECT
                CASE
                    WHEN COUNT(*) = 0 THEN 1
                    ELSE EXP(SUM(LOG(CAST([value] AS FLOAT))))
                END
             FROM OPENJSON(Shape)
             WHERE ISNUMERIC([value]) = 1 AND CAST([value] AS INT) > 0)
        ELSE 1
    END
) PERSISTED;

-- Views for easier data access
CREATE VIEW dbo.vw_ModelSummary AS
SELECT
    m.ModelId,
    m.ModelName,
    m.Version,
    m.License,
    ma.ArchitectureName,
    ma.Framework,
    COUNT(DISTINCT ml.LayerId) as LayerCount,
    COUNT(DISTINCT mw.WeightId) as WeightCount,
    SUM(mw.SizeBytes) as TotalSizeBytes,
    SUM(mw.ParameterCount) as TotalParameters,
    MAX(mv.CreatedAt) as LastVersionDate
FROM dbo.ModelMetadata m
LEFT JOIN dbo.ModelArchitectures ma ON m.ModelId = ma.ModelId
LEFT JOIN dbo.ModelLayers ml ON m.ModelId = ml.ModelId
LEFT JOIN dbo.ModelWeights mw ON m.ModelId = mw.ModelId
LEFT JOIN dbo.ModelVersions mv ON m.ModelId = mv.ModelId
GROUP BY m.ModelId, m.ModelName, m.Version, m.License, ma.ArchitectureName, ma.Framework;

-- View for layer statistics
CREATE VIEW dbo.vw_LayerStatistics AS
SELECT
    ml.ModelId,
    ml.LayerType,
    COUNT(*) as LayerCount,
    AVG(CAST(mw.ParameterCount AS FLOAT)) as AvgParametersPerLayer,
    SUM(mw.SizeBytes) as TotalLayerSizeBytes
FROM dbo.ModelLayers ml
LEFT JOIN dbo.ModelWeights mw ON ml.ModelId = mw.ModelId AND ml.LayerName = mw.LayerName
GROUP BY ml.ModelId, ml.LayerType;

-- Stored procedures for common operations

-- Procedure to get model introspection data
CREATE PROCEDURE dbo.sp_GetModelIntrospection
    @ModelId UNIQUEIDENTIFIER,
    @UserId NVARCHAR(450)
AS
BEGIN
    SET NOCOUNT ON;

    -- Verify user access
    IF NOT EXISTS (
        SELECT 1 FROM dbo.ModelMetadata m
        INNER JOIN dbo.Projects p ON m.ProjectId = p.ProjectId
        WHERE m.ModelId = @ModelId AND p.UserId = @UserId
    )
    BEGIN
        THROW 50001, 'Model not found or access denied', 1;
    END

    -- Return model summary
    SELECT * FROM dbo.vw_ModelSummary WHERE ModelId = @ModelId;

    -- Return layer statistics
    SELECT * FROM dbo.vw_LayerStatistics WHERE ModelId = @ModelId;

    -- Return weight details
    SELECT WeightId, LayerName, WeightName, DataType, Shape, SizeBytes, ParameterCount
    FROM dbo.ModelWeights
    WHERE ModelId = @ModelId
    ORDER BY LayerName, WeightName;
END;

-- Procedure to compare two models
CREATE PROCEDURE dbo.sp_CompareModels
    @ModelAId UNIQUEIDENTIFIER,
    @ModelBId UNIQUEIDENTIFIER,
    @UserId NVARCHAR(450)
AS
BEGIN
    SET NOCOUNT ON;

    -- Verify user access to both models
    IF NOT EXISTS (
        SELECT 1 FROM dbo.ModelMetadata m
        INNER JOIN dbo.Projects p ON m.ProjectId = p.ProjectId
        WHERE m.ModelId = @ModelAId AND p.UserId = @UserId
    ) OR NOT EXISTS (
        SELECT 1 FROM dbo.ModelMetadata m
        INNER JOIN dbo.Projects p ON m.ProjectId = p.ProjectId
        WHERE m.ModelId = @ModelBId AND p.UserId = @UserId
    )
    BEGIN
        THROW 50001, 'One or both models not found or access denied', 1;
    END

    -- Compare model summaries
    SELECT 'Summary' as ComparisonType, * FROM dbo.vw_ModelSummary WHERE ModelId IN (@ModelAId, @ModelBId);

    -- Compare layer types
    SELECT
        'LayerTypes' as ComparisonType,
        LayerType,
        SUM(CASE WHEN ModelId = @ModelAId THEN LayerCount ELSE 0 END) as ModelA_Count,
        SUM(CASE WHEN ModelId = @ModelBId THEN LayerCount ELSE 0 END) as ModelB_Count
    FROM dbo.vw_LayerStatistics
    WHERE ModelId IN (@ModelAId, @ModelBId)
    GROUP BY LayerType;
END;

-- Function to calculate model similarity
CREATE FUNCTION dbo.fn_CalculateModelSimilarity
(
    @ModelAId UNIQUEIDENTIFIER,
    @ModelBId UNIQUEIDENTIFIER
)
RETURNS FLOAT
AS
BEGIN
    DECLARE @Similarity FLOAT = 0.0;
    DECLARE @CommonLayers INT = 0;
    DECLARE @TotalLayers INT = 0;

    -- Calculate layer similarity
    SELECT
        @CommonLayers = COUNT(*),
        @TotalLayers = (
            SELECT COUNT(DISTINCT LayerType)
            FROM dbo.ModelLayers
            WHERE ModelId IN (@ModelAId, @ModelBId)
        )
    FROM (
        SELECT LayerType FROM dbo.ModelLayers WHERE ModelId = @ModelAId
        INTERSECT
        SELECT LayerType FROM dbo.ModelLayers WHERE ModelId = @ModelBId
    ) CommonLayerTypes;

    IF @TotalLayers > 0
        SET @Similarity = CAST(@CommonLayers AS FLOAT) / @TotalLayers;

    RETURN @Similarity;
END;

PRINT 'Model Query Engine database schema created successfully.';