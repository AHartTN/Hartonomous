/*
 * Hartonomous AI Agent Factory Platform
 * Mechanistic Interpretability Service - Advanced Neural Network Analysis and Circuit Discovery
 *
 * Copyright (c) 2024-2025 All Rights Reserved.
 * This software is proprietary and confidential. No part of this software may be reproduced,
 * distributed, or transmitted in any form or by any means without the prior written permission
 * of the copyright holder.
 *
 * This service implements cutting-edge mechanistic interpretability techniques to understand
 * how neural networks process information. It provides neural pattern analysis, circuit discovery,
 * causal mechanism detection, and attention visualization for transparent AI development.
 *
 * PROPRIETARY ALGORITHMS: This module contains proprietary research algorithms for neural
 * interpretability. Reverse engineering or extraction of these algorithms is strictly prohibited.
 *
 * Author: AI Agent Factory Development Team
 * Created: 2024
 * Last Modified: 2025
 */

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Hartonomous.Core.Data;
using Hartonomous.Core.Models;
using Hartonomous.Core.Interfaces;
using System.Text.Json;

namespace Hartonomous.Core.Services;

/// <summary>
/// Advanced mechanistic interpretability service that provides deep insights into neural network behavior.
///
/// This service is a core component of the Hartonomous platform's interpretability capabilities, offering:
/// - Neural pattern analysis using proprietary algorithms
/// - Component interpretation for understanding individual neurons and layers
/// - Attention pattern visualization for transformer-based models
/// - Causal mechanism discovery for identifying computational circuits
/// - Feature attribution and importance analysis
///
/// The service integrates with SQL Server CLR functions for high-performance neural analysis
/// and connects to Neo4j for graph-based circuit discovery and relationship mapping.
///
/// All analysis results are automatically scoped by user ID for multi-tenant isolation.
/// </summary>
public class MechanisticInterpretabilityService
{
    private readonly HartonomousDbContext _context;
    private readonly IModelRepository _modelRepository;
    private readonly IModelComponentRepository _componentRepository;
    private readonly IKnowledgeGraphRepository _knowledgeGraphRepository;
    private readonly ILogger<MechanisticInterpretabilityService> _logger;

    public MechanisticInterpretabilityService(
        HartonomousDbContext context,
        IModelRepository modelRepository,
        IModelComponentRepository componentRepository,
        IKnowledgeGraphRepository knowledgeGraphRepository,
        ILogger<MechanisticInterpretabilityService> logger)
    {
        _context = context;
        _modelRepository = modelRepository;
        _componentRepository = componentRepository;
        _knowledgeGraphRepository = knowledgeGraphRepository;
        _logger = logger;
    }

