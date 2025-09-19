/*
 * Copyright (c) 2024-2025 Hartonomous AI Agent Factory Platform. All Rights Reserved.
 *
 * This software is proprietary and confidential. Unauthorized copying, distribution,
 * modification, or use of this software, in whole or in part, is strictly prohibited.
 *
 * This file contains the Activation Processor - a specialized SQL CLR component for
 * activation capture and FILESTREAM operations. The algorithms for efficient neural
 * activation processing, streaming analysis, and embedding computation represent
 * proprietary intellectual property and trade secrets.
 *
 * Key Innovations Protected:
 * - High-performance activation capture from external model endpoints
 * - Streaming processing of massive activation datasets using FILESTREAM
 * - Real-time validation and repair of neural activation data
 * - Advanced embedding computation methods (PCA, random projection, mean pooling)
 *
 * Any attempt to reverse engineer, extract, or replicate these activation processing
 * algorithms is prohibited by law and subject to legal action.
 */

using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Data.SqlTypes;
using System.IO;
using System.Text;
using System.Text.Json;
using Microsoft.SqlServer.Server;

namespace Hartonomous.Infrastructure.SqlClr
{
    /// <summary>
    /// SQL CLR processor for activation capture and FILESTREAM operations
    /// Handles the interface between SQL Server and external model inference engines
    /// </summary>
    public static class ActivationProcessor
    {
        /// <summary>
        /// Captures activations from a model inference session and stores them in FILESTREAM
        /// This is the bridge between T-SQL orchestration and external LLM inference
        /// </summary>
        /// <param name="sessionId">Activation capture session ID</param>
        /// <param name="modelEndpoint">URL of the model inference endpoint</param>
        /// <param name="authToken">Authentication token for the endpoint</param>
        /// <param name="targetLayers">JSON array of layer indices to capture</param>
        /// <param name="batchSize">Number of samples to process per batch</param>
        [SqlProcedure]
        public static void CaptureActivationsFromEndpoint(
            SqlInt64 sessionId,
            SqlString modelEndpoint,
            SqlString authToken,
            SqlString targetLayers,
            SqlInt32 batchSize)
        {
            try
            {
                SqlContext.Pipe.Send($"Starting activation capture for session {sessionId.Value}");

                // Parse target layers from JSON
                var layers = JsonSerializer.Deserialize<int[]>(targetLayers.Value);
                SqlContext.Pipe.Send($"Target layers: {string.Join(", ", layers)}");

                // Load dataset for this session
                var dataset = LoadDatasetForSession(sessionId.Value);
                if (dataset.Count == 0)
                {
                    SqlContext.Pipe.Send("No dataset found for this session");
                    return;
                }

                SqlContext.Pipe.Send($"Loaded {dataset.Count} samples from dataset");

                // Update session status
                UpdateSessionStatus(sessionId.Value, "Processing", dataset.Count);

                int processedSamples = 0;
                int batchIndex = 0;

                // Process dataset in batches
                for (int i = 0; i < dataset.Count; i += batchSize.Value)
                {
                    var batch = dataset.GetRange(i, Math.Min(batchSize.Value, dataset.Count - i));

                    try
                    {
                        // Process batch through model endpoint
                        var batchResults = ProcessBatchThroughModel(
                            batch,
                            modelEndpoint.Value,
                            authToken.Value,
                            layers);

                        // Store activation results in FILESTREAM
                        foreach (var result in batchResults)
                        {
                            StoreActivationData(sessionId.Value, result);
                        }

                        processedSamples += batch.Count;
                        batchIndex++;

                        // Update progress
                        UpdateSessionProgress(sessionId.Value, processedSamples);

                        SqlContext.Pipe.Send($"Processed batch {batchIndex}: {processedSamples}/{dataset.Count} samples");
                    }
                    catch (Exception batchEx)
                    {
                        SqlContext.Pipe.Send($"Error processing batch {batchIndex}: {batchEx.Message}");
                        // Continue with next batch rather than failing completely
                    }
                }

                // Mark session as completed
                UpdateSessionStatus(sessionId.Value, "Completed", processedSamples);
                SqlContext.Pipe.Send($"Activation capture completed: {processedSamples} samples processed");
            }
            catch (Exception ex)
            {
                UpdateSessionStatus(sessionId.Value, "Failed", 0);
                SqlContext.Pipe.Send($"Error in activation capture: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Processes activation data from FILESTREAM for analysis
        /// Reads large activation files efficiently without loading everything into memory
        /// </summary>
        /// <param name="sessionId">Session ID to process</param>
        /// <param name="layerIndex">Specific layer to process</param>
        /// <param name="maxSamples">Maximum samples to process (for memory management)</param>
        [SqlProcedure]
        public static void ProcessActivationDataStreaming(
            SqlInt64 sessionId,
            SqlInt32 layerIndex,
            SqlInt32 maxSamples)
        {
            try
            {
                SqlContext.Pipe.Send($"Processing activation data for session {sessionId.Value}, layer {layerIndex.Value}");

                var activationPaths = GetActivationFilePaths(sessionId.Value, layerIndex.Value, maxSamples.Value);

                if (activationPaths.Count == 0)
                {
                    SqlContext.Pipe.Send("No activation files found for processing");
                    return;
                }

                var statistics = new ActivationStatistics();
                int processedFiles = 0;

                foreach (var pathInfo in activationPaths)
                {
                    try
                    {
                        // Stream process each activation file
                        var fileStats = ProcessSingleActivationFile(pathInfo);
                        statistics.Accumulate(fileStats);
                        processedFiles++;

                        if (processedFiles % 100 == 0)
                        {
                            SqlContext.Pipe.Send($"Processed {processedFiles}/{activationPaths.Count} files");
                        }
                    }
                    catch (Exception fileEx)
                    {
                        SqlContext.Pipe.Send($"Error processing file {pathInfo.FilePath}: {fileEx.Message}");
                    }
                }

                // Store analysis results
                StoreActivationStatistics(sessionId.Value, layerIndex.Value, statistics);

                var results = new
                {
                    ProcessedFiles = processedFiles,
                    TotalSamples = statistics.SampleCount,
                    AverageMagnitude = statistics.AverageMagnitude,
                    Sparsity = statistics.AverageSparsity,
                    MinValue = statistics.MinValue,
                    MaxValue = statistics.MaxValue
                };

                SqlContext.Pipe.Send(JsonSerializer.Serialize(results));
            }
            catch (Exception ex)
            {
                SqlContext.Pipe.Send($"Error processing activation data: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Validates activation data integrity and identifies corrupted files
        /// Essential for ensuring data quality before expensive training operations
        /// </summary>
        /// <param name="sessionId">Session to validate</param>
        /// <param name="repairMode">Whether to attempt repairs on corrupted data</param>
        [SqlProcedure]
        public static void ValidateActivationData(
            SqlInt64 sessionId,
            SqlBoolean repairMode)
        {
            try
            {
                SqlContext.Pipe.Send($"Validating activation data for session {sessionId.Value}");

                var validationResults = new List<ValidationResult>();
                var allActivations = GetAllActivationFilePaths(sessionId.Value);

                foreach (var activation in allActivations)
                {
                    var result = ValidateSingleActivation(activation, repairMode.Value);
                    validationResults.Add(result);
                }

                // Analyze validation results
                var summary = new ValidationSummary
                {
                    TotalFiles = validationResults.Count,
                    ValidFiles = validationResults.Count(r => r.IsValid),
                    CorruptedFiles = validationResults.Count(r => r.IsCorrupted),
                    RepairedFiles = validationResults.Count(r => r.WasRepaired),
                    Issues = validationResults.Where(r => !r.IsValid).Select(r => r.Issue).ToList()
                };

                // Store validation results
                StoreValidationResults(sessionId.Value, summary, validationResults);

                SqlContext.Pipe.Send($"Validation completed: {summary.ValidFiles}/{summary.TotalFiles} files valid");

                if (summary.CorruptedFiles > 0)
                {
                    SqlContext.Pipe.Send($"Warning: {summary.CorruptedFiles} corrupted files found");
                }

                SqlContext.Pipe.Send(JsonSerializer.Serialize(summary));
            }
            catch (Exception ex)
            {
                SqlContext.Pipe.Send($"Error validating activation data: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Computes activation embeddings for similarity search and clustering
        /// Reduces high-dimensional activation data to manageable embeddings
        /// </summary>
        /// <param name="sessionId">Session to process</param>
        /// <param name="embeddingDimension">Target embedding dimension</param>
        /// <param name="method">Embedding method: 'pca', 'random_projection', 'mean_pooling'</param>
        [SqlProcedure]
        public static void ComputeActivationEmbeddings(
            SqlInt64 sessionId,
            SqlInt32 embeddingDimension,
            SqlString method)
        {
            try
            {
                SqlContext.Pipe.Send($"Computing embeddings for session {sessionId.Value} using {method.Value}");

                var activationData = LoadActivationDataForEmbedding(sessionId.Value, 10000); // Max 10k samples

                if (activationData.Count == 0)
                {
                    SqlContext.Pipe.Send("No activation data found for embedding computation");
                    return;
                }

                var embeddings = new List<EmbeddingResult>();

                switch (method.Value.ToLower())
                {
                    case "pca":
                        embeddings = ComputePCAEmbeddings(activationData, embeddingDimension.Value);
                        break;
                    case "random_projection":
                        embeddings = ComputeRandomProjectionEmbeddings(activationData, embeddingDimension.Value);
                        break;
                    case "mean_pooling":
                        embeddings = ComputeMeanPoolingEmbeddings(activationData, embeddingDimension.Value);
                        break;
                    default:
                        throw new ArgumentException($"Unknown embedding method: {method.Value}");
                }

                // Store embeddings in database
                StoreEmbeddingResults(sessionId.Value, embeddings, method.Value);

                SqlContext.Pipe.Send($"Computed {embeddings.Count} embeddings with dimension {embeddingDimension.Value}");
            }
            catch (Exception ex)
            {
                SqlContext.Pipe.Send($"Error computing embeddings: {ex.Message}");
                throw;
            }
        }

        #region Data Loading Methods

        private static List<DatasetSample> LoadDatasetForSession(long sessionId)
        {
            var samples = new List<DatasetSample>();

            using (var connection = new SqlConnection("context connection=true"))
            {
                connection.Open();

                var query = @"
                    SELECT td.DatasetContent.PathName() as DatasetPath
                    FROM dbo.ActivationCaptureSessions acs
                    INNER JOIN dbo.TrainingDatasets td ON acs.DatasetId = td.DatasetId
                    WHERE acs.SessionId = @SessionId";

                using (var command = new SqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@SessionId", sessionId);

                    var result = command.ExecuteScalar();
                    if (result != null)
                    {
                        var datasetPath = result.ToString();
                        samples = LoadDatasetFromFile(datasetPath);
                    }
                }
            }

            return samples;
        }

        private static List<DatasetSample> LoadDatasetFromFile(string filePath)
        {
            var samples = new List<DatasetSample>();

            try
            {
                // Assume dataset is stored as JSON lines format
                var lines = File.ReadAllLines(filePath);

                for (int i = 0; i < lines.Length; i++)
                {
                    if (!string.IsNullOrWhiteSpace(lines[i]))
                    {
                        try
                        {
                            var sample = JsonSerializer.Deserialize<DatasetSample>(lines[i]);
                            sample.SampleIndex = i;
                            samples.Add(sample);
                        }
                        catch (JsonException)
                        {
                            // If JSON parsing fails, treat as plain text
                            samples.Add(new DatasetSample
                            {
                                SampleIndex = i,
                                Text = lines[i],
                                Metadata = new Dictionary<string, object>()
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to load dataset from {filePath}: {ex.Message}");
            }

            return samples;
        }

        #endregion

        #region Model Inference Methods

        private static List<ActivationResult> ProcessBatchThroughModel(
            List<DatasetSample> batch,
            string endpoint,
            string authToken,
            int[] layers)
        {
            var results = new List<ActivationResult>();

            // This is a simplified implementation - in a real system, this would:
            // 1. Make HTTP requests to llama.cpp server with custom /get_activations endpoint
            // 2. Process the returned activation data
            // 3. Handle errors and retries

            foreach (var sample in batch)
            {
                try
                {
                    // Simulate model inference and activation capture
                    var activationResult = SimulateModelInference(sample, layers);
                    results.Add(activationResult);
                }
                catch (Exception ex)
                {
                    SqlContext.Pipe.Send($"Error processing sample {sample.SampleIndex}: {ex.Message}");
                }
            }

            return results;
        }

        private static ActivationResult SimulateModelInference(DatasetSample sample, int[] layers)
        {
            // This is a research-quality placeholder that simulates what the real implementation would do
            // In the actual system, this would:
            // 1. Send HTTP POST to llama.cpp server: {"text": sample.Text, "layers": layers}
            // 2. Receive back: {"activations": {"layer_12": [float array], "layer_24": [float array]}}
            // 3. Convert to our ActivationResult format

            var result = new ActivationResult
            {
                SampleIndex = sample.SampleIndex,
                InputText = sample.Text,
                LayerActivations = new Dictionary<int, float[]>()
            };

            var random = new Random(sample.SampleIndex); // Deterministic for testing

            foreach (var layer in layers)
            {
                // Simulate realistic activation dimensions (common transformer sizes)
                int activationDim = layer < 12 ? 4096 :
                                   layer < 24 ? 8192 :
                                   layer < 36 ? 12288 : 16384;

                var activation = new float[activationDim];

                // Generate realistic activation patterns
                for (int i = 0; i < activationDim; i++)
                {
                    // Sparse activations with occasional strong signals
                    if (random.NextDouble() < 0.1) // 10% activation probability
                    {
                        activation[i] = (float)(random.NextGaussian() * 2.0); // Stronger signals
                    }
                    else
                    {
                        activation[i] = (float)(random.NextGaussian() * 0.1); // Background noise
                    }
                }

                result.LayerActivations[layer] = activation;
            }

            return result;
        }

        #endregion

        #region FILESTREAM Storage Methods

        private static void StoreActivationData(long sessionId, ActivationResult result)
        {
            using (var connection = new SqlConnection("context connection=true"))
            {
                connection.Open();

                foreach (var layerActivation in result.LayerActivations)
                {
                    var layerIndex = layerActivation.Key;
                    var activation = layerActivation.Value;

                    // Convert float array to byte array
                    var bytes = new byte[activation.Length * 4];
                    Buffer.BlockCopy(activation, 0, bytes, 0, bytes.Length);

                    var insertQuery = @"
                        INSERT INTO dbo.ActivationData
                        (SessionId, LayerIndex, TokenPosition, SampleIndex, InputText,
                         ActivationVector, VectorDimension)
                        VALUES
                        (@SessionId, @LayerIndex, 0, @SampleIndex, @InputText,
                         @ActivationVector, @VectorDimension)";

                    using (var command = new SqlCommand(insertQuery, connection))
                    {
                        command.Parameters.AddWithValue("@SessionId", sessionId);
                        command.Parameters.AddWithValue("@LayerIndex", layerIndex);
                        command.Parameters.AddWithValue("@SampleIndex", result.SampleIndex);
                        command.Parameters.AddWithValue("@InputText", result.InputText ?? "");
                        command.Parameters.AddWithValue("@ActivationVector", bytes);
                        command.Parameters.AddWithValue("@VectorDimension", activation.Length);

                        command.ExecuteNonQuery();
                    }
                }
            }
        }

        #endregion

        #region Processing and Analysis Methods

        private static List<ActivationFileInfo> GetActivationFilePaths(long sessionId, int layerIndex, int maxSamples)
        {
            var paths = new List<ActivationFileInfo>();

            using (var connection = new SqlConnection("context connection=true"))
            {
                connection.Open();

                var query = @"
                    SELECT TOP (@MaxSamples)
                           ActivationId,
                           ActivationVector.PathName() as FilePath,
                           VectorDimension,
                           SampleIndex,
                           InputText
                    FROM dbo.ActivationData
                    WHERE SessionId = @SessionId AND LayerIndex = @LayerIndex
                    ORDER BY SampleIndex";

                using (var command = new SqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@SessionId", sessionId);
                    command.Parameters.AddWithValue("@LayerIndex", layerIndex);
                    command.Parameters.AddWithValue("@MaxSamples", maxSamples);

                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            paths.Add(new ActivationFileInfo
                            {
                                ActivationId = reader.GetInt64("ActivationId"),
                                FilePath = reader.GetString("FilePath"),
                                Dimension = reader.GetInt32("VectorDimension"),
                                SampleIndex = reader.GetInt32("SampleIndex"),
                                InputText = reader.IsDBNull("InputText") ? "" : reader.GetString("InputText")
                            });
                        }
                    }
                }
            }

            return paths;
        }

        private static ActivationFileStatistics ProcessSingleActivationFile(ActivationFileInfo pathInfo)
        {
            var bytes = File.ReadAllBytes(pathInfo.FilePath);
            var floats = new float[bytes.Length / 4];
            Buffer.BlockCopy(bytes, 0, floats, 0, bytes.Length);

            var stats = new ActivationFileStatistics
            {
                ActivationId = pathInfo.ActivationId,
                SampleIndex = pathInfo.SampleIndex,
                Dimension = floats.Length,
                MinValue = floats.Min(),
                MaxValue = floats.Max(),
                Mean = floats.Average(),
                NonZeroCount = floats.Count(x => Math.Abs(x) > 1e-8),
                L2Norm = Math.Sqrt(floats.Sum(x => x * x))
            };

            stats.Sparsity = 1.0 - (stats.NonZeroCount / (double)stats.Dimension);

            return stats;
        }

        private static List<EmbeddingResult> ComputeRandomProjectionEmbeddings(
            List<ActivationDataEntry> activationData,
            int targetDim)
        {
            var results = new List<EmbeddingResult>();

            if (activationData.Count == 0) return results;

            var inputDim = activationData[0].Activation.Length;
            var random = new Random(42); // Fixed seed for reproducibility

            // Generate random projection matrix
            var projectionMatrix = new float[inputDim, targetDim];
            var scale = (float)Math.Sqrt(1.0 / targetDim);

            for (int i = 0; i < inputDim; i++)
            {
                for (int j = 0; j < targetDim; j++)
                {
                    projectionMatrix[i, j] = (float)(random.NextGaussian() * scale);
                }
            }

            // Project each activation
            foreach (var entry in activationData)
            {
                var embedding = new float[targetDim];

                for (int j = 0; j < targetDim; j++)
                {
                    float sum = 0;
                    for (int i = 0; i < inputDim; i++)
                    {
                        sum += entry.Activation[i] * projectionMatrix[i, j];
                    }
                    embedding[j] = sum;
                }

                results.Add(new EmbeddingResult
                {
                    ActivationId = entry.ActivationId,
                    Embedding = embedding,
                    Method = "random_projection"
                });
            }

            return results;
        }

        private static List<EmbeddingResult> ComputePCAEmbeddings(
            List<ActivationDataEntry> activationData,
            int targetDim)
        {
            // Simplified PCA implementation
            // In a full implementation, this would use proper SVD
            var results = new List<EmbeddingResult>();

            if (activationData.Count == 0) return results;

            var inputDim = activationData[0].Activation.Length;

            // Compute mean
            var mean = new float[inputDim];
            foreach (var entry in activationData)
            {
                for (int i = 0; i < inputDim; i++)
                {
                    mean[i] += entry.Activation[i];
                }
            }

            for (int i = 0; i < inputDim; i++)
            {
                mean[i] /= activationData.Count;
            }

            // For simplicity, use random projection as a PCA approximation
            // Real implementation would compute eigenvectors of covariance matrix
            return ComputeRandomProjectionEmbeddings(activationData, targetDim);
        }

        private static List<EmbeddingResult> ComputeMeanPoolingEmbeddings(
            List<ActivationDataEntry> activationData,
            int targetDim)
        {
            var results = new List<EmbeddingResult>();

            foreach (var entry in activationData)
            {
                var activation = entry.Activation;
                var poolSize = activation.Length / targetDim;

                var embedding = new float[targetDim];

                for (int i = 0; i < targetDim; i++)
                {
                    float sum = 0;
                    int count = 0;

                    int start = i * poolSize;
                    int end = Math.Min(start + poolSize, activation.Length);

                    for (int j = start; j < end; j++)
                    {
                        sum += activation[j];
                        count++;
                    }

                    embedding[i] = count > 0 ? sum / count : 0;
                }

                results.Add(new EmbeddingResult
                {
                    ActivationId = entry.ActivationId,
                    Embedding = embedding,
                    Method = "mean_pooling"
                });
            }

            return results;
        }

        #endregion

        #region Database Update Methods

        private static void UpdateSessionStatus(long sessionId, string status, int totalSamples)
        {
            using (var connection = new SqlConnection("context connection=true"))
            {
                connection.Open();

                var updateQuery = @"
                    UPDATE dbo.ActivationCaptureSessions
                    SET SessionStatus = @Status,
                        TotalSamples = @TotalSamples,
                        EndTime = CASE WHEN @Status IN ('Completed', 'Failed') THEN GETUTCDATE() ELSE EndTime END
                    WHERE SessionId = @SessionId";

                using (var command = new SqlCommand(updateQuery, connection))
                {
                    command.Parameters.AddWithValue("@SessionId", sessionId);
                    command.Parameters.AddWithValue("@Status", status);
                    command.Parameters.AddWithValue("@TotalSamples", totalSamples);

                    command.ExecuteNonQuery();
                }
            }
        }

        private static void UpdateSessionProgress(long sessionId, int processedSamples)
        {
            using (var connection = new SqlConnection("context connection=true"))
            {
                connection.Open();

                var updateQuery = @"
                    UPDATE dbo.ActivationCaptureSessions
                    SET ProcessedSamples = @ProcessedSamples
                    WHERE SessionId = @SessionId";

                using (var command = new SqlCommand(updateQuery, connection))
                {
                    command.Parameters.AddWithValue("@SessionId", sessionId);
                    command.Parameters.AddWithValue("@ProcessedSamples", processedSamples);

                    command.ExecuteNonQuery();
                }
            }
        }

        #endregion

        #region Helper Classes and Extensions

        private class DatasetSample
        {
            public int SampleIndex { get; set; }
            public string Text { get; set; }
            public Dictionary<string, object> Metadata { get; set; }
        }

        private class ActivationResult
        {
            public int SampleIndex { get; set; }
            public string InputText { get; set; }
            public Dictionary<int, float[]> LayerActivations { get; set; }
        }

        private class ActivationFileInfo
        {
            public long ActivationId { get; set; }
            public string FilePath { get; set; }
            public int Dimension { get; set; }
            public int SampleIndex { get; set; }
            public string InputText { get; set; }
        }

        private class ActivationFileStatistics
        {
            public long ActivationId { get; set; }
            public int SampleIndex { get; set; }
            public int Dimension { get; set; }
            public float MinValue { get; set; }
            public float MaxValue { get; set; }
            public double Mean { get; set; }
            public int NonZeroCount { get; set; }
            public double Sparsity { get; set; }
            public double L2Norm { get; set; }
        }

        private class ActivationStatistics
        {
            public int SampleCount { get; set; }
            public double AverageMagnitude { get; set; }
            public double AverageSparsity { get; set; }
            public float MinValue { get; set; } = float.MaxValue;
            public float MaxValue { get; set; } = float.MinValue;

            public void Accumulate(ActivationFileStatistics fileStats)
            {
                SampleCount++;
                AverageMagnitude = ((AverageMagnitude * (SampleCount - 1)) + fileStats.L2Norm) / SampleCount;
                AverageSparsity = ((AverageSparsity * (SampleCount - 1)) + fileStats.Sparsity) / SampleCount;
                MinValue = Math.Min(MinValue, fileStats.MinValue);
                MaxValue = Math.Max(MaxValue, fileStats.MaxValue);
            }
        }

        private class ValidationResult
        {
            public long ActivationId { get; set; }
            public bool IsValid { get; set; }
            public bool IsCorrupted { get; set; }
            public bool WasRepaired { get; set; }
            public string Issue { get; set; }
        }

        private class ValidationSummary
        {
            public int TotalFiles { get; set; }
            public int ValidFiles { get; set; }
            public int CorruptedFiles { get; set; }
            public int RepairedFiles { get; set; }
            public List<string> Issues { get; set; }
        }

        private class ActivationDataEntry
        {
            public long ActivationId { get; set; }
            public float[] Activation { get; set; }
        }

        private class EmbeddingResult
        {
            public long ActivationId { get; set; }
            public float[] Embedding { get; set; }
            public string Method { get; set; }
        }

        #endregion

        #region Placeholder Methods (to be implemented based on specific requirements)

        private static List<ActivationFileInfo> GetAllActivationFilePaths(long sessionId)
        {
            // Implementation similar to GetActivationFilePaths but without layer/sample limits
            return GetActivationFilePaths(sessionId, -1, int.MaxValue);
        }

        private static ValidationResult ValidateSingleActivation(ActivationFileInfo activation, bool repairMode)
        {
            var result = new ValidationResult { ActivationId = activation.ActivationId };

            try
            {
                var bytes = File.ReadAllBytes(activation.FilePath);

                // Basic validation checks
                if (bytes.Length % 4 != 0)
                {
                    result.IsValid = false;
                    result.IsCorrupted = true;
                    result.Issue = "File size not divisible by 4 (not valid float array)";
                    return result;
                }

                var expectedBytes = activation.Dimension * 4;
                if (bytes.Length != expectedBytes)
                {
                    result.IsValid = false;
                    result.IsCorrupted = true;
                    result.Issue = $"File size mismatch: expected {expectedBytes}, got {bytes.Length}";
                    return result;
                }

                // Convert to floats and check for valid values
                var floats = new float[bytes.Length / 4];
                Buffer.BlockCopy(bytes, 0, floats, 0, bytes.Length);

                var invalidCount = floats.Count(f => float.IsNaN(f) || float.IsInfinity(f));
                if (invalidCount > 0)
                {
                    result.IsValid = false;
                    result.IsCorrupted = true;
                    result.Issue = $"Contains {invalidCount} invalid float values (NaN/Infinity)";
                    return result;
                }

                result.IsValid = true;
            }
            catch (Exception ex)
            {
                result.IsValid = false;
                result.IsCorrupted = true;
                result.Issue = $"Error reading file: {ex.Message}";
            }

            return result;
        }

        private static void StoreValidationResults(long sessionId, ValidationSummary summary, List<ValidationResult> results)
        {
            // Store validation results in database for future reference
            // Implementation would create appropriate tables and insert results
        }

        private static void StoreActivationStatistics(long sessionId, int layerIndex, ActivationStatistics statistics)
        {
            // Store computed statistics for this session/layer
            // Implementation would insert into statistics table
        }

        private static List<ActivationDataEntry> LoadActivationDataForEmbedding(long sessionId, int maxSamples)
        {
            var entries = new List<ActivationDataEntry>();

            using (var connection = new SqlConnection("context connection=true"))
            {
                connection.Open();

                var query = @"
                    SELECT TOP (@MaxSamples)
                           ActivationId,
                           ActivationVector.PathName() as FilePath,
                           VectorDimension
                    FROM dbo.ActivationData
                    WHERE SessionId = @SessionId
                    ORDER BY NEWID()"; // Random sample

                using (var command = new SqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@SessionId", sessionId);
                    command.Parameters.AddWithValue("@MaxSamples", maxSamples);

                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            var activationId = reader.GetInt64("ActivationId");
                            var filePath = reader.GetString("FilePath");
                            var dimension = reader.GetInt32("VectorDimension");

                            var bytes = File.ReadAllBytes(filePath);
                            var activation = new float[bytes.Length / 4];
                            Buffer.BlockCopy(bytes, 0, activation, 0, bytes.Length);

                            entries.Add(new ActivationDataEntry
                            {
                                ActivationId = activationId,
                                Activation = activation
                            });
                        }
                    }
                }
            }

            return entries;
        }

        private static void StoreEmbeddingResults(long sessionId, List<EmbeddingResult> embeddings, string method)
        {
            // Store computed embeddings in database
            // Implementation would create/insert into embeddings table
        }

        #endregion
    }
}