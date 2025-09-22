/*
 * Model Component Query Stored Procedure
 * Provides T-SQL interface for querying model components with vector similarity
 */

CREATE OR ALTER PROCEDURE sp_QueryModelComponents
    @ModelId UNIQUEIDENTIFIER = NULL,
    @ComponentType NVARCHAR(100) = NULL,
    @SearchQuery NVARCHAR(500) = NULL,
    @SimilarityThreshold FLOAT = 0.8,
    @MaxResults INT = 100,
    @UserId NVARCHAR(128)
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @QueryVector VECTOR(1536);

    -- Generate embedding for search query if provided
    IF @SearchQuery IS NOT NULL
    BEGIN
        -- In real implementation, this would call an embedding service
        -- For now, we'll search by text matching
        SET @QueryVector = NULL;
    END

    -- Build dynamic query based on parameters
    DECLARE @SQL NVARCHAR(MAX) = N'
        SELECT TOP (@MaxResults)
            mc.ComponentId,
            mc.ModelId,
            mc.ComponentName,
            mc.ComponentType,
            mc.RelevanceScore,
            mc.FunctionalDescription,
            ml.LayerName,
            ml.LayerType,
            ml.LayerIndex';

    -- Add similarity calculation if vector search
    IF @QueryVector IS NOT NULL
    BEGIN
        SET @SQL = @SQL + N',
            VECTOR_DISTANCE(''cosine'', ce.EmbeddingVector, @QueryVector) AS Distance,
            (1 - VECTOR_DISTANCE(''cosine'', ce.EmbeddingVector, @QueryVector)) AS SimilarityScore';
    END

    SET @SQL = @SQL + N'
        FROM ModelComponents mc
        INNER JOIN ModelLayers ml ON mc.LayerId = ml.LayerId
        LEFT JOIN ComponentEmbeddings ce ON mc.ComponentId = ce.ComponentId';

    -- Add WHERE conditions
    DECLARE @WhereClause NVARCHAR(1000) = N' WHERE mc.UserId = @UserId';

    IF @ModelId IS NOT NULL
        SET @WhereClause = @WhereClause + N' AND mc.ModelId = @ModelId';

    IF @ComponentType IS NOT NULL
        SET @WhereClause = @WhereClause + N' AND mc.ComponentType = @ComponentType';

    IF @SearchQuery IS NOT NULL AND @QueryVector IS NULL
        SET @WhereClause = @WhereClause + N' AND (mc.ComponentName LIKE ''%'' + @SearchQuery + ''%''
                                                 OR mc.FunctionalDescription LIKE ''%'' + @SearchQuery + ''%'')';

    IF @QueryVector IS NOT NULL
        SET @WhereClause = @WhereClause + N' AND VECTOR_DISTANCE(''cosine'', ce.EmbeddingVector, @QueryVector) < @SimilarityThreshold';

    SET @SQL = @SQL + @WhereClause;

    -- Add ORDER BY
    IF @QueryVector IS NOT NULL
        SET @SQL = @SQL + N' ORDER BY VECTOR_DISTANCE(''cosine'', ce.EmbeddingVector, @QueryVector)';
    ELSE
        SET @SQL = @SQL + N' ORDER BY mc.RelevanceScore DESC, mc.ComponentName';

    -- Execute dynamic query
    EXEC sp_executesql @SQL,
        N'@ModelId UNIQUEIDENTIFIER, @ComponentType NVARCHAR(100), @SearchQuery NVARCHAR(500),
          @SimilarityThreshold FLOAT, @MaxResults INT, @UserId NVARCHAR(128), @QueryVector VECTOR(1536)',
        @ModelId, @ComponentType, @SearchQuery, @SimilarityThreshold, @MaxResults, @UserId, @QueryVector;
END;
GO