    /// <summary>
    /// Analyzes neural patterns in a model using SQL CLR functions
    /// </summary>
    public async Task<NeuralPatternAnalysisResult> AnalyzeNeuralPatternsAsync(Guid modelId, string userId, NeuralPatternRequest request)
    {
        var model = await _modelRepository.GetByIdAsync(modelId, userId);
        if (model == null)
            throw new ArgumentException("Model not found", nameof(modelId));

        var patterns = new List<NeuralPattern>();

        try
        {
            // Use SQL CLR function for neural pattern extraction
            var parameters = JsonSerializer.Serialize(request);
            var sqlResult = await _context.Database
                .SqlQueryRaw<string>("SELECT dbo.ExtractNeuralPatterns(@p0, @p1, @p2, @p3)",
                    modelId, request.PatternType, parameters, userId)
                .FirstOrDefaultAsync();

            if (!string.IsNullOrEmpty(sqlResult))
            {
                patterns = JsonSerializer.Deserialize<List<NeuralPattern>>(sqlResult) ?? new List<NeuralPattern>();
            }

            // Store patterns in database for future analysis
            await StorePatternsAsync(modelId, patterns, userId);

            return new NeuralPatternAnalysisResult
            {
                ModelId = modelId,
                PatternType = request.PatternType,
                PatternsFound = patterns.Count,
                Patterns = patterns,
                AnalysisTimestamp = DateTime.UtcNow,
                Statistics = CalculatePatternStatistics(patterns)
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error analyzing neural patterns for model {ModelId}", modelId);
            throw;
        }
    }

    /// <summary>
    /// Interprets individual neurons using activation analysis
    /// </summary>
    public async Task<NeuronInterpretationResult> InterpretNeuronAsync(Guid componentId, string userId, NeuronInterpretationRequest request)
    {
        var component = await _componentRepository.GetByIdAsync(componentId, userId);
        if (component == null)
            throw new ArgumentException("Component not found", nameof(componentId));

        try
        {
            // Use SQL CLR for neuron analysis
            var parameters = JsonSerializer.Serialize(request);
            var sqlResult = await _context.Database
                .SqlQueryRaw<string>("SELECT dbo.AnalyzeNeuronBehavior(@p0, @p1, @p2)",
                    componentId, parameters, userId)
                .FirstOrDefaultAsync();

            NeuronAnalysis analysis;
            if (!string.IsNullOrEmpty(sqlResult))
            {
                analysis = JsonSerializer.Deserialize<NeuronAnalysis>(sqlResult) ?? new NeuronAnalysis();
            }
            else
            {
                analysis = new NeuronAnalysis();
            }

            // Create or update neuron interpretation
            var interpretation = await _context.NeuronInterpretations
                .FirstOrDefaultAsync(ni => ni.ComponentId == componentId && ni.UserId == userId);

            if (interpretation == null)
            {
                interpretation = new NeuronInterpretation
                {
                    ModelId = component.ModelId,
                    LayerId = component.LayerId,
                    ComponentId = componentId,
                    UserId = userId
                };
                _context.NeuronInterpretations.Add(interpretation);
            }

            interpretation.FunctionalInterpretation = analysis.FunctionalDescription;
            interpretation.SetDetectedConcepts(analysis.DetectedConcepts);
            interpretation.SetActivationStatistics(analysis.ActivationStatistics);
            interpretation.SetMaximalActivationExamples(analysis.MaxActivationExamples);
            interpretation.InterpretationConfidence = analysis.Confidence;
            interpretation.InterpretationMethod = request.Method;
            interpretation.LastUpdated = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            return new NeuronInterpretationResult
            {
                ComponentId = componentId,
                Interpretation = interpretation,
                Analysis = analysis,
                Timestamp = DateTime.UtcNow
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error interpreting neuron for component {ComponentId}", componentId);
            throw;
        }
    }

    /// <summary>
    /// Analyzes attention patterns in transformer models
    /// </summary>
    public async Task<AttentionAnalysisResult> AnalyzeAttentionPatternsAsync(Guid modelId, string userId, AttentionAnalysisRequest request)
    {
        var model = await _modelRepository.GetByIdAsync(modelId, userId);
        if (model == null)
            throw new ArgumentException("Model not found", nameof(modelId));

        try
        {
            // Get attention heads for the model
            var attentionHeads = await _context.AttentionHeads
                .Where(ah => ah.ModelId == modelId && ah.UserId == userId)
                .Include(ah => ah.ActivationPatterns)
                .ToListAsync();

            var analysisResults = new List<AttentionHeadAnalysis>();

            foreach (var head in attentionHeads)
            {
                // Analyze attention patterns using SQL CLR
                var parameters = JsonSerializer.Serialize(request);
                var sqlResult = await _context.Database
                    .SqlQueryRaw<string>("SELECT dbo.AnalyzeAttentionPatterns(@p0, @p1, @p2)",
                        head.AttentionHeadId, parameters, userId)
                    .FirstOrDefaultAsync();

                AttentionHeadAnalysis headAnalysis;
                if (!string.IsNullOrEmpty(sqlResult))
                {
                    headAnalysis = JsonSerializer.Deserialize<AttentionHeadAnalysis>(sqlResult) ?? new AttentionHeadAnalysis();
                }
                else
                {
                    headAnalysis = new AttentionHeadAnalysis();
                }

                headAnalysis.AttentionHeadId = head.AttentionHeadId;
                headAnalysis.HeadIndex = head.HeadIndex;
                headAnalysis.LayerId = head.LayerId;

                analysisResults.Add(headAnalysis);

                // Update attention head with analysis results
                head.AttentionPatternType = headAnalysis.PatternType;
                head.FunctionalDescription = headAnalysis.FunctionalDescription;
                head.SetAttentionStatistics(headAnalysis.Statistics);
                head.SetExamplePatterns(headAnalysis.ExamplePatterns);
                head.SpecificityScore = headAnalysis.SpecificityScore;
                head.ImportanceScore = headAnalysis.ImportanceScore;
                head.LastUpdated = DateTime.UtcNow;
            }

            await _context.SaveChangesAsync();

            return new AttentionAnalysisResult
            {
                ModelId = modelId,
                HeadAnalyses = analysisResults,
                GlobalStatistics = CalculateGlobalAttentionStatistics(analysisResults),
                Timestamp = DateTime.UtcNow
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error analyzing attention patterns for model {ModelId}", modelId);
            throw;
        }
    }

    /// <summary>
    /// Discovers causal mechanisms between model components
    /// </summary>
    public async Task<CausalMechanismResult> DiscoverCausalMechanismsAsync(Guid modelId, string userId, CausalAnalysisRequest request)
    {
        var model = await _modelRepository.GetByIdAsync(modelId, userId);
        if (model == null)
            throw new ArgumentException("Model not found", nameof(modelId));

        try
        {
            // Use SQL CLR for causal mechanism discovery
            var parameters = JsonSerializer.Serialize(request);
            var sqlResult = await _context.Database
                .SqlQueryRaw<string>("SELECT dbo.DiscoverCausalMechanisms(@p0, @p1, @p2)",
                    modelId, parameters, userId)
                .FirstOrDefaultAsync();

            List<CausalMechanism> mechanisms;
            if (!string.IsNullOrEmpty(sqlResult))
            {
                mechanisms = JsonSerializer.Deserialize<List<CausalMechanism>>(sqlResult) ?? new List<CausalMechanism>();
            }
            else
            {
                mechanisms = new List<CausalMechanism>();
            }

            // Create capability mappings for discovered mechanisms
            foreach (var mechanism in mechanisms)
            {
                var mapping = new CapabilityMapping
                {
                    ModelId = modelId,
                    ComponentId = mechanism.SourceComponentId,
                    CapabilityName = mechanism.Capability,
                    Description = mechanism.Description,
                    Category = "causal_mechanism",
                    CapabilityStrength = mechanism.Strength,
                    MappingConfidence = mechanism.Confidence,
                    MappingMethod = "causal_analysis",
                    UserId = userId
                };

                mapping.SetEvidence(mechanism.Evidence);
                mapping.SetAnalysisResults(mechanism.AnalysisData);

                _context.CapabilityMappings.Add(mapping);

                // Create knowledge graph relationships for causal mechanisms
                try
                {
                    await _knowledgeGraphRepository.CreateComponentRelationshipAsync(
                        mechanism.SourceComponentId,
                        mechanism.TargetComponentId,
                        "CAUSALLY_INFLUENCES",
                        userId);

                    _logger.LogDebug("Created causal relationship in knowledge graph: {Source} -> {Target}",
                        mechanism.SourceComponentId, mechanism.TargetComponentId);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to create knowledge graph relationship for mechanism {SourceId} -> {TargetId}",
                        mechanism.SourceComponentId, mechanism.TargetComponentId);
                    // Continue with other mechanisms - knowledge graph errors are not critical
                }
            }

            await _context.SaveChangesAsync();

            return new CausalMechanismResult
            {
                ModelId = modelId,
                Mechanisms = mechanisms,
                Statistics = CalculateCausalStatistics(mechanisms),
                Timestamp = DateTime.UtcNow
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error discovering causal mechanisms for model {ModelId}", modelId);
            throw;
        }
    }

    /// <summary>
    /// Gets comprehensive interpretability report for a model
    /// </summary>
    public async Task<ModelInterpretabilityReport> GetModelInterpretabilityReportAsync(Guid modelId, string userId)
    {
        var model = await _modelRepository.GetByIdAsync(modelId, userId);
        if (model == null)
            throw new ArgumentException("Model not found", nameof(modelId));

        try
        {
            // Gather all interpretability data
            var neuronInterpretations = await _context.NeuronInterpretations
                .Where(ni => ni.ModelId == modelId && ni.UserId == userId)
                .ToListAsync();

            var attentionHeads = await _context.AttentionHeads
                .Where(ah => ah.ModelId == modelId && ah.UserId == userId)
                .ToListAsync();

            var activationPatterns = await _context.ActivationPatterns
                .Where(ap => ap.ModelId == modelId && ap.UserId == userId)
                .ToListAsync();

            var capabilityMappings = await _context.CapabilityMappings
                .Where(cm => cm.ModelId == modelId && cm.UserId == userId)
                .ToListAsync();

            return new ModelInterpretabilityReport
            {
                ModelId = modelId,
                ModelName = model.ModelName,
                NeuronInterpretations = neuronInterpretations,
                AttentionHeads = attentionHeads,
                ActivationPatterns = activationPatterns,
                CapabilityMappings = capabilityMappings,
                Summary = GenerateInterpretabilitySummary(neuronInterpretations, attentionHeads, activationPatterns, capabilityMappings),
                GeneratedAt = DateTime.UtcNow
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating interpretability report for model {ModelId}", modelId);
            throw;
        }
    }

    private async Task StorePatternsAsync(Guid modelId, List<NeuralPattern> patterns, string userId)
    {
        foreach (var pattern in patterns)
        {
            var activationPattern = new ActivationPattern
            {
                ModelId = modelId,
                ComponentId = pattern.ComponentId,
                PatternType = pattern.Type,
                TriggerContext = pattern.Context,
                PatternStrength = pattern.Strength,
                PatternDuration = pattern.Duration,
                Frequency = pattern.Frequency,
                UserId = userId
            };

            activationPattern.SetActivationData(pattern.Data);
            activationPattern.SetPatternStatistics(pattern.Statistics);

            _context.ActivationPatterns.Add(activationPattern);
        }

        await _context.SaveChangesAsync();
    }

    private Dictionary<string, object> CalculatePatternStatistics(List<NeuralPattern> patterns)
    {
        if (!patterns.Any()) return new Dictionary<string, object>();

        return new Dictionary<string, object>
        {
            ["total_patterns"] = patterns.Count,
            ["pattern_types"] = patterns.GroupBy(p => p.Type).ToDictionary(g => g.Key, g => g.Count()),
            ["average_strength"] = patterns.Average(p => p.Strength),
            ["max_strength"] = patterns.Max(p => p.Strength),
            ["average_frequency"] = patterns.Average(p => p.Frequency),
            ["component_coverage"] = patterns.Select(p => p.ComponentId).Distinct().Count()
        };
    }

    private Dictionary<string, object> CalculateGlobalAttentionStatistics(List<AttentionHeadAnalysis> analyses)
    {
        if (!analyses.Any()) return new Dictionary<string, object>();

        return new Dictionary<string, object>
        {
            ["total_heads"] = analyses.Count,
            ["pattern_types"] = analyses.GroupBy(a => a.PatternType).ToDictionary(g => g.Key, g => g.Count()),
            ["average_specificity"] = analyses.Average(a => a.SpecificityScore),
            ["average_importance"] = analyses.Average(a => a.ImportanceScore),
            ["high_importance_heads"] = analyses.Count(a => a.ImportanceScore > 0.8)
        };
    }

    private Dictionary<string, object> CalculateCausalStatistics(List<CausalMechanism> mechanisms)
    {
        if (!mechanisms.Any()) return new Dictionary<string, object>();

        return new Dictionary<string, object>
        {
            ["total_mechanisms"] = mechanisms.Count,
            ["capabilities"] = mechanisms.GroupBy(m => m.Capability).ToDictionary(g => g.Key, g => g.Count()),
            ["average_strength"] = mechanisms.Average(m => m.Strength),
            ["average_confidence"] = mechanisms.Average(m => m.Confidence),
            ["high_confidence_mechanisms"] = mechanisms.Count(m => m.Confidence > 0.8)
        };
    }

    private InterpretabilitySummary GenerateInterpretabilitySummary(
        List<NeuronInterpretation> neurons,
        List<AttentionHead> heads,
        List<ActivationPattern> patterns,
        List<CapabilityMapping> capabilities)
    {
        return new InterpretabilitySummary
        {
            TotalNeuronsAnalyzed = neurons.Count,
            HighConfidenceInterpretations = neurons.Count(n => n.InterpretationConfidence > 0.8),
            AttentionHeadsAnalyzed = heads.Count,
            UniquePatternTypes = patterns.Select(p => p.PatternType).Distinct().Count(),
            IdentifiedCapabilities = capabilities.Select(c => c.CapabilityName).Distinct().Count(),
            OverallInterpretabilityScore = CalculateOverallScore(neurons, heads, patterns, capabilities)
        };
    }

    private double CalculateOverallScore(
        List<NeuronInterpretation> neurons,
        List<AttentionHead> heads,
        List<ActivationPattern> patterns,
        List<CapabilityMapping> capabilities)
    {
        var factors = new List<double>();

        if (neurons.Any())
            factors.Add(neurons.Average(n => n.InterpretationConfidence));

        if (heads.Any())
            factors.Add(heads.Average(h => h.ImportanceScore));

        if (capabilities.Any())
            factors.Add(capabilities.Average(c => c.MappingConfidence));

        return factors.Any() ? factors.Average() : 0.0;
    }
}

// Supporting data structures
public class NeuralPatternRequest
{
    public string PatternType { get; set; } = string.Empty;
    public Dictionary<string, object> Parameters { get; set; } = new();
}

public class NeuralPattern
{
    public Guid ComponentId { get; set; }
    public string Type { get; set; } = string.Empty;
    public string Context { get; set; } = string.Empty;
    public double Strength { get; set; }
    public double Duration { get; set; }
    public double Frequency { get; set; }
    public Dictionary<string, object> Data { get; set; } = new();
    public Dictionary<string, object> Statistics { get; set; } = new();
}

public class NeuralPatternAnalysisResult
{
    public Guid ModelId { get; set; }
    public string PatternType { get; set; } = string.Empty;
    public int PatternsFound { get; set; }
    public List<NeuralPattern> Patterns { get; set; } = new();
    public DateTime AnalysisTimestamp { get; set; }
    public Dictionary<string, object> Statistics { get; set; } = new();
}

public class NeuronInterpretationRequest
{
    public string Method { get; set; } = string.Empty;
    public Dictionary<string, object> Parameters { get; set; } = new();
}

public class NeuronAnalysis
{
    public string FunctionalDescription { get; set; } = string.Empty;
    public List<string> DetectedConcepts { get; set; } = new();
    public Dictionary<string, object> ActivationStatistics { get; set; } = new();
    public List<object> MaxActivationExamples { get; set; } = new();
    public double Confidence { get; set; }
}

public class NeuronInterpretationResult
{
    public Guid ComponentId { get; set; }
    public NeuronInterpretation Interpretation { get; set; } = null!;
    public NeuronAnalysis Analysis { get; set; } = null!;
    public DateTime Timestamp { get; set; }
}

public class AttentionAnalysisRequest
{
    public string AnalysisType { get; set; } = string.Empty;
    public Dictionary<string, object> Parameters { get; set; } = new();
}

public class AttentionHeadAnalysis
{
    public Guid AttentionHeadId { get; set; }
    public int HeadIndex { get; set; }
    public int Index { get; set; }
    public Guid LayerId { get; set; }
    public string PatternType { get; set; } = string.Empty;
    public string FunctionalDescription { get; set; } = string.Empty;
    public Dictionary<string, object> Statistics { get; set; } = new();
    public List<object> ExamplePatterns { get; set; } = new();
    public double SpecificityScore { get; set; }
    public double ImportanceScore { get; set; }

    /// <summary>
    /// Human-readable description of the attention head's analysis results
    /// Provides interpretable insights into the head's computational behavior
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Example inputs that strongly activate this attention head
    /// List of representative input samples for interpretability analysis
    /// </summary>
    public List<object> ExampleInputs { get; set; } = new();

    /// <summary>
    /// Strength of the identified attention pattern
    /// Indicates how consistent and pronounced the pattern is across inputs
    /// </summary>
    public double PatternStrength { get; set; } = 0.0;
}

public class AttentionAnalysisResult
{
    public Guid ModelId { get; set; }
    public List<AttentionHeadAnalysis> HeadAnalyses { get; set; } = new();
    public Dictionary<string, object> GlobalStatistics { get; set; } = new();
    public DateTime Timestamp { get; set; }
}

public class CausalAnalysisRequest
{
    public string AnalysisType { get; set; } = string.Empty;
    public Dictionary<string, object> Parameters { get; set; } = new();
}

public class CausalMechanism
{
    public Guid SourceComponentId { get; set; }
    public Guid TargetComponentId { get; set; }
    public string Capability { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public double Strength { get; set; }
    public double Confidence { get; set; }
    public List<object> Evidence { get; set; } = new();
    public Dictionary<string, object> AnalysisData { get; set; } = new();
}

public class CausalMechanismResult
{
    public Guid ModelId { get; set; }
    public List<CausalMechanism> Mechanisms { get; set; } = new();
    public Dictionary<string, object> Statistics { get; set; } = new();
    public DateTime Timestamp { get; set; }
}

public class ModelInterpretabilityReport
{
    public Guid ModelId { get; set; }
    public string ModelName { get; set; } = string.Empty;
    public List<NeuronInterpretation> NeuronInterpretations { get; set; } = new();
    public List<AttentionHead> AttentionHeads { get; set; } = new();
    public List<ActivationPattern> ActivationPatterns { get; set; } = new();
    public List<CapabilityMapping> CapabilityMappings { get; set; } = new();
    public InterpretabilitySummary Summary { get; set; } = null!;
    public DateTime GeneratedAt { get; set; }
}

public class InterpretabilitySummary
{
    public int TotalNeuronsAnalyzed { get; set; }
    public int HighConfidenceInterpretations { get; set; }
    public int AttentionHeadsAnalyzed { get; set; }
    public int UniquePatternTypes { get; set; }
    public int IdentifiedCapabilities { get; set; }
    public double OverallInterpretabilityScore { get; set; }
}