using Hartonomous.Orchestration.DTOs;

namespace Hartonomous.Orchestration.Interfaces;

/// <summary>
/// Interface for workflow template management
/// </summary>
public interface IWorkflowTemplateService
{
    /// <summary>
    /// Create a new workflow template
    /// </summary>
    Task<Guid> CreateTemplateAsync(CreateTemplateFromWorkflowRequest request, string userId);

    /// <summary>
    /// Get a template by ID
    /// </summary>
    Task<WorkflowTemplateDto?> GetTemplateByIdAsync(Guid templateId, string userId);

    /// <summary>
    /// Update an existing template
    /// </summary>
    Task<bool> UpdateTemplateAsync(Guid templateId, WorkflowTemplateDto template, string userId);

    /// <summary>
    /// Delete a template
    /// </summary>
    Task<bool> DeleteTemplateAsync(Guid templateId, string userId);

    /// <summary>
    /// Search templates by criteria
    /// </summary>
    Task<PaginatedResult<WorkflowTemplateDto>> SearchTemplatesAsync(
        string? query, string? category, List<string>? tags, bool includePublic, string userId,
        int page = 1, int pageSize = 20);

    /// <summary>
    /// Get templates by category
    /// </summary>
    Task<List<WorkflowTemplateDto>> GetTemplatesByCategoryAsync(string category, bool includePublic, string userId);

    /// <summary>
    /// Get popular templates
    /// </summary>
    Task<List<WorkflowTemplateDto>> GetPopularTemplatesAsync(int limit = 10, bool includePublic = true);

    /// <summary>
    /// Create workflow from template
    /// </summary>
    Task<Guid> CreateWorkflowFromTemplateAsync(Guid templateId, string workflowName,
        Dictionary<string, object>? parameters, string userId);

    /// <summary>
    /// Validate template parameters
    /// </summary>
    Task<WorkflowValidationResult> ValidateTemplateParametersAsync(Guid templateId,
        Dictionary<string, object> parameters, string userId);

    /// <summary>
    /// Get template usage statistics
    /// </summary>
    Task<Dictionary<string, object>> GetTemplateUsageStatsAsync(Guid templateId, string userId);

    /// <summary>
    /// Export template to file
    /// </summary>
    Task<byte[]> ExportTemplateAsync(Guid templateId, string userId);

    /// <summary>
    /// Import template from file
    /// </summary>
    Task<Guid> ImportTemplateAsync(byte[] templateData, string userId);
}