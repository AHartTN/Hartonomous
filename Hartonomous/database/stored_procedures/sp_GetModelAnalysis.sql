/*
 * Model Analysis Stored Procedure
 * Provides comprehensive analysis of ingested models
 */

CREATE OR ALTER PROCEDURE sp_GetModelAnalysis
    @ModelId UNIQUEIDENTIFIER,
    @UserId NVARCHAR(128)
AS
BEGIN
    SET NOCOUNT ON;

    -- Basic model information
    SELECT
        fm.ModelId,
        fm.ModelName,
        fm.ModelFormat,
        fm.ParameterCount,
        fm.ModelSizeBytes,
        fm.Status,
        fm.CreatedAt,
        fm.ProcessedAt,
        JSON_VALUE(fm.Metadata, '$.architecture') AS Architecture,
        JSON_VALUE(fm.Metadata, '$.context_length') AS ContextLength,
        JSON_VALUE(fm.Metadata, '$.embedding_length') AS EmbeddingLength
    FROM FoundationModels fm
    WHERE fm.ModelId = @ModelId AND fm.UserId = @UserId;

    -- Layer analysis
    SELECT
        ml.LayerName,
        ml.LayerType,
        ml.LayerIndex,
        COUNT(mc.ComponentId) AS ComponentCount,
        AVG(mc.RelevanceScore) AS AvgRelevanceScore,
        SUM(CASE WHEN ce.ComponentId IS NOT NULL THEN 1 ELSE 0 END) AS EmbeddedComponents
    FROM ModelLayers ml
    LEFT JOIN ModelComponents mc ON ml.LayerId = mc.LayerId
    LEFT JOIN ComponentEmbeddings ce ON mc.ComponentId = ce.ComponentId
    WHERE ml.ModelId = @ModelId AND ml.UserId = @UserId
    GROUP BY ml.LayerName, ml.LayerType, ml.LayerIndex
    ORDER BY ml.LayerIndex;

    -- Component type distribution
    SELECT
        mc.ComponentType,
        COUNT(*) AS ComponentCount,
        AVG(mc.RelevanceScore) AS AvgRelevanceScore,
        MIN(mc.RelevanceScore) AS MinRelevanceScore,
        MAX(mc.RelevanceScore) AS MaxRelevanceScore
    FROM ModelComponents mc
    WHERE mc.ModelId = @ModelId AND mc.UserId = @UserId
    GROUP BY mc.ComponentType
    ORDER BY COUNT(*) DESC;

    -- High relevance components
    SELECT TOP 20
        mc.ComponentName,
        mc.ComponentType,
        mc.RelevanceScore,
        mc.FunctionalDescription,
        ml.LayerName
    FROM ModelComponents mc
    INNER JOIN ModelLayers ml ON mc.LayerId = ml.LayerId
    WHERE mc.ModelId = @ModelId AND mc.UserId = @UserId
    ORDER BY mc.RelevanceScore DESC;

    -- Model statistics summary
    SELECT
        COUNT(DISTINCT ml.LayerId) AS TotalLayers,
        COUNT(DISTINCT mc.ComponentId) AS TotalComponents,
        COUNT(DISTINCT ce.ComponentId) AS EmbeddedComponents,
        AVG(mc.RelevanceScore) AS OverallAvgRelevance,
        STDEV(mc.RelevanceScore) AS RelevanceStdDev
    FROM ModelLayers ml
    LEFT JOIN ModelComponents mc ON ml.LayerId = mc.LayerId
    LEFT JOIN ComponentEmbeddings ce ON mc.ComponentId = ce.ComponentId
    WHERE ml.ModelId = @ModelId AND ml.UserId = @UserId;
END;
GO