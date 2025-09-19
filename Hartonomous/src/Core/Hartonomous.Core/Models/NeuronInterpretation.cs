/*
 * Copyright (c) 2024-2025 Hartonomous AI Agent Factory Platform. All Rights Reserved.
 *
 * This software is proprietary and confidential. Unauthorized copying, distribution,
 * modification, or use of this software, in whole or in part, is strictly prohibited.
 *
 * This file contains the NeuronInterpretation entity for mechanistic interpretability,
 * enabling explainable AI through individual neuron function analysis and concept detection.
 */

using System.ComponentModel.DataAnnotations;
using System.Text.Json;

namespace Hartonomous.Core.Models;

/// <summary>
/// Represents interpretability analysis results for individual neurons
/// Core entity for mechanistic interpretability and explainable AI
/// </summary>
public class NeuronInterpretation
{
    public Guid InterpretationId { get; set; } = Guid.NewGuid();

    [Required]
    public Guid ModelId { get; set; }

    [Required]
    public Guid LayerId { get; set; }

    [Required]
    public Guid ComponentId { get; set; }

    public int NeuronIndex { get; set; }

    /// <summary>
    /// Human-readable interpretation of neuron function
    /// </summary>
    [MaxLength(1000)]
    public string FunctionalInterpretation { get; set; } = string.Empty;

    /// <summary>
    /// Detected concepts that activate this neuron
    /// </summary>
    public string DetectedConcepts { get; set; } = "[]";

    /// <summary>
    /// Activation patterns and statistics
    /// </summary>
    public string ActivationStatistics { get; set; } = "{}";

    /// <summary>
    /// Examples of inputs that maximally activate this neuron
    /// </summary>
    public string MaximalActivationExamples { get; set; } = "[]";

    /// <summary>
    /// Confidence in the interpretation
    /// </summary>
    public double InterpretationConfidence { get; set; } = 0.0;

    /// <summary>
    /// Method used for interpretation
    /// </summary>
    [MaxLength(100)]
    public string InterpretationMethod { get; set; } = string.Empty; // activation_maximization, feature_visualization, probing

    /// <summary>
    /// Multi-tenant isolation
    /// </summary>
    [Required]
    [MaxLength(128)]
    public string UserId { get; set; } = string.Empty;

    public DateTime AnalyzedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastUpdated { get; set; }

    // Navigation properties
    public virtual Model Model { get; set; } = null!;
    public virtual ModelLayer Layer { get; set; } = null!;
    public virtual ModelComponent Component { get; set; } = null!;

    /// <summary>
    /// Get detected concepts as strongly typed list
    /// </summary>
    public List<string> GetDetectedConceptsList()
    {
        if (string.IsNullOrEmpty(DetectedConcepts) || DetectedConcepts == "[]")
            return new List<string>();

        return JsonSerializer.Deserialize<List<string>>(DetectedConcepts) ?? new List<string>();
    }

    /// <summary>
    /// Set detected concepts from list
    /// </summary>
    public void SetDetectedConcepts(List<string> concepts)
    {
        DetectedConcepts = JsonSerializer.Serialize(concepts);
    }

    /// <summary>
    /// Get activation statistics as typed object
    /// </summary>
    public T? GetActivationStatistics<T>() where T : class
    {
        if (string.IsNullOrEmpty(ActivationStatistics) || ActivationStatistics == "{}")
            return null;

        return JsonSerializer.Deserialize<T>(ActivationStatistics);
    }

    /// <summary>
    /// Set activation statistics from typed object
    /// </summary>
    public void SetActivationStatistics<T>(T statistics) where T : class
    {
        ActivationStatistics = JsonSerializer.Serialize(statistics);
    }

    /// <summary>
    /// Get maximal activation examples as typed list
    /// </summary>
    public List<T> GetMaximalActivationExamples<T>() where T : class
    {
        if (string.IsNullOrEmpty(MaximalActivationExamples) || MaximalActivationExamples == "[]")
            return new List<T>();

        return JsonSerializer.Deserialize<List<T>>(MaximalActivationExamples) ?? new List<T>();
    }

    /// <summary>
    /// Set maximal activation examples from typed list
    /// </summary>
    public void SetMaximalActivationExamples<T>(List<T> examples) where T : class
    {
        MaximalActivationExamples = JsonSerializer.Serialize(examples);
    }
}