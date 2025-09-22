/*
 * Model Ingestion Stored Procedure
 * Ingests GGUF model files into SQL Server with FILESTREAM storage
 * Extracts components and stores them with vector embeddings
 */

CREATE OR ALTER PROCEDURE sp_IngestModel
    @ModelName NVARCHAR(255),
    @ModelPath NVARCHAR(500),
    @UserId NVARCHAR(128),
    @ModelId UNIQUEIDENTIFIER OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @ErrorMessage NVARCHAR(4000);

    BEGIN TRY
        BEGIN TRANSACTION;

        -- Generate new model ID
        SET @ModelId = NEWID();

        -- Create model record
        INSERT INTO FoundationModels (
            ModelId,
            ModelName,
            ModelFormat,
            FilePath,
            UserId,
            CreatedAt,
            Status
        )
        VALUES (
            @ModelId,
            @ModelName,
            'GGUF',
            @ModelPath,
            @UserId,
            GETUTCDATE(),
            'Processing'
        );

        -- Load model file into FILESTREAM (this would be called from application)
        -- The actual file loading happens in the application layer

        -- Parse model structure using SQL CLR
        DECLARE @ModelStructure NVARCHAR(MAX);
        EXEC @ModelStructure = clr_ParseModelFile @ModelPath, 'GGUF';

        -- Extract and store model metadata
        DECLARE @Metadata NVARCHAR(MAX) = JSON_QUERY(@ModelStructure, '$.metadata');

        UPDATE FoundationModels
        SET
            Metadata = @Metadata,
            ParameterCount = CAST(JSON_VALUE(@ModelStructure, '$.parameter_count') AS BIGINT),
            ModelSizeBytes = CAST(JSON_VALUE(@ModelStructure, '$.file_size') AS BIGINT)
        WHERE ModelId = @ModelId;

        -- Extract layers from model structure
        DECLARE @Layers NVARCHAR(MAX) = JSON_QUERY(@ModelStructure, '$.layers');

        -- Parse and insert layers
        INSERT INTO ModelLayers (
            LayerId,
            ModelId,
            LayerName,
            LayerType,
            LayerIndex,
            Configuration,
            UserId,
            CreatedAt
        )
        SELECT
            NEWID(),
            @ModelId,
            JSON_VALUE(layer.value, '$.name'),
            JSON_VALUE(layer.value, '$.type'),
            CAST(JSON_VALUE(layer.value, '$.index') AS INT),
            layer.value,
            @UserId,
            GETUTCDATE()
        FROM OPENJSON(@Layers) AS layer;

        -- Mark model as ready
        UPDATE FoundationModels
        SET Status = 'Ready', ProcessedAt = GETUTCDATE()
        WHERE ModelId = @ModelId;

        COMMIT TRANSACTION;

        -- Return success
        SELECT
            @ModelId AS ModelId,
            'SUCCESS' AS Status,
            'Model ingested successfully' AS Message;

    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0
            ROLLBACK TRANSACTION;

        SET @ErrorMessage = ERROR_MESSAGE();

        -- Update model status to failed
        UPDATE FoundationModels
        SET Status = 'Failed', ErrorMessage = @ErrorMessage
        WHERE ModelId = @ModelId;

        -- Return error
        SELECT
            @ModelId AS ModelId,
            'ERROR' AS Status,
            @ErrorMessage AS Message;

    END CATCH
END;
GO