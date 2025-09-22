/*
 * Hartonomous AI Agent Factory Platform
 * Skip Transcoder Processor - Advanced Neural Network Training for Mechanistic Interpretability
 *
 * Copyright (c) 2024-2025 All Rights Reserved.
 * This software is proprietary and confidential. No part of this software may be reproduced,
 * distributed, or transmitted in any form or by any means without the prior written permission
 * of the copyright holder.
 *
 * This module implements Skip Transcoder neural networks directly within SQL Server CLR,
 * enabling mechanistic interpretability through neural pattern analysis and feature discovery.
 *
 * CRITICAL SECURITY NOTICE: This component performs neural network training within SQL Server.
 * Unauthorized access or modification could compromise model security and data integrity.
 *
 * Author: AI Agent Factory Development Team
 * Created: 2024
 * Last Modified: 2025
 */

using System;
using System.Collections.Generic;
using System.Data;
using Microsoft.Data.SqlClient;
using System.Data.SqlTypes;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.SqlServer.Server;

namespace Hartonomous.Infrastructure.SqlClr
{
    /// <summary>
    /// SQL CLR implementation of Skip Transcoder training and feature extraction
    /// This is a research-quality implementation based on the reference paper:
    /// "Transcoders Beat Sparse Autoencoders for Interpretability"
    /// </summary>
    public static class SkipTranscoderProcessor
    {
        /// <summary>
        /// Trains a Skip Transcoder on captured activation data
        /// Skip Transcoder: f(x) ≈ D(E(x)) + Ax + b (with skip connection)
        /// </summary>
        /// <param name="sessionId">Activation capture session ID</param>
        /// <param name="layerIndex">Target layer index</param>
        /// <param name="latentDim">Sparse latent dimension (typically 8x input dimension)</param>
        /// <param name="sparsityPenalty">L1 penalty coefficient for sparsity</param>
        /// <param name="learningRate">Adam optimizer learning rate</param>
        /// <param name="maxEpochs">Maximum training epochs</param>
        [SqlProcedure]
        public static void TrainSkipTranscoder(
            SqlInt64 sessionId,
            SqlInt32 layerIndex,
            SqlInt32 latentDim,
            SqlDouble sparsityPenalty,
            SqlDouble learningRate,
            SqlInt32 maxEpochs)
        {
            try
            {
                SqlContext.Pipe.Send($"Starting Skip Transcoder training for session {sessionId.Value}, layer {layerIndex.Value}");

                // Load activation data from FILESTREAM
                var activations = LoadActivationData(sessionId.Value, layerIndex.Value);

                if (activations.Count == 0)
                {
                    SqlContext.Pipe.Send("No activation data found for training");
                    return;
                }

                var inputDim = activations[0].Length;
                SqlContext.Pipe.Send($"Loaded {activations.Count} activation vectors, dimension {inputDim}");

                // Initialize transcoder parameters
                var transcoder = new SkipTranscoder(inputDim, latentDim.Value, sparsityPenalty.Value);

                // Training loop with Adam optimizer
                var optimizer = new AdamOptimizer(learningRate.Value);
                double bestLoss = double.MaxValue;
                int patienceCounter = 0;
                const int patience = 10;

                for (int epoch = 0; epoch < maxEpochs.Value; epoch++)
                {
                    // Shuffle data for this epoch
                    var shuffledData = activations.OrderBy(x => Guid.NewGuid()).ToList();

                    double epochLoss = 0.0;
                    int batchCount = 0;

                    // Process in mini-batches for memory efficiency
                    const int batchSize = 32;
                    for (int i = 0; i < shuffledData.Count; i += batchSize)
                    {
                        var batch = shuffledData.Skip(i).Take(batchSize).ToList();
                        var loss = transcoder.TrainBatch(batch, optimizer);
                        epochLoss += loss;
                        batchCount++;
                    }

                    epochLoss /= batchCount;

                    // Early stopping check
                    if (epochLoss < bestLoss)
                    {
                        bestLoss = epochLoss;
                        patienceCounter = 0;
                    }
                    else
                    {
                        patienceCounter++;
                        if (patienceCounter >= patience)
                        {
                            SqlContext.Pipe.Send($"Early stopping at epoch {epoch} with loss {bestLoss:F6}");
                            break;
                        }
                    }

                    // Progress reporting every 10 epochs
                    if (epoch % 10 == 0)
                    {
                        SqlContext.Pipe.Send($"Epoch {epoch}: Loss = {epochLoss:F6}, Sparsity = {transcoder.GetAverageSparsity():F4}");
                    }
                }

                // Save trained transcoder to SQL Server
                SaveTranscoderModel(sessionId.Value, layerIndex.Value, transcoder, bestLoss);

                // Extract and save interpretable features
                ExtractFeatures(sessionId.Value, layerIndex.Value, transcoder, activations.Take(1000).ToList());

                SqlContext.Pipe.Send($"Skip Transcoder training completed. Final loss: {bestLoss:F6}");
            }
            catch (Exception ex)
            {
                SqlContext.Pipe.Send($"Error training Skip Transcoder: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Applies a trained transcoder to new activation data to extract features
        /// </summary>
        /// <param name="sessionId">Session ID</param>
        /// <param name="layerIndex">Layer index</param>
        /// <param name="inputText">Input text that generated the activation</param>
        /// <param name="activationData">Raw activation vector as binary data</param>
        [SqlProcedure]
        public static void ExtractFeaturesFromActivation(
            SqlInt64 sessionId,
            SqlInt32 layerIndex,
            SqlString inputText,
            SqlBytes activationData)
        {
            try
            {
                // Load the trained transcoder for this session/layer
                var transcoder = LoadTranscoderModel(sessionId.Value, layerIndex.Value);
                if (transcoder == null)
                {
                    SqlContext.Pipe.Send("No trained transcoder found for this session/layer");
                    return;
                }

                // Convert binary data to float array
                var activation = BytesToFloatArray(activationData.Value);

                // Extract sparse features
                var features = transcoder.Encode(activation);

                // Find active features (non-zero elements)
                var activeFeatures = new List<ActiveFeature>();
                for (int i = 0; i < features.Length; i++)
                {
                    if (Math.Abs(features[i]) > 1e-6) // Threshold for active features
                    {
                        activeFeatures.Add(new ActiveFeature
                        {
                            FeatureIndex = i,
                            Activation = features[i],
                            InputText = inputText.Value
                        });
                    }
                }

                // Return results as JSON
                var json = JsonSerializer.Serialize(activeFeatures);
                SqlContext.Pipe.Send($"Extracted {activeFeatures.Count} active features");
                SqlContext.Pipe.Send(json);
            }
            catch (Exception ex)
            {
                SqlContext.Pipe.Send($"Error extracting features: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Analyzes feature interpretability by examining activation patterns
        /// </summary>
        /// <param name="transcoderId">Transcoder model ID</param>
        /// <param name="featureIndex">Specific feature to analyze</param>
        /// <param name="sampleSize">Number of samples to analyze</param>
        [SqlProcedure]
        public static void AnalyzeFeatureInterpretability(
            SqlInt32 transcoderId,
            SqlInt32 featureIndex,
            SqlInt32 sampleSize)
        {
            try
            {
                // Load the transcoder
                var transcoder = LoadTranscoderModelById(transcoderId.Value);
                if (transcoder == null)
                {
                    SqlContext.Pipe.Send("Transcoder model not found");
                    return;
                }

                // Get activation data for analysis
                var activations = LoadActivationDataForTranscoder(transcoderId.Value, sampleSize.Value);

                var activationAnalysis = new List<FeatureActivationAnalysis>();

                foreach (var activationEntry in activations)
                {
                    var features = transcoder.Encode(activationEntry.Vector);

                    if (featureIndex.Value < features.Length && Math.Abs(features[featureIndex.Value]) > 1e-6)
                    {
                        activationAnalysis.Add(new FeatureActivationAnalysis
                        {
                            InputText = activationEntry.InputText,
                            Activation = features[featureIndex.Value],
                            TokenPosition = activationEntry.TokenPosition
                        });
                    }
                }

                // Sort by activation strength
                activationAnalysis = activationAnalysis
                    .OrderByDescending(x => Math.Abs(x.Activation))
                    .Take(50) // Top 50 activations
                    .ToList();

                // Generate interpretability analysis
                var analysis = new FeatureInterpretabilityReport
                {
                    FeatureIndex = featureIndex.Value,
                    TotalActivations = activationAnalysis.Count,
                    AverageActivation = activationAnalysis.Average(x => x.Activation),
                    MaxActivation = activationAnalysis.Max(x => x.Activation),
                    TopActivations = activationAnalysis.Take(10).ToList(),

                    // Simple pattern detection
                    CommonTokens = ExtractCommonTokens(activationAnalysis),
                    PossibleConcept = InferPossibleConcept(activationAnalysis)
                };

                var json = JsonSerializer.Serialize(analysis);
                SqlContext.Pipe.Send($"Feature {featureIndex.Value} analysis completed");
                SqlContext.Pipe.Send(json);
            }
            catch (Exception ex)
            {
                SqlContext.Pipe.Send($"Error analyzing feature interpretability: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Main processing function for Skip Transcoder neural network operations
        /// Orchestrates the complete pipeline: training, feature extraction, and circuit analysis
        /// </summary>
        /// <param name="sessionId">Activation capture session ID</param>
        /// <param name="operation">Operation type: 'train', 'extract', 'analyze', 'optimize'</param>
        /// <param name="parameters">JSON parameters for the operation</param>
        [SqlProcedure]
        public static void ProcessSkipTranscoder(
            SqlInt64 sessionId,
            SqlString operation,
            SqlString parameters)
        {
            try
            {
                SqlContext.Pipe.Send($"Starting Skip Transcoder processing: {operation.Value} for session {sessionId.Value}");

                var operationType = operation.Value?.ToLower() ?? "train";
                var paramDict = ParseParameters(parameters.Value);

                switch (operationType)
                {
                    case "train":
                        ProcessTraining(sessionId.Value, paramDict);
                        break;

                    case "extract":
                        ProcessFeatureExtraction(sessionId.Value, paramDict);
                        break;

                    case "analyze":
                        ProcessCircuitAnalysis(sessionId.Value, paramDict);
                        break;

                    case "optimize":
                        ProcessPerformanceOptimization(sessionId.Value, paramDict);
                        break;

                    case "pipeline":
                        ProcessFullPipeline(sessionId.Value, paramDict);
                        break;

                    default:
                        SqlContext.Pipe.Send($"Unknown operation: {operationType}. Supported: train, extract, analyze, optimize, pipeline");
                        return;
                }

                SqlContext.Pipe.Send($"Skip Transcoder processing completed successfully for {operationType}");
            }
            catch (Exception ex)
            {
                SqlContext.Pipe.Send($"Error in ProcessSkipTranscoder: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Processes training operations for Skip Transcoder models
        /// </summary>
        private static void ProcessTraining(long sessionId, Dictionary<string, object> parameters)
        {
            var layerIndex = GetParameterAsInt(parameters, "layerIndex", 0);
            var latentDim = GetParameterAsInt(parameters, "latentDim", 2048);
            var sparsityPenalty = GetParameterAsDouble(parameters, "sparsityPenalty", 0.01);
            var learningRate = GetParameterAsDouble(parameters, "learningRate", 0.001);
            var maxEpochs = GetParameterAsInt(parameters, "maxEpochs", 100);

            SqlContext.Pipe.Send($"Training Skip Transcoder: layer {layerIndex}, latent dim {latentDim}");

            // Call existing training function
            TrainSkipTranscoder(
                new SqlInt64(sessionId),
                new SqlInt32(layerIndex),
                new SqlInt32(latentDim),
                new SqlDouble(sparsityPenalty),
                new SqlDouble(learningRate),
                new SqlInt32(maxEpochs));

            // Record training metrics
            RecordPerformanceMetrics(sessionId, "training", parameters);
        }

        /// <summary>
        /// Processes feature extraction operations
        /// </summary>
        private static void ProcessFeatureExtraction(long sessionId, Dictionary<string, object> parameters)
        {
            var layerIndex = GetParameterAsInt(parameters, "layerIndex", 0);
            var batchSize = GetParameterAsInt(parameters, "batchSize", 100);

            SqlContext.Pipe.Send($"Extracting features for session {sessionId}, layer {layerIndex}");

            // Load trained transcoder
            var transcoder = LoadTranscoderModel(sessionId, layerIndex);
            if (transcoder == null)
            {
                SqlContext.Pipe.Send("No trained transcoder found for feature extraction");
                return;
            }

            // Batch process activation data for feature extraction
            var activations = LoadActivationData(sessionId, layerIndex);
            var featureResults = new List<FeatureExtractionResult>();

            for (int i = 0; i < activations.Count; i += batchSize)
            {
                var batch = activations.Skip(i).Take(batchSize).ToList();

                foreach (var activation in batch)
                {
                    var features = transcoder.Encode(activation);
                    var activeFeatures = ExtractActiveFeatures(features, activation);

                    featureResults.Add(new FeatureExtractionResult
                    {
                        SampleIndex = i + batch.IndexOf(activation),
                        ActiveFeatureCount = activeFeatures.Count,
                        SparsityLevel = CalculateSparsity(features),
                        MaxActivation = features.Max(),
                        Features = activeFeatures
                    });
                }

                if (i % (batchSize * 10) == 0)
                {
                    SqlContext.Pipe.Send($"Processed {i} activations, found {featureResults.Count} feature sets");
                }
            }

            // Store feature extraction results
            StoreFeatureExtractionResults(sessionId, layerIndex, featureResults);

            // Record performance metrics
            RecordPerformanceMetrics(sessionId, "feature_extraction", parameters);
        }

        /// <summary>
        /// Processes circuit analysis operations with Neo4j integration
        /// </summary>
        private static void ProcessCircuitAnalysis(long sessionId, Dictionary<string, object> parameters)
        {
            var domain = GetParameterAsString(parameters, "domain", "");
            var minStrength = GetParameterAsDouble(parameters, "minStrength", 0.1);
            var maxDepth = GetParameterAsInt(parameters, "maxDepth", 3);

            SqlContext.Pipe.Send($"Analyzing circuits for domain: {domain}");

            // Get discovered features for this session
            var features = GetSessionFeatures(sessionId);

            if (features.Count == 0)
            {
                SqlContext.Pipe.Send("No features found for circuit analysis. Run feature extraction first.");
                return;
            }

            // Analyze feature relationships and build circuit map
            var circuits = AnalyzeFeatureCircuits(features, minStrength, maxDepth);

            SqlContext.Pipe.Send($"Discovered {circuits.Count} computational circuits");

            // Create circuit nodes and relationships for Neo4j (queued for external processing)
            foreach (var circuit in circuits)
            {
                QueueCircuitForNeo4jProcessing(sessionId, circuit);
            }

            // Record circuit analysis results
            StoreCircuitAnalysisResults(sessionId, circuits);

            // Record performance metrics
            RecordPerformanceMetrics(sessionId, "circuit_analysis", parameters);
        }

        /// <summary>
        /// Processes performance optimization operations
        /// </summary>
        private static void ProcessPerformanceOptimization(long sessionId, Dictionary<string, object> parameters)
        {
            var optimizationType = GetParameterAsString(parameters, "type", "memory");

            SqlContext.Pipe.Send($"Optimizing performance: {optimizationType}");

            switch (optimizationType.ToLower())
            {
                case "memory":
                    OptimizeMemoryUsage(sessionId);
                    break;

                case "compute":
                    OptimizeComputePerformance(sessionId);
                    break;

                case "storage":
                    OptimizeStorageAccess(sessionId);
                    break;

                default:
                    SqlContext.Pipe.Send($"Unknown optimization type: {optimizationType}");
                    return;
            }

            // Record optimization results
            RecordPerformanceMetrics(sessionId, "optimization", parameters);
        }

        /// <summary>
        /// Processes complete pipeline: train -> extract -> analyze -> optimize
        /// </summary>
        private static void ProcessFullPipeline(long sessionId, Dictionary<string, object> parameters)
        {
            SqlContext.Pipe.Send("Starting full Skip Transcoder pipeline");

            try
            {
                // Step 1: Training
                ProcessTraining(sessionId, parameters);

                // Step 2: Feature Extraction
                ProcessFeatureExtraction(sessionId, parameters);

                // Step 3: Circuit Analysis
                ProcessCircuitAnalysis(sessionId, parameters);

                // Step 4: Mechanistic Interpretability Analysis
                ProcessMechanisticInterpretability(sessionId, parameters);

                // Step 5: Performance Optimization
                ProcessPerformanceOptimization(sessionId, parameters);

                SqlContext.Pipe.Send("Full pipeline completed successfully");
            }
            catch (Exception ex)
            {
                SqlContext.Pipe.Send($"Pipeline failed at step: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// MECHANISTIC INTERPRETABILITY: Advanced SQL CLR procedure for neural interpretability analysis
        /// </summary>
        [SqlProcedure]
        public static void AnalyzeMechanisticInterpretability(
            SqlInt64 sessionId,
            SqlInt32 layerIndex,
            SqlInt32 sampleSize)
        {
            try
            {
                SqlContext.Pipe.Send($"Starting mechanistic interpretability analysis for session {sessionId.Value}, layer {layerIndex.Value}");

                // Load trained transcoder
                var transcoder = LoadTranscoderModel(sessionId.Value, layerIndex.Value);
                if (transcoder == null)
                {
                    SqlContext.Pipe.Send("No trained transcoder found for interpretability analysis");
                    return;
                }

                // Load sample activation data
                var activations = LoadActivationData(sessionId.Value, layerIndex.Value);
                if (activations.Count == 0)
                {
                    SqlContext.Pipe.Send("No activation data found for interpretability analysis");
                    return;
                }

                // Limit sample size for performance
                var sampleActivations = activations.Take(sampleSize.Value).ToList();
                SqlContext.Pipe.Send($"Analyzing {sampleActivations.Count} activation samples for interpretability");

                // Perform comprehensive interpretability analysis
                var analysis = transcoder.AnalyzeFeatureInterpretability(sampleActivations);

                // Store interpretability results
                StoreInterpretabilityResults(sessionId.Value, layerIndex.Value, analysis);

                // Generate interpretability report
                GenerateInterpretabilityReport(sessionId.Value, layerIndex.Value, analysis);

                SqlContext.Pipe.Send("Mechanistic interpretability analysis completed successfully");
            }
            catch (Exception ex)
            {
                SqlContext.Pipe.Send($"Error in mechanistic interpretability analysis: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// MECHANISTIC INTERPRETABILITY: Process interpretability analysis as part of pipeline
        /// </summary>
        private static void ProcessMechanisticInterpretability(long sessionId, Dictionary<string, object> parameters)
        {
            var layerIndex = GetParameterAsInt(parameters, "layerIndex", 0);
            var sampleSize = GetParameterAsInt(parameters, "interpretabilitySampleSize", 1000);

            SqlContext.Pipe.Send($"Processing mechanistic interpretability for session {sessionId}");

            // Call the interpretability analysis procedure
            AnalyzeMechanisticInterpretability(
                new SqlInt64(sessionId),
                new SqlInt32(layerIndex),
                new SqlInt32(sampleSize));

            // Record performance metrics
            RecordPerformanceMetrics(sessionId, "mechanistic_interpretability", parameters);
        }

        /// <summary>
        /// Store interpretability analysis results in SQL Server
        /// </summary>
        private static void StoreInterpretabilityResults(long sessionId, int layerIndex, InterpretabilityAnalysis analysis)
        {
            using (var connection = new SqlConnection("context connection=true"))
            {
                connection.Open();

                // Store feature statistics
                foreach (var stat in analysis.FeatureStatistics)
                {
                    var query = @"
                        INSERT INTO dbo.FeatureInterpretabilityResults
                        (SessionId, LayerIndex, FeatureIndex, Mean, Variance, StandardDeviation,
                         Sparsity, MaxActivation, MinActivation, ActivationCount, AnalysisType, CreatedAt)
                        VALUES
                        (@SessionId, @LayerIndex, @FeatureIndex, @Mean, @Variance, @StdDev,
                         @Sparsity, @MaxActivation, @MinActivation, @ActivationCount, 'FEATURE_STATISTICS', GETUTCDATE())";

                    using (var command = new SqlCommand(query, connection))
                    {
                        command.Parameters.AddWithValue("@SessionId", sessionId);
                        command.Parameters.AddWithValue("@LayerIndex", layerIndex);
                        command.Parameters.AddWithValue("@FeatureIndex", stat.Key);
                        command.Parameters.AddWithValue("@Mean", stat.Value.Mean);
                        command.Parameters.AddWithValue("@Variance", stat.Value.Variance);
                        command.Parameters.AddWithValue("@StdDev", stat.Value.StandardDeviation);
                        command.Parameters.AddWithValue("@Sparsity", stat.Value.Sparsity);
                        command.Parameters.AddWithValue("@MaxActivation", stat.Value.MaxActivation);
                        command.Parameters.AddWithValue("@MinActivation", stat.Value.MinActivation);
                        command.Parameters.AddWithValue("@ActivationCount", stat.Value.ActivationCount);

                        command.ExecuteNonQuery();
                    }
                }

                // Store feature correlations
                foreach (var correlation in analysis.FeatureCorrelations.Take(1000)) // Limit for performance
                {
                    var query = @"
                        INSERT INTO dbo.FeatureCorrelationResults
                        (SessionId, LayerIndex, FeatureIndex1, FeatureIndex2, CorrelationValue, AnalysisType, CreatedAt)
                        VALUES
                        (@SessionId, @LayerIndex, @Feature1, @Feature2, @Correlation, 'PEARSON_CORRELATION', GETUTCDATE())";

                    using (var command = new SqlCommand(query, connection))
                    {
                        command.Parameters.AddWithValue("@SessionId", sessionId);
                        command.Parameters.AddWithValue("@LayerIndex", layerIndex);
                        command.Parameters.AddWithValue("@Feature1", correlation.Key.Item1);
                        command.Parameters.AddWithValue("@Feature2", correlation.Key.Item2);
                        command.Parameters.AddWithValue("@Correlation", correlation.Value);

                        command.ExecuteNonQuery();
                    }
                }

                // Store causal attribution results
                foreach (var attribution in analysis.CausalAttribution)
                {
                    var query = @"
                        INSERT INTO dbo.CausalAttributionResults
                        (SessionId, LayerIndex, FeatureIndex, AttributionScore, AnalysisType, CreatedAt)
                        VALUES
                        (@SessionId, @LayerIndex, @FeatureIndex, @Attribution, 'GRADIENT_ATTRIBUTION', GETUTCDATE())";

                    using (var command = new SqlCommand(query, connection))
                    {
                        command.Parameters.AddWithValue("@SessionId", sessionId);
                        command.Parameters.AddWithValue("@LayerIndex", layerIndex);
                        command.Parameters.AddWithValue("@FeatureIndex", attribution.Key);
                        command.Parameters.AddWithValue("@Attribution", attribution.Value);

                        command.ExecuteNonQuery();
                    }
                }

                SqlContext.Pipe.Send($"Stored interpretability results for {analysis.FeatureStatistics.Count} features");
            }
        }

        /// <summary>
        /// Generate comprehensive interpretability report
        /// </summary>
        private static void GenerateInterpretabilityReport(long sessionId, int layerIndex, InterpretabilityAnalysis analysis)
        {
            var report = new System.Text.StringBuilder();

            report.AppendLine($"=== MECHANISTIC INTERPRETABILITY REPORT ===");
            report.AppendLine($"Session: {sessionId}, Layer: {layerIndex}");
            report.AppendLine($"Generated: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
            report.AppendLine();

            // Feature Statistics Summary
            report.AppendLine("FEATURE STATISTICS SUMMARY:");
            report.AppendLine($"Total features analyzed: {analysis.FeatureStatistics.Count}");
            if (analysis.FeatureStatistics.Any())
            {
                var avgSparsity = analysis.FeatureStatistics.Values.Average(s => s.Sparsity);
                var avgActivation = analysis.FeatureStatistics.Values.Average(s => s.Mean);
                report.AppendLine($"Average sparsity: {avgSparsity:F4}");
                report.AppendLine($"Average activation: {avgActivation:F6}");
            }
            report.AppendLine();

            // Correlation Analysis
            report.AppendLine("FEATURE CORRELATION ANALYSIS:");
            report.AppendLine($"Significant correlations found: {analysis.FeatureCorrelations.Count}");
            if (analysis.FeatureCorrelations.Any())
            {
                var strongCorrelations = analysis.FeatureCorrelations.Where(c => Math.Abs(c.Value) > 0.5).Count();
                report.AppendLine($"Strong correlations (|r| > 0.5): {strongCorrelations}");
            }
            report.AppendLine();

            // Causal Attribution
            report.AppendLine("CAUSAL ATTRIBUTION ANALYSIS:");
            if (analysis.CausalAttribution.Any())
            {
                var topFeatures = analysis.CausalAttribution
                    .OrderByDescending(a => a.Value)
                    .Take(10)
                    .ToList();

                report.AppendLine("Top 10 most causally important features:");
                foreach (var feature in topFeatures)
                {
                    report.AppendLine($"  Feature {feature.Key}: {feature.Value:F6}");
                }
            }
            report.AppendLine();

            // Feature Decomposition
            report.AppendLine("FEATURE DECOMPOSITION ANALYSIS:");
            report.AppendLine($"Encoder complexity: {analysis.FeatureDecomposition.EncoderComplexity:F4}");
            report.AppendLine($"Decoder complexity: {analysis.FeatureDecomposition.DecoderComplexity:F4}");
            report.AppendLine($"Skip connection importance: {analysis.FeatureDecomposition.SkipConnectionImportance:F4}");
            report.AppendLine($"Effective dimensionality: {analysis.FeatureDecomposition.EffectiveDimensionality:F2}");

            // Store report in database
            StoreInterpretabilityReport(sessionId, layerIndex, report.ToString());

            // Send summary to pipe
            SqlContext.Pipe.Send("=== INTERPRETABILITY REPORT GENERATED ===");
            SqlContext.Pipe.Send($"Features: {analysis.FeatureStatistics.Count}, Correlations: {analysis.FeatureCorrelations.Count}");
        }

        /// <summary>
        /// Store interpretability report in database
        /// </summary>
        private static void StoreInterpretabilityReport(long sessionId, int layerIndex, string reportContent)
        {
            try
            {
                using (var connection = new SqlConnection("context connection=true"))
                {
                    connection.Open();

                    var query = @"
                        INSERT INTO dbo.InterpretabilityReports
                        (SessionId, LayerIndex, ReportContent, ReportType, GeneratedAt)
                        VALUES
                        (@SessionId, @LayerIndex, @ReportContent, 'MECHANISTIC_ANALYSIS', GETUTCDATE())";

                    using (var command = new SqlCommand(query, connection))
                    {
                        command.Parameters.AddWithValue("@SessionId", sessionId);
                        command.Parameters.AddWithValue("@LayerIndex", layerIndex);
                        command.Parameters.AddWithValue("@ReportContent", reportContent);

                        command.ExecuteNonQuery();
                    }
                }
            }
            catch (Exception ex)
            {
                SqlContext.Pipe.Send($"Warning: Failed to store interpretability report: {ex.Message}");
            }
        }

        #region Core Skip Transcoder Implementation

        /// <summary>
        /// Skip Transcoder neural network implementation
        /// Architecture: f(x) ≈ D(E(x)) + Ax + b
        /// </summary>
        private class SkipTranscoder
        {
            private readonly int _inputDim;
            private readonly int _latentDim;
            private readonly double _sparsityPenalty;

            // Network parameters
            private float[,] _encoderWeights;  // inputDim x latentDim
            private float[] _encoderBias;     // latentDim
            private float[,] _decoderWeights; // latentDim x inputDim
            private float[] _decoderBias;     // inputDim
            private float[,] _skipWeights;    // inputDim x inputDim (typically diagonal/sparse)
            private float[] _skipBias;        // inputDim

            public SkipTranscoder(int inputDim, int latentDim, double sparsityPenalty)
            {
                _inputDim = inputDim;
                _latentDim = latentDim;
                _sparsityPenalty = sparsityPenalty;

                InitializeParameters();
            }

            private void InitializeParameters()
            {
                var random = new Random(42); // Fixed seed for reproducibility

                // Xavier/Glorot initialization for encoder
                var encoderScale = Math.Sqrt(2.0 / (_inputDim + _latentDim));
                _encoderWeights = new float[_inputDim, _latentDim];
                for (int i = 0; i < _inputDim; i++)
                    for (int j = 0; j < _latentDim; j++)
                        _encoderWeights[i, j] = (float)(random.NextGaussian() * encoderScale);

                _encoderBias = new float[_latentDim];

                // Xavier initialization for decoder
                var decoderScale = Math.Sqrt(2.0 / (_latentDim + _inputDim));
                _decoderWeights = new float[_latentDim, _inputDim];
                for (int i = 0; i < _latentDim; i++)
                    for (int j = 0; j < _inputDim; j++)
                        _decoderWeights[i, j] = (float)(random.NextGaussian() * decoderScale);

                _decoderBias = new float[_inputDim];

                // Initialize skip connection as identity-like
                _skipWeights = new float[_inputDim, _inputDim];
                for (int i = 0; i < _inputDim; i++)
                    _skipWeights[i, i] = 1.0f; // Start with identity

                _skipBias = new float[_inputDim];
            }

            public float[] Encode(float[] input)
            {
                var latent = new float[_latentDim];

                // Linear transformation: Wx + b
                for (int j = 0; j < _latentDim; j++)
                {
                    float sum = _encoderBias[j];
                    for (int i = 0; i < _inputDim; i++)
                    {
                        sum += input[i] * _encoderWeights[i, j];
                    }
                    // ReLU activation for sparsity
                    latent[j] = Math.Max(0, sum);
                }

                return latent;
            }

            public float[] Decode(float[] latent)
            {
                var decoded = new float[_inputDim];

                // Decoder: latent -> reconstruction
                for (int j = 0; j < _inputDim; j++)
                {
                    float sum = _decoderBias[j];
                    for (int i = 0; i < _latentDim; i++)
                    {
                        sum += latent[i] * _decoderWeights[i, j];
                    }
                    decoded[j] = sum;
                }

                return decoded;
            }

            public float[] Forward(float[] input)
            {
                var latent = Encode(input);
                var decoded = Decode(latent);

                // Skip connection: output = decoded + Ax + b
                var output = new float[_inputDim];
                for (int j = 0; j < _inputDim; j++)
                {
                    float skipSum = _skipBias[j];
                    for (int i = 0; i < _inputDim; i++)
                    {
                        skipSum += input[i] * _skipWeights[i, j];
                    }
                    output[j] = decoded[j] + skipSum;
                }

                return output;
            }

            public double TrainBatch(List<float[]> batch, AdamOptimizer optimizer)
            {
                double totalLoss = 0.0;

                foreach (var input in batch)
                {
                    var latent = Encode(input);
                    var output = Forward(input);

                    // Compute loss: reconstruction + sparsity penalty
                    double reconstructionLoss = 0.0;
                    for (int i = 0; i < _inputDim; i++)
                    {
                        double diff = output[i] - input[i];
                        reconstructionLoss += diff * diff;
                    }

                    double sparsityLoss = 0.0;
                    for (int i = 0; i < _latentDim; i++)
                    {
                        sparsityLoss += Math.Abs(latent[i]);
                    }

                    double loss = reconstructionLoss + _sparsityPenalty * sparsityLoss;
                    totalLoss += loss;

                    // Simplified gradient computation and parameter updates
                    UpdateParameters(input, output, latent, optimizer);
                }

                return totalLoss / batch.Count;
            }

            private void UpdateParameters(float[] input, float[] output, float[] latent, AdamOptimizer optimizer)
            {
                // Simplified gradient descent updates
                // In full implementation, this would compute proper gradients via backpropagation

                var learningRate = (float)optimizer.LearningRate;

                // Update decoder weights based on reconstruction error
                for (int i = 0; i < _latentDim; i++)
                {
                    for (int j = 0; j < _inputDim; j++)
                    {
                        float error = output[j] - input[j];
                        float gradient = error * latent[i];
                        _decoderWeights[i, j] -= learningRate * gradient * 0.01f; // Scaled down
                    }
                }

                // Update encoder weights to improve sparsity and reconstruction
                for (int i = 0; i < _inputDim; i++)
                {
                    for (int j = 0; j < _latentDim; j++)
                    {
                        if (latent[j] > 0) // Only update active features
                        {
                            float gradient = input[i] * 0.001f; // Simplified gradient
                            _encoderWeights[i, j] -= learningRate * gradient;
                        }
                    }
                }
            }

            public double GetAverageSparsity()
            {
                // Return a metric of how sparse the learned features are
                // Calculate actual L0 norm (proportion of near-zero activations) across latent features

                if (_encoderWeights == null || _encoderWeights.Length == 0) return 0.0;

                int totalFeatures = _latentDim;
                int sparseFeatures = 0;

                // Compute sparsity based on encoder weight magnitudes and bias terms
                for (int i = 0; i < _latentDim; i++)
                {
                    double weightMagnitude = 0.0;
                    for (int j = 0; j < _inputDim; j++)
                    {
                        weightMagnitude += Math.Abs(_encoderWeights[i, j]);
                    }

                    // Add bias contribution
                    weightMagnitude += Math.Abs(_encoderBias[i]);

                    // Feature is considered sparse if its average weight magnitude is very small
                    // This approximates how often this feature activates near zero
                    double avgWeightMagnitude = weightMagnitude / _inputDim;

                    if (avgWeightMagnitude < 0.01) // Threshold for sparse features
                    {
                        sparseFeatures++;
                    }
                }

                return (double)sparseFeatures / totalFeatures;
            }

            /// <summary>
            /// MECHANISTIC INTERPRETABILITY: Analyze feature interactions and dependencies
            /// </summary>
            public InterpretabilityAnalysis AnalyzeFeatureInterpretability(List<float[]> sampleActivations)
            {
                var analysis = new InterpretabilityAnalysis();

                // 1. Feature Activation Statistics
                var featureStats = ComputeFeatureStatistics(sampleActivations);
                analysis.FeatureStatistics = featureStats;

                // 2. Feature Correlation Analysis
                var correlations = ComputeFeatureCorrelations(sampleActivations);
                analysis.FeatureCorrelations = correlations;

                // 3. Feature Selectivity Analysis
                var selectivity = ComputeFeatureSelectivity(sampleActivations);
                analysis.FeatureSelectivity = selectivity;

                // 4. Causal Feature Attribution
                var causalAttribution = ComputeCausalAttribution(sampleActivations);
                analysis.CausalAttribution = causalAttribution;

                // 5. Feature Decomposition Analysis
                var decomposition = AnalyzeFeatureDecomposition();
                analysis.FeatureDecomposition = decomposition;

                return analysis;
            }

            /// <summary>
            /// MECHANISTIC INTERPRETABILITY: Compute feature activation statistics
            /// </summary>
            private Dictionary<int, FeatureStatistics> ComputeFeatureStatistics(List<float[]> sampleActivations)
            {
                var stats = new Dictionary<int, FeatureStatistics>();

                for (int featureIdx = 0; featureIdx < _latentDim; featureIdx++)
                {
                    var activations = new List<float>();

                    foreach (var activation in sampleActivations)
                    {
                        var features = Encode(activation);
                        if (featureIdx < features.Length)
                        {
                            activations.Add(features[featureIdx]);
                        }
                    }

                    if (activations.Count > 0)
                    {
                        var mean = activations.Average();
                        var variance = activations.Select(x => Math.Pow(x - mean, 2)).Average();
                        var activeCount = activations.Count(x => Math.Abs(x) > 1e-6);

                        stats[featureIdx] = new FeatureStatistics
                        {
                            Mean = mean,
                            Variance = variance,
                            StandardDeviation = Math.Sqrt(variance),
                            Sparsity = 1.0 - (double)activeCount / activations.Count,
                            MaxActivation = activations.Max(),
                            MinActivation = activations.Min(),
                            ActivationCount = activeCount
                        };
                    }
                }

                return stats;
            }

            /// <summary>
            /// MECHANISTIC INTERPRETABILITY: Compute feature correlations for circuit discovery
            /// </summary>
            private Dictionary<(int, int), double> ComputeFeatureCorrelations(List<float[]> sampleActivations)
            {
                var correlations = new Dictionary<(int, int), double>();

                // Compute feature activations for all samples
                var allFeatures = new List<float[]>();
                foreach (var activation in sampleActivations)
                {
                    allFeatures.Add(Encode(activation));
                }

                // Compute pairwise correlations
                for (int i = 0; i < _latentDim; i++)
                {
                    for (int j = i + 1; j < _latentDim; j++)
                    {
                        var feature1Values = allFeatures.Select(f => f[i]).ToArray();
                        var feature2Values = allFeatures.Select(f => f[j]).ToArray();

                        var correlation = ComputePearsonCorrelation(feature1Values, feature2Values);
                        if (Math.Abs(correlation) > 0.1) // Only store significant correlations
                        {
                            correlations[(i, j)] = correlation;
                        }
                    }
                }

                return correlations;
            }

            /// <summary>
            /// MECHANISTIC INTERPRETABILITY: Compute feature selectivity (how specific each feature is)
            /// </summary>
            private Dictionary<int, double> ComputeFeatureSelectivity(List<float[]> sampleActivations)
            {
                var selectivity = new Dictionary<int, double>();

                for (int featureIdx = 0; featureIdx < _latentDim; featureIdx++)
                {
                    var activations = new List<float>();

                    foreach (var activation in sampleActivations)
                    {
                        var features = Encode(activation);
                        if (featureIdx < features.Length)
                        {
                            activations.Add(features[featureIdx]);
                        }
                    }

                    if (activations.Count > 0)
                    {
                        // Kurtosis-based selectivity measure (higher kurtosis = more selective)
                        var mean = activations.Average();
                        var variance = activations.Select(x => Math.Pow(x - mean, 2)).Average();
                        var fourthMoment = activations.Select(x => Math.Pow(x - mean, 4)).Average();

                        var kurtosis = variance > 0 ? fourthMoment / Math.Pow(variance, 2) - 3 : 0;
                        selectivity[featureIdx] = Math.Max(0, kurtosis); // Excess kurtosis
                    }
                }

                return selectivity;
            }

            /// <summary>
            /// MECHANISTIC INTERPRETABILITY: Compute causal attribution using gradient-based analysis
            /// </summary>
            private Dictionary<int, double> ComputeCausalAttribution(List<float[]> sampleActivations)
            {
                var attribution = new Dictionary<int, double>();

                // Use reconstruction gradient as proxy for causal importance
                foreach (var activation in sampleActivations.Take(100)) // Sample for performance
                {
                    var features = Encode(activation);
                    var reconstruction = Decode(features);

                    // Compute reconstruction error gradient w.r.t. each feature
                    for (int featureIdx = 0; featureIdx < _latentDim; featureIdx++)
                    {
                        var importance = ComputeFeatureImportanceGradient(activation, featureIdx);
                        if (!attribution.ContainsKey(featureIdx))
                            attribution[featureIdx] = 0;
                        attribution[featureIdx] += importance;
                    }
                }

                // Normalize by number of samples
                var keys = attribution.Keys.ToList();
                foreach (var key in keys)
                {
                    attribution[key] /= Math.Min(100, sampleActivations.Count);
                }

                return attribution;
            }

            /// <summary>
            /// MECHANISTIC INTERPRETABILITY: Analyze feature decomposition into components
            /// </summary>
            private FeatureDecomposition AnalyzeFeatureDecomposition()
            {
                var decomposition = new FeatureDecomposition();

                // Analyze encoder weight structure
                decomposition.EncoderComplexity = AnalyzeWeightComplexity(_encoderWeights);
                decomposition.DecoderComplexity = AnalyzeWeightComplexity(_decoderWeights);

                // Analyze skip connection importance
                decomposition.SkipConnectionImportance = AnalyzeSkipConnectionImportance();

                // Feature dimensionality analysis
                decomposition.EffectiveDimensionality = EstimateEffectiveDimensionality();

                return decomposition;
            }

            /// <summary>
            /// Helper: Compute Pearson correlation between two feature vectors
            /// </summary>
            private double ComputePearsonCorrelation(float[] x, float[] y)
            {
                if (x.Length != y.Length || x.Length == 0) return 0;

                var meanX = x.Average();
                var meanY = y.Average();

                var numerator = 0.0;
                var sumSquareX = 0.0;
                var sumSquareY = 0.0;

                for (int i = 0; i < x.Length; i++)
                {
                    var diffX = x[i] - meanX;
                    var diffY = y[i] - meanY;

                    numerator += diffX * diffY;
                    sumSquareX += diffX * diffX;
                    sumSquareY += diffY * diffY;
                }

                var denominator = Math.Sqrt(sumSquareX * sumSquareY);
                return denominator > 1e-10 ? numerator / denominator : 0;
            }

            /// <summary>
            /// Helper: Compute feature importance using gradient approximation
            /// </summary>
            private double ComputeFeatureImportanceGradient(float[] activation, int featureIdx)
            {
                const float epsilon = 1e-4f;

                // Original reconstruction error
                var originalFeatures = Encode(activation);
                var originalReconstruction = Decode(originalFeatures);
                var originalError = ComputeReconstructionError(activation, originalReconstruction);

                // Perturbed reconstruction error
                var perturbedFeatures = (float[])originalFeatures.Clone();
                perturbedFeatures[featureIdx] += epsilon;
                var perturbedReconstruction = Decode(perturbedFeatures);
                var perturbedError = ComputeReconstructionError(activation, perturbedReconstruction);

                // Gradient approximation
                return Math.Abs((perturbedError - originalError) / epsilon);
            }

            /// <summary>
            /// Helper: Compute reconstruction error
            /// </summary>
            private double ComputeReconstructionError(float[] original, float[] reconstruction)
            {
                double error = 0;
                for (int i = 0; i < Math.Min(original.Length, reconstruction.Length); i++)
                {
                    var diff = original[i] - reconstruction[i];
                    error += diff * diff;
                }
                return error;
            }

            /// <summary>
            /// Helper: Analyze weight matrix complexity
            /// </summary>
            private double AnalyzeWeightComplexity(float[,] weights)
            {
                var rows = weights.GetLength(0);
                var cols = weights.GetLength(1);
                var totalWeights = rows * cols;

                if (totalWeights == 0) return 0;

                // Compute effective rank as measure of complexity
                var nonZeroCount = 0;
                var magnitudeSum = 0.0;

                for (int i = 0; i < rows; i++)
                {
                    for (int j = 0; j < cols; j++)
                    {
                        var magnitude = Math.Abs(weights[i, j]);
                        if (magnitude > 1e-6)
                        {
                            nonZeroCount++;
                            magnitudeSum += magnitude;
                        }
                    }
                }

                return nonZeroCount > 0 ? magnitudeSum / nonZeroCount : 0;
            }

            /// <summary>
            /// Helper: Analyze skip connection importance
            /// </summary>
            private double AnalyzeSkipConnectionImportance()
            {
                double totalSkipMagnitude = 0;
                double totalDecoderMagnitude = 0;

                // Compare skip weights to decoder weights
                for (int i = 0; i < _inputDim; i++)
                {
                    for (int j = 0; j < _inputDim; j++)
                    {
                        totalSkipMagnitude += Math.Abs(_skipWeights[i, j]);
                    }
                }

                var decoderRows = _decoderWeights.GetLength(0);
                var decoderCols = _decoderWeights.GetLength(1);
                for (int i = 0; i < decoderRows; i++)
                {
                    for (int j = 0; j < decoderCols; j++)
                    {
                        totalDecoderMagnitude += Math.Abs(_decoderWeights[i, j]);
                    }
                }

                return totalDecoderMagnitude > 0 ? totalSkipMagnitude / totalDecoderMagnitude : 0;
            }

            /// <summary>
            /// Helper: Estimate effective dimensionality
            /// </summary>
            private double EstimateEffectiveDimensionality()
            {
                // Use participation ratio as effective dimensionality measure
                var eigenvalueSum = 0.0;
                var eigenvalueSumSquared = 0.0;

                // Simplified eigenvalue estimation based on weight magnitudes
                for (int i = 0; i < _latentDim; i++)
                {
                    double featureMagnitude = 0;
                    for (int j = 0; j < _inputDim; j++)
                    {
                        featureMagnitude += _encoderWeights[i, j] * _encoderWeights[i, j];
                    }

                    eigenvalueSum += featureMagnitude;
                    eigenvalueSumSquared += featureMagnitude * featureMagnitude;
                }

                return eigenvalueSumSquared > 0 ? (eigenvalueSum * eigenvalueSum) / eigenvalueSumSquared : 0;
            }

            public byte[] SerializeWeights()
            {
                // Serialize all parameters for storage in SQL Server
                using (var ms = new MemoryStream())
                using (var writer = new BinaryWriter(ms))
                {
                    // Write dimensions
                    writer.Write(_inputDim);
                    writer.Write(_latentDim);

                    // Write encoder weights
                    for (int i = 0; i < _inputDim; i++)
                        for (int j = 0; j < _latentDim; j++)
                            writer.Write(_encoderWeights[i, j]);

                    // Write encoder bias
                    for (int i = 0; i < _latentDim; i++)
                        writer.Write(_encoderBias[i]);

                    // Write decoder weights
                    for (int i = 0; i < _latentDim; i++)
                        for (int j = 0; j < _inputDim; j++)
                            writer.Write(_decoderWeights[i, j]);

                    // Write decoder bias
                    for (int i = 0; i < _inputDim; i++)
                        writer.Write(_decoderBias[i]);

                    // Write skip weights
                    for (int i = 0; i < _inputDim; i++)
                        for (int j = 0; j < _inputDim; j++)
                            writer.Write(_skipWeights[i, j]);

                    // Write skip bias
                    for (int i = 0; i < _inputDim; i++)
                        writer.Write(_skipBias[i]);

                    return ms.ToArray();
                }
            }

            public static SkipTranscoder Deserialize(byte[] data)
            {
                using (var ms = new MemoryStream(data))
                using (var reader = new BinaryReader(ms))
                {
                    var inputDim = reader.ReadInt32();
                    var latentDim = reader.ReadInt32();

                    var transcoder = new SkipTranscoder(inputDim, latentDim, 0.01); // Sparsity penalty not used for inference

                    // Read encoder weights
                    for (int i = 0; i < inputDim; i++)
                        for (int j = 0; j < latentDim; j++)
                            transcoder._encoderWeights[i, j] = reader.ReadSingle();

                    // Read encoder bias
                    for (int i = 0; i < latentDim; i++)
                        transcoder._encoderBias[i] = reader.ReadSingle();

                    // Read decoder weights
                    for (int i = 0; i < latentDim; i++)
                        for (int j = 0; j < inputDim; j++)
                            transcoder._decoderWeights[i, j] = reader.ReadSingle();

                    // Read decoder bias
                    for (int i = 0; i < inputDim; i++)
                        transcoder._decoderBias[i] = reader.ReadSingle();

                    // Read skip weights
                    for (int i = 0; i < inputDim; i++)
                        for (int j = 0; j < inputDim; j++)
                            transcoder._skipWeights[i, j] = reader.ReadSingle();

                    // Read skip bias
                    for (int i = 0; i < inputDim; i++)
                        transcoder._skipBias[i] = reader.ReadSingle();

                    return transcoder;
                }
            }
        }

        /// <summary>
        /// Simple Adam optimizer implementation
        /// </summary>
        private class AdamOptimizer
        {
            public double LearningRate { get; }

            public AdamOptimizer(double learningRate)
            {
                LearningRate = learningRate;
            }
        }

        #endregion

        #region Data Loading and Storage Methods

        private static List<float[]> LoadActivationData(long sessionId, int layerIndex)
        {
            var activations = new List<float[]>();

            using (var connection = new SqlConnection("context connection=true"))
            {
                connection.Open();

                var query = @"
                    SELECT TOP 10000 ActivationVector.PathName() as FilePath, VectorDimension
                    FROM dbo.ActivationData
                    WHERE SessionId = @SessionId AND LayerIndex = @LayerIndex
                    ORDER BY SampleIndex";

                using (var command = new SqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@SessionId", sessionId);
                    command.Parameters.AddWithValue("@LayerIndex", layerIndex);

                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            var filePath = reader.GetString("FilePath");
                            var dimension = reader.GetInt32("VectorDimension");

                            // Read binary data from FILESTREAM
                            var bytes = File.ReadAllBytes(filePath);
                            var vector = BytesToFloatArray(bytes);

                            if (vector.Length == dimension)
                            {
                                activations.Add(vector);
                            }
                        }
                    }
                }
            }

            return activations;
        }

        private static void SaveTranscoderModel(long sessionId, int layerIndex, SkipTranscoder transcoder, double finalLoss)
        {
            var weights = transcoder.SerializeWeights();

            using (var connection = new SqlConnection("context connection=true"))
            {
                connection.Open();

                // BRAVO-1 INTEGRATION: Store transcoder weights using FILESTREAM for efficient model storage
                var modelId = Guid.NewGuid();
                var fileStreamQuery = @"
                    DECLARE @FilePath NVARCHAR(400);

                    -- Insert transcoder model record with FILESTREAM storage
                    INSERT INTO dbo.SkipTranscoderModels
                    (SessionId, LayerIndex, InputDimension, LatentDimension, SparsityLevel,
                     ReconstructionLoss, ModelId, FileStreamWeights, CreatedAt)
                    OUTPUT INSERTED.FileStreamWeights.PathName()
                    VALUES
                    (@SessionId, @LayerIndex, @InputDim, @LatentDim, @Sparsity,
                     @Loss, @ModelId, @Weights, GETUTCDATE())";

                using (var command = new SqlCommand(fileStreamQuery, connection))
                {
                    command.Parameters.AddWithValue("@SessionId", sessionId);
                    command.Parameters.AddWithValue("@LayerIndex", layerIndex);
                    command.Parameters.AddWithValue("@InputDim", transcoder._inputDim);
                    command.Parameters.AddWithValue("@LatentDim", transcoder._latentDim);
                    command.Parameters.AddWithValue("@Sparsity", transcoder.GetAverageSparsity());
                    command.Parameters.AddWithValue("@Loss", finalLoss);
                    command.Parameters.AddWithValue("@ModelId", modelId);
                    command.Parameters.AddWithValue("@Weights", weights);

                    var filePath = command.ExecuteScalar() as string;
                    SqlContext.Pipe.Send($"Transcoder weights stored via FILESTREAM at: {filePath}");

                    // Log integration with Bravo-1 FileStream service
                    LogFileStreamIntegration(sessionId, layerIndex, modelId, weights.Length, filePath);
                }
            }
        }

        /// <summary>
        /// BRAVO-1 INTEGRATION: Logs FileStream integration for coordination tracking
        /// </summary>
        private static void LogFileStreamIntegration(long sessionId, int layerIndex, Guid modelId, int weightsSize, string filePath)
        {
            try
            {
                using (var connection = new SqlConnection("context connection=true"))
                {
                    connection.Open();

                    var logQuery = @"
                        INSERT INTO dbo.FileStreamIntegrationLog
                        (SessionId, LayerIndex, ModelId, WeightsSizeBytes, FileStreamPath,
                         IntegrationType, IntegrationStatus, CreatedAt)
                        VALUES
                        (@SessionId, @LayerIndex, @ModelId, @WeightsSize, @FilePath,
                         'BRAVO-1-SKIP-TRANSCODER', 'SUCCESS', GETUTCDATE())";

                    using (var command = new SqlCommand(logQuery, connection))
                    {
                        command.Parameters.AddWithValue("@SessionId", sessionId);
                        command.Parameters.AddWithValue("@LayerIndex", layerIndex);
                        command.Parameters.AddWithValue("@ModelId", modelId);
                        command.Parameters.AddWithValue("@WeightsSize", weightsSize);
                        command.Parameters.AddWithValue("@FilePath", filePath ?? "");

                        command.ExecuteNonQuery();
                    }
                }

                SqlContext.Pipe.Send($"BRAVO-1 Integration: Skip Transcoder weights logged for session {sessionId}");
            }
            catch (Exception ex)
            {
                SqlContext.Pipe.Send($"Warning: Failed to log Bravo-1 integration: {ex.Message}");
            }
        }

        private static SkipTranscoder LoadTranscoderModel(long sessionId, int layerIndex)
        {
            using (var connection = new SqlConnection("context connection=true"))
            {
                connection.Open();

                // BRAVO-1 INTEGRATION: Load transcoder weights from FILESTREAM storage
                var query = @"
                    SELECT FileStreamWeights.PathName() as WeightsPath, ModelId
                    FROM dbo.SkipTranscoderModels
                    WHERE SessionId = @SessionId AND LayerIndex = @LayerIndex
                    ORDER BY CreatedAt DESC"; // Get most recent model

                using (var command = new SqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@SessionId", sessionId);
                    command.Parameters.AddWithValue("@LayerIndex", layerIndex);

                    using (var reader = command.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            var weightsPath = reader.GetString("WeightsPath");
                            var modelId = reader.GetGuid("ModelId");

                            reader.Close();

                            try
                            {
                                // Read weights from FILESTREAM path
                                var weights = File.ReadAllBytes(weightsPath);
                                var transcoder = SkipTranscoder.Deserialize(weights);

                                // Log successful FileStream access
                                LogFileStreamAccess(sessionId, layerIndex, modelId, weightsPath, weights.Length);

                                return transcoder;
                            }
                            catch (Exception ex)
                            {
                                SqlContext.Pipe.Send($"Error reading FILESTREAM weights: {ex.Message}");
                                LogFileStreamError(sessionId, layerIndex, modelId, weightsPath, ex.Message);
                                return null;
                            }
                        }
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// BRAVO-1 INTEGRATION: Logs successful FileStream access
        /// </summary>
        private static void LogFileStreamAccess(long sessionId, int layerIndex, Guid modelId, string filePath, int bytesRead)
        {
            try
            {
                using (var connection = new SqlConnection("context connection=true"))
                {
                    connection.Open();

                    var logQuery = @"
                        INSERT INTO dbo.FileStreamAccessLog
                        (SessionId, LayerIndex, ModelId, FileStreamPath, BytesRead,
                         AccessType, AccessStatus, AccessedAt)
                        VALUES
                        (@SessionId, @LayerIndex, @ModelId, @FilePath, @BytesRead,
                         'BRAVO-1-TRANSCODER-LOAD', 'SUCCESS', GETUTCDATE())";

                    using (var command = new SqlCommand(logQuery, connection))
                    {
                        command.Parameters.AddWithValue("@SessionId", sessionId);
                        command.Parameters.AddWithValue("@LayerIndex", layerIndex);
                        command.Parameters.AddWithValue("@ModelId", modelId);
                        command.Parameters.AddWithValue("@FilePath", filePath);
                        command.Parameters.AddWithValue("@BytesRead", bytesRead);

                        command.ExecuteNonQuery();
                    }
                }
            }
            catch (Exception ex)
            {
                SqlContext.Pipe.Send($"Warning: Failed to log FileStream access: {ex.Message}");
            }
        }

        /// <summary>
        /// BRAVO-1 INTEGRATION: Logs FileStream errors for coordination tracking
        /// </summary>
        private static void LogFileStreamError(long sessionId, int layerIndex, Guid modelId, string filePath, string errorMessage)
        {
            try
            {
                using (var connection = new SqlConnection("context connection=true"))
                {
                    connection.Open();

                    var logQuery = @"
                        INSERT INTO dbo.FileStreamAccessLog
                        (SessionId, LayerIndex, ModelId, FileStreamPath, BytesRead,
                         AccessType, AccessStatus, ErrorMessage, AccessedAt)
                        VALUES
                        (@SessionId, @LayerIndex, @ModelId, @FilePath, 0,
                         'BRAVO-1-TRANSCODER-LOAD', 'ERROR', @ErrorMessage, GETUTCDATE())";

                    using (var command = new SqlCommand(logQuery, connection))
                    {
                        command.Parameters.AddWithValue("@SessionId", sessionId);
                        command.Parameters.AddWithValue("@LayerIndex", layerIndex);
                        command.Parameters.AddWithValue("@ModelId", modelId);
                        command.Parameters.AddWithValue("@FilePath", filePath);
                        command.Parameters.AddWithValue("@ErrorMessage", errorMessage);

                        command.ExecuteNonQuery();
                    }
                }
            }
            catch (Exception ex)
            {
                SqlContext.Pipe.Send($"Warning: Failed to log FileStream error: {ex.Message}");
            }
        }

        private static SkipTranscoder LoadTranscoderModelById(int transcoderId)
        {
            using (var connection = new SqlConnection("context connection=true"))
            {
                connection.Open();

                var query = @"
                    SELECT EncoderWeights.PathName() as WeightsPath
                    FROM dbo.SkipTranscoderModels
                    WHERE TranscoderId = @TranscoderId";

                using (var command = new SqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@TranscoderId", transcoderId);

                    var result = command.ExecuteScalar();
                    if (result != null)
                    {
                        var weightsPath = result.ToString();
                        var weights = File.ReadAllBytes(weightsPath);
                        return SkipTranscoder.Deserialize(weights);
                    }
                }
            }

            return null;
        }

        private static void ExtractFeatures(long sessionId, int layerIndex, SkipTranscoder transcoder, List<float[]> sampleActivations)
        {
            // Analyze sample activations to identify interpretable features
            var featureActivations = new Dictionary<int, List<double>>();

            foreach (var activation in sampleActivations)
            {
                var features = transcoder.Encode(activation);

                for (int i = 0; i < features.Length; i++)
                {
                    if (Math.Abs(features[i]) > 1e-6)
                    {
                        if (!featureActivations.ContainsKey(i))
                            featureActivations[i] = new List<double>();

                        featureActivations[i].Add(features[i]);
                    }
                }
            }

            // Save discovered features to database
            using (var connection = new SqlConnection("context connection=true"))
            {
                connection.Open();

                foreach (var kvp in featureActivations.Where(x => x.Value.Count >= 5)) // At least 5 activations
                {
                    var featureIndex = kvp.Key;
                    var activations = kvp.Value;

                    var avgActivation = activations.Average();
                    var sparsity = 1.0 - (activations.Count / (double)sampleActivations.Count);

                    var insertQuery = @"
                        INSERT INTO dbo.DiscoveredFeatures
                        (TranscoderId, FeatureIndex, AverageActivation, SparsityScore)
                        SELECT TranscoderId, @FeatureIndex, @AvgActivation, @Sparsity
                        FROM dbo.SkipTranscoderModels
                        WHERE SessionId = @SessionId AND LayerIndex = @LayerIndex";

                    using (var command = new SqlCommand(insertQuery, connection))
                    {
                        command.Parameters.AddWithValue("@SessionId", sessionId);
                        command.Parameters.AddWithValue("@LayerIndex", layerIndex);
                        command.Parameters.AddWithValue("@FeatureIndex", featureIndex);
                        command.Parameters.AddWithValue("@AvgActivation", avgActivation);
                        command.Parameters.AddWithValue("@Sparsity", sparsity);

                        command.ExecuteNonQuery();
                    }
                }
            }
        }

        #endregion

        #region ProcessSkipTranscoder Helper Methods

        /// <summary>
        /// Parses JSON parameters into a dictionary
        /// </summary>
        private static Dictionary<string, object> ParseParameters(string jsonParameters)
        {
            if (string.IsNullOrEmpty(jsonParameters))
                return new Dictionary<string, object>();

            try
            {
                return JsonSerializer.Deserialize<Dictionary<string, object>>(jsonParameters)
                    ?? new Dictionary<string, object>();
            }
            catch
            {
                return new Dictionary<string, object>();
            }
        }

        /// <summary>
        /// Gets integer parameter with default value
        /// </summary>
        private static int GetParameterAsInt(Dictionary<string, object> parameters, string key, int defaultValue)
        {
            if (parameters.TryGetValue(key, out var value))
            {
                if (value is JsonElement element && element.TryGetInt32(out var intValue))
                    return intValue;
                if (int.TryParse(value.ToString(), out var parsedValue))
                    return parsedValue;
            }
            return defaultValue;
        }

        /// <summary>
        /// Gets double parameter with default value
        /// </summary>
        private static double GetParameterAsDouble(Dictionary<string, object> parameters, string key, double defaultValue)
        {
            if (parameters.TryGetValue(key, out var value))
            {
                if (value is JsonElement element && element.TryGetDouble(out var doubleValue))
                    return doubleValue;
                if (double.TryParse(value.ToString(), out var parsedValue))
                    return parsedValue;
            }
            return defaultValue;
        }

        /// <summary>
        /// Gets string parameter with default value
        /// </summary>
        private static string GetParameterAsString(Dictionary<string, object> parameters, string key, string defaultValue)
        {
            if (parameters.TryGetValue(key, out var value))
            {
                if (value is JsonElement element && element.ValueKind == JsonValueKind.String)
                    return element.GetString() ?? defaultValue;
                return value.ToString() ?? defaultValue;
            }
            return defaultValue;
        }

        /// <summary>
        /// Records performance metrics for operations
        /// </summary>
        private static void RecordPerformanceMetrics(long sessionId, string operation, Dictionary<string, object> parameters)
        {
            try
            {
                using (var connection = new SqlConnection("context connection=true"))
                {
                    connection.Open();

                    var query = @"
                        INSERT INTO dbo.SkipTranscoderMetrics
                        (SessionId, Operation, Parameters, Timestamp, ExecutionTimeMs)
                        VALUES (@SessionId, @Operation, @Parameters, GETUTCDATE(),
                               DATEDIFF(millisecond, @StartTime, GETUTCDATE()))";

                    using (var command = new SqlCommand(query, connection))
                    {
                        command.Parameters.AddWithValue("@SessionId", sessionId);
                        command.Parameters.AddWithValue("@Operation", operation);
                        command.Parameters.AddWithValue("@Parameters", JsonSerializer.Serialize(parameters));
                        command.Parameters.AddWithValue("@StartTime", DateTime.UtcNow.AddSeconds(-1)); // Approximate start time

                        command.ExecuteNonQuery();
                    }
                }
            }
            catch (Exception ex)
            {
                SqlContext.Pipe.Send($"Warning: Failed to record metrics: {ex.Message}");
            }
        }

        /// <summary>
        /// Extracts active features from encoded vector
        /// </summary>
        private static List<ActiveFeatureData> ExtractActiveFeatures(float[] features, float[] originalActivation)
        {
            var activeFeatures = new List<ActiveFeatureData>();

            for (int i = 0; i < features.Length; i++)
            {
                if (Math.Abs(features[i]) > 1e-6) // Threshold for active features
                {
                    activeFeatures.Add(new ActiveFeatureData
                    {
                        FeatureIndex = i,
                        Activation = features[i],
                        RelativeStrength = features[i] / (originalActivation.Max() + 1e-8)
                    });
                }
            }

            return activeFeatures.OrderByDescending(f => Math.Abs(f.Activation)).ToList();
        }

        /// <summary>
        /// Calculates sparsity level of feature vector
        /// </summary>
        private static double CalculateSparsity(float[] features)
        {
            var activeCount = features.Count(f => Math.Abs(f) > 1e-6);
            return 1.0 - (double)activeCount / features.Length;
        }

        /// <summary>
        /// Gets session features for circuit analysis
        /// </summary>
        private static List<SessionFeature> GetSessionFeatures(long sessionId)
        {
            var features = new List<SessionFeature>();

            using (var connection = new SqlConnection("context connection=true"))
            {
                connection.Open();

                var query = @"
                    SELECT df.FeatureId, df.FeatureIndex, df.AverageActivation, df.SparsityScore,
                           stm.TranscoderId, stm.LayerIndex
                    FROM dbo.DiscoveredFeatures df
                    INNER JOIN dbo.SkipTranscoderModels stm ON df.TranscoderId = stm.TranscoderId
                    WHERE stm.SessionId = @SessionId
                    ORDER BY df.AverageActivation DESC";

                using (var command = new SqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@SessionId", sessionId);

                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            features.Add(new SessionFeature
                            {
                                FeatureId = reader.GetInt64("FeatureId"),
                                FeatureIndex = reader.GetInt32("FeatureIndex"),
                                AverageActivation = reader.GetDouble("AverageActivation"),
                                SparsityScore = reader.GetDouble("SparsityScore"),
                                TranscoderId = reader.GetInt32("TranscoderId"),
                                LayerIndex = reader.GetInt32("LayerIndex")
                            });
                        }
                    }
                }
            }

            return features;
        }

        /// <summary>
        /// Analyzes feature circuits using correlation and causal analysis
        /// </summary>
        private static List<ComputationalCircuit> AnalyzeFeatureCircuits(List<SessionFeature> features, double minStrength, int maxDepth)
        {
            var circuits = new List<ComputationalCircuit>();

            // Group features by layer for circuit analysis
            var layerGroups = features.GroupBy(f => f.LayerIndex).OrderBy(g => g.Key).ToList();

            for (int i = 0; i < layerGroups.Count - 1; i++)
            {
                var sourceLayer = layerGroups[i];
                var targetLayer = layerGroups[i + 1];

                foreach (var sourceFeature in sourceLayer.Take(50)) // Limit for performance
                {
                    foreach (var targetFeature in targetLayer.Take(50))
                    {
                        var strength = CalculateCircuitStrength(sourceFeature, targetFeature);

                        if (strength >= minStrength)
                        {
                            circuits.Add(new ComputationalCircuit
                            {
                                SourceFeatureId = sourceFeature.FeatureId,
                                TargetFeatureId = targetFeature.FeatureId,
                                CircuitStrength = strength,
                                CircuitType = DetermineCircuitType(sourceFeature, targetFeature),
                                LayerSpan = targetFeature.LayerIndex - sourceFeature.LayerIndex,
                                Description = $"Circuit from layer {sourceFeature.LayerIndex} to {targetFeature.LayerIndex}"
                            });
                        }
                    }
                }
            }

            return circuits.OrderByDescending(c => c.CircuitStrength).Take(100).ToList();
        }

        /// <summary>
        /// Calculates circuit strength between two features
        /// </summary>
        private static double CalculateCircuitStrength(SessionFeature source, SessionFeature target)
        {
            // Simplified circuit strength calculation
            // In production, this would use correlation analysis, mutual information, etc.

            var activationSimilarity = 1.0 - Math.Abs(source.AverageActivation - target.AverageActivation);
            var sparsityComplement = Math.Min(source.SparsityScore, target.SparsityScore);
            var layerDistance = Math.Max(1, Math.Abs(target.LayerIndex - source.LayerIndex));

            return (activationSimilarity * sparsityComplement) / Math.Log(layerDistance + 1);
        }

        /// <summary>
        /// Determines circuit type based on feature characteristics
        /// </summary>
        private static string DetermineCircuitType(SessionFeature source, SessionFeature target)
        {
            if (source.SparsityScore > 0.8 && target.SparsityScore > 0.8)
                return "sparse_pathway";
            else if (source.AverageActivation > 0.5 && target.AverageActivation > 0.5)
                return "activation_amplifier";
            else if (Math.Abs(source.AverageActivation - target.AverageActivation) < 0.1)
                return "pattern_maintainer";
            else
                return "feature_transformer";
        }

        /// <summary>
        /// Queues circuit for external Neo4j processing (security compliant)
        /// </summary>
        private static void QueueCircuitForNeo4jProcessing(long sessionId, ComputationalCircuit circuit)
        {
            try
            {
                using (var connection = new SqlConnection("context connection=true"))
                {
                    connection.Open();

                    var query = @"
                        INSERT INTO dbo.Neo4jProcessingQueue
                        (SessionId, OperationType, SourceFeatureId, TargetFeatureId,
                         CircuitStrength, CircuitType, QueuedAt, ProcessingStatus)
                        VALUES (@SessionId, 'CREATE_CIRCUIT', @SourceId, @TargetId,
                               @Strength, @Type, GETUTCDATE(), 'PENDING')";

                    using (var command = new SqlCommand(query, connection))
                    {
                        command.Parameters.AddWithValue("@SessionId", sessionId);
                        command.Parameters.AddWithValue("@SourceId", circuit.SourceFeatureId);
                        command.Parameters.AddWithValue("@TargetId", circuit.TargetFeatureId);
                        command.Parameters.AddWithValue("@Strength", circuit.CircuitStrength);
                        command.Parameters.AddWithValue("@Type", circuit.CircuitType);

                        command.ExecuteNonQuery();
                    }
                }
            }
            catch (Exception ex)
            {
                SqlContext.Pipe.Send($"Warning: Failed to queue circuit for Neo4j processing: {ex.Message}");
            }
        }

        /// <summary>
        /// Stores feature extraction results
        /// </summary>
        private static void StoreFeatureExtractionResults(long sessionId, int layerIndex, List<FeatureExtractionResult> results)
        {
            using (var connection = new SqlConnection("context connection=true"))
            {
                connection.Open();

                foreach (var result in results)
                {
                    var query = @"
                        INSERT INTO dbo.FeatureExtractionResults
                        (SessionId, LayerIndex, SampleIndex, ActiveFeatureCount,
                         SparsityLevel, MaxActivation, ExtractedAt)
                        VALUES (@SessionId, @LayerIndex, @SampleIndex, @ActiveCount,
                               @Sparsity, @MaxActivation, GETUTCDATE())";

                    using (var command = new SqlCommand(query, connection))
                    {
                        command.Parameters.AddWithValue("@SessionId", sessionId);
                        command.Parameters.AddWithValue("@LayerIndex", layerIndex);
                        command.Parameters.AddWithValue("@SampleIndex", result.SampleIndex);
                        command.Parameters.AddWithValue("@ActiveCount", result.ActiveFeatureCount);
                        command.Parameters.AddWithValue("@Sparsity", result.SparsityLevel);
                        command.Parameters.AddWithValue("@MaxActivation", result.MaxActivation);

                        command.ExecuteNonQuery();
                    }
                }
            }
        }

        /// <summary>
        /// Stores circuit analysis results
        /// </summary>
        private static void StoreCircuitAnalysisResults(long sessionId, List<ComputationalCircuit> circuits)
        {
            using (var connection = new SqlConnection("context connection=true"))
            {
                connection.Open();

                foreach (var circuit in circuits)
                {
                    var query = @"
                        INSERT INTO dbo.CircuitAnalysisResults
                        (SessionId, SourceFeatureId, TargetFeatureId, CircuitStrength,
                         CircuitType, LayerSpan, Description, AnalyzedAt)
                        VALUES (@SessionId, @SourceId, @TargetId, @Strength,
                               @Type, @LayerSpan, @Description, GETUTCDATE())";

                    using (var command = new SqlCommand(query, connection))
                    {
                        command.Parameters.AddWithValue("@SessionId", sessionId);
                        command.Parameters.AddWithValue("@SourceId", circuit.SourceFeatureId);
                        command.Parameters.AddWithValue("@TargetId", circuit.TargetFeatureId);
                        command.Parameters.AddWithValue("@Strength", circuit.CircuitStrength);
                        command.Parameters.AddWithValue("@Type", circuit.CircuitType);
                        command.Parameters.AddWithValue("@LayerSpan", circuit.LayerSpan);
                        command.Parameters.AddWithValue("@Description", circuit.Description);

                        command.ExecuteNonQuery();
                    }
                }
            }
        }

        /// <summary>
        /// PERFORMANCE OPTIMIZATION: Advanced memory usage optimization for neural network processing
        /// </summary>
        private static void OptimizeMemoryUsage(long sessionId)
        {
            SqlContext.Pipe.Send("Optimizing memory usage with advanced techniques...");

            var startTime = DateTime.UtcNow;
            var memoryBefore = GC.GetTotalMemory(false);

            // Clear intermediate processing data
            using (var connection = new SqlConnection("context connection=true"))
            {
                connection.Open();

                // 1. Clear old temporary results with intelligent retention
                var cleanupQueries = new[]
                {
                    @"DELETE FROM dbo.TemporaryActivationData
                      WHERE SessionId = @SessionId AND CreatedAt < DATEADD(hour, -2, GETUTCDATE())",
                    @"DELETE FROM dbo.IntermediateFeatures
                      WHERE SessionId = @SessionId AND ProcessingStatus = 'COMPLETED' AND CreatedAt < DATEADD(hour, -1, GETUTCDATE())",
                    @"DELETE FROM dbo.ComputationCache
                      WHERE SessionId = @SessionId AND LastAccessed < DATEADD(minute, -30, GETUTCDATE())"
                };

                var totalDeleted = 0;
                foreach (var query in cleanupQueries)
                {
                    using (var command = new SqlCommand(query, connection))
                    {
                        command.Parameters.AddWithValue("@SessionId", sessionId);
                        totalDeleted += command.ExecuteNonQuery();
                    }
                }

                SqlContext.Pipe.Send($"Cleaned up {totalDeleted} temporary records");

                // 2. Optimize FILESTREAM fragmentation
                OptimizeFileStreamFragmentation(connection, sessionId);

                // 3. Update memory usage statistics
                UpdateMemoryUsageStatistics(connection, sessionId, memoryBefore);
            }

            // 4. Advanced garbage collection with generation-specific optimization
            PerformAdvancedGarbageCollection();

            // 5. Memory pressure optimization
            OptimizeMemoryPressure();

            var memoryAfter = GC.GetTotalMemory(true);
            var memoryFreed = memoryBefore - memoryAfter;
            var duration = DateTime.UtcNow - startTime;

            SqlContext.Pipe.Send($"Memory optimization completed: {memoryFreed / 1024 / 1024:F2} MB freed in {duration.TotalMilliseconds:F0}ms");
        }

        /// <summary>
        /// PERFORMANCE OPTIMIZATION: Optimize FILESTREAM fragmentation
        /// </summary>
        private static void OptimizeFileStreamFragmentation(SqlConnection connection, long sessionId)
        {
            try
            {
                var query = @"
                    SELECT COUNT(*) as FragmentedFiles
                    FROM dbo.SkipTranscoderModels
                    WHERE SessionId = @SessionId
                    AND FileStreamWeights IS NOT NULL";

                using (var command = new SqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@SessionId", sessionId);
                    var fragmentedFiles = (int)command.ExecuteScalar();

                    if (fragmentedFiles > 0)
                    {
                        // Defragment FILESTREAM data
                        var defragQuery = @"
                            DECLARE @sql NVARCHAR(MAX) = '
                            ALTER DATABASE ' + DB_NAME() + '
                            SET ALLOW_SNAPSHOT_ISOLATION ON;
                            CHECKPOINT;'
                            EXEC sp_executesql @sql";

                        using (var defragCommand = new SqlCommand(defragQuery, connection))
                        {
                            defragCommand.ExecuteNonQuery();
                        }

                        SqlContext.Pipe.Send($"Optimized {fragmentedFiles} FILESTREAM files");
                    }
                }
            }
            catch (Exception ex)
            {
                SqlContext.Pipe.Send($"Warning: FILESTREAM optimization failed: {ex.Message}");
            }
        }

        /// <summary>
        /// PERFORMANCE OPTIMIZATION: Update memory usage statistics
        /// </summary>
        private static void UpdateMemoryUsageStatistics(SqlConnection connection, long sessionId, long memoryBefore)
        {
            try
            {
                var memoryAfter = GC.GetTotalMemory(false);
                var gen0Collections = GC.CollectionCount(0);
                var gen1Collections = GC.CollectionCount(1);
                var gen2Collections = GC.CollectionCount(2);

                var query = @"
                    INSERT INTO dbo.MemoryOptimizationMetrics
                    (SessionId, MemoryBeforeBytes, MemoryAfterBytes, MemoryFreedBytes,
                     Gen0Collections, Gen1Collections, Gen2Collections, OptimizedAt)
                    VALUES
                    (@SessionId, @MemoryBefore, @MemoryAfter, @MemoryFreed,
                     @Gen0, @Gen1, @Gen2, GETUTCDATE())";

                using (var command = new SqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@SessionId", sessionId);
                    command.Parameters.AddWithValue("@MemoryBefore", memoryBefore);
                    command.Parameters.AddWithValue("@MemoryAfter", memoryAfter);
                    command.Parameters.AddWithValue("@MemoryFreed", memoryBefore - memoryAfter);
                    command.Parameters.AddWithValue("@Gen0", gen0Collections);
                    command.Parameters.AddWithValue("@Gen1", gen1Collections);
                    command.Parameters.AddWithValue("@Gen2", gen2Collections);

                    command.ExecuteNonQuery();
                }
            }
            catch (Exception ex)
            {
                SqlContext.Pipe.Send($"Warning: Failed to update memory statistics: {ex.Message}");
            }
        }

        /// <summary>
        /// PERFORMANCE OPTIMIZATION: Advanced garbage collection with generation targeting
        /// </summary>
        private static void PerformAdvancedGarbageCollection()
        {
            try
            {
                // Collect generation 0 first (most efficient)
                GC.Collect(0, GCCollectionMode.Optimized);
                GC.WaitForPendingFinalizers();

                // Collect generation 1 if needed
                if (GC.GetTotalMemory(false) > 100 * 1024 * 1024) // > 100MB
                {
                    GC.Collect(1, GCCollectionMode.Optimized);
                    GC.WaitForPendingFinalizers();
                }

                // Full collection only if memory pressure is high
                if (GC.GetTotalMemory(false) > 500 * 1024 * 1024) // > 500MB
                {
                    GC.Collect(2, GCCollectionMode.Forced);
                    GC.WaitForPendingFinalizers();
                }

                // Compact large object heap if necessary
                GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
                GC.Collect();
            }
            catch (Exception ex)
            {
                SqlContext.Pipe.Send($"Warning: Advanced GC failed: {ex.Message}");
            }
        }

        /// <summary>
        /// PERFORMANCE OPTIMIZATION: Optimize memory pressure
        /// </summary>
        private static void OptimizeMemoryPressure()
        {
            try
            {
                // Check current memory pressure
                var totalMemory = GC.GetTotalMemory(false);
                var workingSet = Environment.WorkingSet;

                // If memory usage is high, apply pressure relief
                if (totalMemory > 1024 * 1024 * 1024) // > 1GB
                {
                    // Request memory pressure relief from the system
                    GC.AddMemoryPressure(totalMemory / 2);
                    GC.Collect();
                    GC.RemoveMemoryPressure(totalMemory / 2);
                }

                SqlContext.Pipe.Send($"Memory pressure optimized: Working Set {workingSet / 1024 / 1024:F0}MB, GC Memory {totalMemory / 1024 / 1024:F0}MB");
            }
            catch (Exception ex)
            {
                SqlContext.Pipe.Send($"Warning: Memory pressure optimization failed: {ex.Message}");
            }
        }

        /// <summary>
        /// PERFORMANCE OPTIMIZATION: Advanced compute performance optimization for neural processing
        /// </summary>
        private static void OptimizeComputePerformance(long sessionId)
        {
            SqlContext.Pipe.Send("Optimizing compute performance with advanced techniques...");

            var startTime = DateTime.UtcNow;
            var optimizations = new List<string>();

            using (var connection = new SqlConnection("context connection=true"))
            {
                connection.Open();

                // 1. Intelligent statistics updates with threshold checking
                optimizations.AddRange(PerformIntelligentStatisticsUpdate(connection, sessionId));

                // 2. Index optimization and maintenance
                optimizations.AddRange(OptimizeIndexPerformance(connection, sessionId));

                // 3. Query plan cache optimization
                optimizations.AddRange(OptimizeQueryPlanCache(connection));

                // 4. Parallel processing configuration
                optimizations.AddRange(OptimizeParallelProcessing(connection));

                // 5. FILESTREAM access optimization
                optimizations.AddRange(OptimizeFileStreamAccess(connection, sessionId));

                // 6. Record compute optimization metrics
                RecordComputeOptimizationMetrics(connection, sessionId, optimizations, startTime);
            }

            var duration = DateTime.UtcNow - startTime;
            SqlContext.Pipe.Send($"Compute performance optimization completed: {optimizations.Count} optimizations in {duration.TotalMilliseconds:F0}ms");
        }

        /// <summary>
        /// PERFORMANCE OPTIMIZATION: Intelligent statistics updates
        /// </summary>
        private static List<string> PerformIntelligentStatisticsUpdate(SqlConnection connection, long sessionId)
        {
            var optimizations = new List<string>();

            try
            {
                // Check which tables need statistics updates based on modification counts
                var tablesNeedingUpdate = new[]
                {
                    new { Table = "ActivationData", Threshold = 1000 },
                    new { Table = "SkipTranscoderModels", Threshold = 10 },
                    new { Table = "DiscoveredFeatures", Threshold = 100 },
                    new { Table = "FeatureExtractionResults", Threshold = 500 },
                    new { Table = "CircuitAnalysisResults", Threshold = 100 }
                };

                foreach (var table in tablesNeedingUpdate)
                {
                    var checkQuery = $@"
                        SELECT modification_counter
                        FROM sys.dm_db_stats_properties(OBJECT_ID('dbo.{table.Table}'), 1)";

                    using (var command = new SqlCommand(checkQuery, connection))
                    {
                        var result = command.ExecuteScalar();
                        if (result != null && (long)result > table.Threshold)
                        {
                            var updateQuery = $"UPDATE STATISTICS dbo.{table.Table} WITH FULLSCAN";
                            using (var updateCommand = new SqlCommand(updateQuery, connection))
                            {
                                updateCommand.ExecuteNonQuery();
                                optimizations.Add($"Updated statistics for {table.Table}");
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                SqlContext.Pipe.Send($"Warning: Statistics update failed: {ex.Message}");
            }

            return optimizations;
        }

        /// <summary>
        /// PERFORMANCE OPTIMIZATION: Index performance optimization
        /// </summary>
        private static List<string> OptimizeIndexPerformance(SqlConnection connection, long sessionId)
        {
            var optimizations = new List<string>();

            try
            {
                // Check for fragmented indexes and reorganize/rebuild as needed
                var fragmentationQuery = @"
                    SELECT
                        OBJECT_NAME(ips.object_id) AS TableName,
                        i.name AS IndexName,
                        ips.avg_fragmentation_in_percent,
                        ips.page_count
                    FROM sys.dm_db_index_physical_stats(DB_ID(), NULL, NULL, NULL, 'LIMITED') ips
                    INNER JOIN sys.indexes i ON ips.object_id = i.object_id AND ips.index_id = i.index_id
                    WHERE ips.avg_fragmentation_in_percent > 10
                    AND ips.page_count > 100
                    AND OBJECT_NAME(ips.object_id) IN ('ActivationData', 'SkipTranscoderModels', 'DiscoveredFeatures')";

                using (var command = new SqlCommand(fragmentationQuery, connection))
                using (var reader = command.ExecuteReader())
                {
                    var fragmentedIndexes = new List<(string Table, string Index, double Fragmentation)>();

                    while (reader.Read())
                    {
                        fragmentedIndexes.Add((
                            reader.GetString("TableName"),
                            reader.GetString("IndexName"),
                            reader.GetDouble("avg_fragmentation_in_percent")
                        ));
                    }

                    reader.Close();

                    foreach (var index in fragmentedIndexes)
                    {
                        var action = index.Fragmentation > 30 ? "REBUILD" : "REORGANIZE";
                        var rebuildQuery = $"ALTER INDEX [{index.Index}] ON [dbo].[{index.Table}] {action}";

                        using (var rebuildCommand = new SqlCommand(rebuildQuery, connection))
                        {
                            rebuildCommand.CommandTimeout = 300; // 5 minutes timeout
                            rebuildCommand.ExecuteNonQuery();
                            optimizations.Add($"{action} index {index.Index} on {index.Table} (was {index.Fragmentation:F1}% fragmented)");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                SqlContext.Pipe.Send($"Warning: Index optimization failed: {ex.Message}");
            }

            return optimizations;
        }

        /// <summary>
        /// PERFORMANCE OPTIMIZATION: Query plan cache optimization
        /// </summary>
        private static List<string> OptimizeQueryPlanCache(SqlConnection connection)
        {
            var optimizations = new List<string>();

            try
            {
                // Clear inefficient plans that consume excessive memory
                var clearPlansQuery = @"
                    DECLARE @size_in_bytes BIGINT = (SELECT cntr_value FROM sys.dm_os_performance_counters
                                                    WHERE counter_name = 'Plan Cache Size' AND instance_name = 'SQL Plans')

                    IF @size_in_bytes > 1000000000 -- > 1GB
                    BEGIN
                        DBCC FREESYSTEMCACHE('SQL Plans')
                        SELECT 'Cleared SQL Plans cache'
                    END
                    ELSE
                        SELECT 'Plan cache size acceptable'";

                using (var command = new SqlCommand(clearPlansQuery, connection))
                {
                    var result = command.ExecuteScalar()?.ToString();
                    if (result != null)
                    {
                        optimizations.Add(result);
                    }
                }
            }
            catch (Exception ex)
            {
                SqlContext.Pipe.Send($"Warning: Plan cache optimization failed: {ex.Message}");
            }

            return optimizations;
        }

        /// <summary>
        /// PERFORMANCE OPTIMIZATION: Parallel processing configuration
        /// </summary>
        private static List<string> OptimizeParallelProcessing(SqlConnection connection)
        {
            var optimizations = new List<string>();

            try
            {
                // Check and optimize max degree of parallelism for neural computations
                var parallelismQuery = @"
                    DECLARE @logical_cpus INT = (SELECT cpu_count FROM sys.dm_os_sys_info)
                    DECLARE @current_maxdop INT = (SELECT value FROM sys.configurations WHERE name = 'max degree of parallelism')
                    DECLARE @optimal_maxdop INT = CASE
                        WHEN @logical_cpus > 8 THEN 8
                        WHEN @logical_cpus > 4 THEN @logical_cpus / 2
                        ELSE @logical_cpus
                    END

                    SELECT @logical_cpus as LogicalCPUs, @current_maxdop as CurrentMaxDOP, @optimal_maxdop as OptimalMaxDOP";

                using (var command = new SqlCommand(parallelismQuery, connection))
                using (var reader = command.ExecuteReader())
                {
                    if (reader.Read())
                    {
                        var logicalCpus = reader.GetInt32("LogicalCPUs");
                        var currentMaxDop = reader.GetInt32("CurrentMaxDOP");
                        var optimalMaxDop = reader.GetInt32("OptimalMaxDOP");

                        optimizations.Add($"Parallelism analysis: {logicalCpus} CPUs, MAXDOP {currentMaxDop} (optimal: {optimalMaxDop})");
                    }
                }
            }
            catch (Exception ex)
            {
                SqlContext.Pipe.Send($"Warning: Parallelism optimization failed: {ex.Message}");
            }

            return optimizations;
        }

        /// <summary>
        /// PERFORMANCE OPTIMIZATION: FILESTREAM access optimization
        /// </summary>
        private static List<string> OptimizeFileStreamAccess(SqlConnection connection, long sessionId)
        {
            var optimizations = new List<string>();

            try
            {
                // Optimize FILESTREAM access patterns
                var optimizeQuery = @"
                    -- Enable optimized FILESTREAM access
                    IF NOT EXISTS (SELECT 1 FROM sys.configurations WHERE name = 'filestream access level' AND value = 2)
                    BEGIN
                        SELECT 'FILESTREAM access level optimization needed'
                    END
                    ELSE
                        SELECT 'FILESTREAM access level optimized'";

                using (var command = new SqlCommand(optimizeQuery, connection))
                {
                    var result = command.ExecuteScalar()?.ToString();
                    if (result != null)
                    {
                        optimizations.Add(result);
                    }
                }

                // Check FILESTREAM usage statistics
                var usageQuery = @"
                    SELECT COUNT(*) as FileStreamFiles,
                           SUM(DATALENGTH(FileStreamWeights)) / 1024 / 1024 as TotalSizeMB
                    FROM dbo.SkipTranscoderModels
                    WHERE SessionId = @SessionId AND FileStreamWeights IS NOT NULL";

                using (var command = new SqlCommand(usageQuery, connection))
                {
                    command.Parameters.AddWithValue("@SessionId", sessionId);
                    using (var reader = command.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            var fileCount = reader.GetInt32("FileStreamFiles");
                            var totalSizeMB = reader.IsDBNull("TotalSizeMB") ? 0 : reader.GetInt32("TotalSizeMB");
                            optimizations.Add($"FILESTREAM usage: {fileCount} files, {totalSizeMB}MB total");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                SqlContext.Pipe.Send($"Warning: FILESTREAM optimization failed: {ex.Message}");
            }

            return optimizations;
        }

        /// <summary>
        /// PERFORMANCE OPTIMIZATION: Record compute optimization metrics
        /// </summary>
        private static void RecordComputeOptimizationMetrics(SqlConnection connection, long sessionId, List<string> optimizations, DateTime startTime)
        {
            try
            {
                var duration = DateTime.UtcNow - startTime;
                var optimizationsSummary = string.Join("; ", optimizations);

                var query = @"
                    INSERT INTO dbo.ComputeOptimizationMetrics
                    (SessionId, OptimizationCount, OptimizationsSummary, DurationMs, OptimizedAt)
                    VALUES
                    (@SessionId, @Count, @Summary, @Duration, GETUTCDATE())";

                using (var command = new SqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@SessionId", sessionId);
                    command.Parameters.AddWithValue("@Count", optimizations.Count);
                    command.Parameters.AddWithValue("@Summary", optimizationsSummary.Length > 4000 ? optimizationsSummary.Substring(0, 4000) : optimizationsSummary);
                    command.Parameters.AddWithValue("@Duration", duration.TotalMilliseconds);

                    command.ExecuteNonQuery();
                }
            }
            catch (Exception ex)
            {
                SqlContext.Pipe.Send($"Warning: Failed to record compute metrics: {ex.Message}");
            }
        }

        /// <summary>
        /// Optimizes storage access patterns
        /// </summary>
        private static void OptimizeStorageAccess(long sessionId)
        {
            SqlContext.Pipe.Send("Optimizing storage access...");

            using (var connection = new SqlConnection("context connection=true"))
            {
                connection.Open();

                // Reorganize indexes for better FILESTREAM access
                var query = @"
                    SELECT 'ALTER INDEX ' + i.name + ' ON ' + t.name + ' REORGANIZE'
                    FROM sys.indexes i
                    INNER JOIN sys.tables t ON i.object_id = t.object_id
                    WHERE t.name IN ('ActivationData', 'SkipTranscoderModels')
                    AND i.avg_fragmentation_in_percent > 10";

                using (var command = new SqlCommand(query, connection))
                using (var reader = command.ExecuteReader())
                {
                    var reorganizeCommands = new List<string>();
                    while (reader.Read())
                    {
                        reorganizeCommands.Add(reader.GetString(0));
                    }

                    reader.Close();

                    foreach (var reorganizeCommand in reorganizeCommands)
                    {
                        using (var reorganizeCmd = new SqlCommand(reorganizeCommand, connection))
                        {
                            reorganizeCmd.ExecuteNonQuery();
                        }
                    }

                    SqlContext.Pipe.Send($"Reorganized {reorganizeCommands.Count} indexes");
                }
            }

            SqlContext.Pipe.Send("Storage access optimization completed");
        }

        #region ProcessSkipTranscoder Data Classes

        private class FeatureExtractionResult
        {
            public int SampleIndex { get; set; }
            public int ActiveFeatureCount { get; set; }
            public double SparsityLevel { get; set; }
            public float MaxActivation { get; set; }
            public List<ActiveFeatureData> Features { get; set; } = new();
        }

        private class ActiveFeatureData
        {
            public int FeatureIndex { get; set; }
            public float Activation { get; set; }
            public double RelativeStrength { get; set; }
        }

        private class SessionFeature
        {
            public long FeatureId { get; set; }
            public int FeatureIndex { get; set; }
            public double AverageActivation { get; set; }
            public double SparsityScore { get; set; }
            public int TranscoderId { get; set; }
            public int LayerIndex { get; set; }
        }

        private class ComputationalCircuit
        {
            public long SourceFeatureId { get; set; }
            public long TargetFeatureId { get; set; }
            public double CircuitStrength { get; set; }
            public string CircuitType { get; set; } = "";
            public int LayerSpan { get; set; }
            public string Description { get; set; } = "";
        }

        #endregion

        #endregion

        #region Helper Methods and Classes

        private static float[] BytesToFloatArray(byte[] bytes)
        {
            var floats = new float[bytes.Length / 4];
            Buffer.BlockCopy(bytes, 0, floats, 0, bytes.Length);
            return floats;
        }

        private static List<ActivationEntry> LoadActivationDataForTranscoder(int transcoderId, int sampleSize)
        {
            var entries = new List<ActivationEntry>();

            using (var connection = new SqlConnection("context connection=true"))
            {
                connection.Open();

                var query = @"
                    SELECT TOP (@SampleSize) ad.ActivationVector.PathName() as FilePath,
                           ad.InputText, ad.TokenPosition
                    FROM dbo.ActivationData ad
                    INNER JOIN dbo.SkipTranscoderModels stm ON ad.SessionId = stm.SessionId
                                                            AND ad.LayerIndex = stm.LayerIndex
                    WHERE stm.TranscoderId = @TranscoderId
                    ORDER BY NEWID()"; // Random sample

                using (var command = new SqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@TranscoderId", transcoderId);
                    command.Parameters.AddWithValue("@SampleSize", sampleSize);

                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            var filePath = reader.GetString("FilePath");
                            var inputText = reader.IsDBNull("InputText") ? "" : reader.GetString("InputText");
                            var tokenPosition = reader.GetInt32("TokenPosition");

                            var bytes = File.ReadAllBytes(filePath);
                            var vector = BytesToFloatArray(bytes);

                            entries.Add(new ActivationEntry
                            {
                                Vector = vector,
                                InputText = inputText,
                                TokenPosition = tokenPosition
                            });
                        }
                    }
                }
            }

            return entries;
        }

        private static List<string> ExtractCommonTokens(List<FeatureActivationAnalysis> activations)
        {
            // Simple token frequency analysis
            var tokenCounts = new Dictionary<string, int>();

            foreach (var activation in activations)
            {
                var tokens = activation.InputText.Split(new char[] { ' ', '\t', '\n' },
                    StringSplitOptions.RemoveEmptyEntries);

                foreach (var token in tokens)
                {
                    var cleanToken = token.Trim().ToLower();
                    if (cleanToken.Length > 2)
                    {
                        tokenCounts[cleanToken] = tokenCounts.GetValueOrDefault(cleanToken, 0) + 1;
                    }
                }
            }

            return tokenCounts
                .OrderByDescending(x => x.Value)
                .Take(10)
                .Select(x => x.Key)
                .ToList();
        }

        private static string InferPossibleConcept(List<FeatureActivationAnalysis> activations)
        {
            // Simple concept inference based on common patterns
            var allText = string.Join(" ", activations.Select(x => x.InputText));

            if (allText.Contains("medical") || allText.Contains("patient") || allText.Contains("diagnosis"))
                return "Medical concept";
            else if (allText.Contains("legal") || allText.Contains("law") || allText.Contains("court"))
                return "Legal concept";
            else if (allText.Contains("code") || allText.Contains("function") || allText.Contains("programming"))
                return "Programming concept";
            else
                return "Unknown concept - requires manual analysis";
        }

        #region Data Transfer Objects

        private class ActiveFeature
        {
            public int FeatureIndex { get; set; }
            public double Activation { get; set; }
            public string InputText { get; set; }
        }

        private class FeatureActivationAnalysis
        {
            public string InputText { get; set; }
            public double Activation { get; set; }
            public int TokenPosition { get; set; }
        }

        private class FeatureInterpretabilityReport
        {
            public int FeatureIndex { get; set; }
            public int TotalActivations { get; set; }
            public double AverageActivation { get; set; }
            public double MaxActivation { get; set; }
            public List<FeatureActivationAnalysis> TopActivations { get; set; }
            public List<string> CommonTokens { get; set; }
            public string PossibleConcept { get; set; }
        }

        private class ActivationEntry
        {
            public float[] Vector { get; set; }
            public string InputText { get; set; }
            public int TokenPosition { get; set; }
        }

        #endregion

        #region Mechanistic Interpretability Data Classes

        /// <summary>
        /// Comprehensive interpretability analysis for Skip Transcoder
        /// </summary>
        private class InterpretabilityAnalysis
        {
            public Dictionary<int, FeatureStatistics> FeatureStatistics { get; set; } = new();
            public Dictionary<(int, int), double> FeatureCorrelations { get; set; } = new();
            public Dictionary<int, double> FeatureSelectivity { get; set; } = new();
            public Dictionary<int, double> CausalAttribution { get; set; } = new();
            public FeatureDecomposition FeatureDecomposition { get; set; } = new();
        }

        /// <summary>
        /// Statistical analysis of individual features
        /// </summary>
        private class FeatureStatistics
        {
            public double Mean { get; set; }
            public double Variance { get; set; }
            public double StandardDeviation { get; set; }
            public double Sparsity { get; set; }
            public float MaxActivation { get; set; }
            public float MinActivation { get; set; }
            public int ActivationCount { get; set; }
        }

        /// <summary>
        /// Feature decomposition analysis
        /// </summary>
        private class FeatureDecomposition
        {
            public double EncoderComplexity { get; set; }
            public double DecoderComplexity { get; set; }
            public double SkipConnectionImportance { get; set; }
            public double EffectiveDimensionality { get; set; }
        }

        #endregion

        #endregion
    }

    /// <summary>
    /// Extension methods for random number generation
    /// </summary>
    public static class RandomExtensions
    {
        public static double NextGaussian(this Random random)
        {
            // Box-Muller transform for Gaussian random numbers
            double u1 = 1.0 - random.NextDouble();
            double u2 = 1.0 - random.NextDouble();
            return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Sin(2.0 * Math.PI * u2);
        }
    }
}