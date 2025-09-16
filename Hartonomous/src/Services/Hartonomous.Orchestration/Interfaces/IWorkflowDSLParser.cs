using Hartonomous.Orchestration.DSL;

namespace Hartonomous.Orchestration.Interfaces;

/// <summary>
/// Interface for workflow DSL parsing and validation
/// </summary>
public interface IWorkflowDSLParser
{
    /// <summary>
    /// Parse workflow definition from DSL
    /// </summary>
    Task<WorkflowGraph> ParseWorkflowAsync(string dslContent);

    /// <summary>
    /// Parse workflow definition from YAML
    /// </summary>
    Task<WorkflowGraph> ParseWorkflowFromYamlAsync(string yamlContent);

    /// <summary>
    /// Parse workflow definition from JSON
    /// </summary>
    Task<WorkflowGraph> ParseWorkflowFromJsonAsync(string jsonContent);

    /// <summary>
    /// Validate workflow DSL syntax
    /// </summary>
    Task<WorkflowValidationResult> ValidateDSLAsync(string dslContent);

    /// <summary>
    /// Convert workflow graph to executable format
    /// </summary>
    Task<string> ConvertToExecutableAsync(WorkflowGraph graph);

    /// <summary>
    /// Generate DSL from workflow graph
    /// </summary>
    Task<string> GenerateDSLAsync(WorkflowGraph graph);

    /// <summary>
    /// Get supported DSL version
    /// </summary>
    string GetSupportedVersion();

    /// <summary>
    /// Validate workflow dependencies
    /// </summary>
    Task<WorkflowValidationResult> ValidateDependenciesAsync(WorkflowGraph graph);

    /// <summary>
    /// Optimize workflow graph
    /// </summary>
    Task<WorkflowGraph> OptimizeGraphAsync(WorkflowGraph graph);
}