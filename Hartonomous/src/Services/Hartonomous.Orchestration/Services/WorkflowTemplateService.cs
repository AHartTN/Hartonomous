using Hartonomous.Orchestration.DTOs;
using Hartonomous.Orchestration.Interfaces;
using Hartonomous.Orchestration.Models;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace Hartonomous.Orchestration.Services;

/// <summary>
/// Workflow template service implementation
/// </summary>
public class WorkflowTemplateService : IWorkflowTemplateService
{
    private readonly IWorkflowRepository _workflowRepository;
    private readonly IWorkflowTemplateRepository _templateRepository;
    private readonly IWorkflowDSLParser _dslParser;
    private readonly ILogger<WorkflowTemplateService> _logger;

    public WorkflowTemplateService(
        IWorkflowRepository workflowRepository,
        IWorkflowTemplateRepository templateRepository,
        IWorkflowDSLParser dslParser,
        ILogger<WorkflowTemplateService> logger)
    {
        _workflowRepository = workflowRepository;
        _templateRepository = templateRepository;
        _dslParser = dslParser;
        _logger = logger;
    }

    public async Task<Guid> CreateTemplateAsync(CreateTemplateFromWorkflowRequest request, string userId)
    {
        try
        {
            _logger.LogInformation("Creating template from workflow {WorkflowId} for user {UserId}",
                request.WorkflowId, userId);

            // Get the source workflow
            var workflow = await _workflowRepository.GetWorkflowByIdAsync(request.WorkflowId, userId);
            if (workflow == null)
            {
                throw new ArgumentException($"Workflow {request.WorkflowId} not found");
            }

            // Parse workflow to extract parameters
            var workflowGraph = await _dslParser.ParseWorkflowAsync(workflow.WorkflowDefinition);
            var parameters = ExtractParametersFromWorkflow(workflowGraph);

            // Create template definition
            var templateDefinition = CreateTemplateDefinition(workflowGraph, parameters);

            // Create template record with real database persistence
            var templateId = Guid.NewGuid();
            var now = DateTime.UtcNow;

            var template = new WorkflowTemplate
            {
                TemplateId = templateId,
                UserId = userId,
                Name = request.Name,
                Description = request.Description,
                Category = request.Category ?? "General",
                TemplateDefinitionJson = templateDefinition,
                ParametersJson = JsonSerializer.Serialize(parameters),
                TagsJson = request.Tags != null ? JsonSerializer.Serialize(request.Tags) : null,
                CreatedAt = now,
                UpdatedAt = now,
                CreatedBy = userId,
                UsageCount = 0,
                IsPublic = request.IsPublic,
                IsActive = true
            };

            await _templateRepository.CreateTemplateAsync(template);

            _logger.LogInformation("Created template {TemplateId} from workflow {WorkflowId}",
                templateId, request.WorkflowId);

            return templateId;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create template from workflow {WorkflowId}", request.WorkflowId);
            throw;
        }
    }

    public async Task<WorkflowTemplateDto?> GetTemplateByIdAsync(Guid templateId, string userId)
    {
        try
        {
            var template = await _templateRepository.GetTemplateByIdAsync(templateId, userId);
            if (template == null)
            {
                return null;
            }

            // Parse parameters from JSON
            var parameters = new Dictionary<string, ParameterDefinition>();
            if (!string.IsNullOrEmpty(template.ParametersJson))
            {
                try
                {
                    parameters = JsonSerializer.Deserialize<Dictionary<string, ParameterDefinition>>(template.ParametersJson) ?? new Dictionary<string, ParameterDefinition>();
                }
                catch (JsonException ex)
                {
                    _logger.LogWarning(ex, "Failed to parse parameters JSON for template {TemplateId}", templateId);
                }
            }

            // Parse tags from JSON
            var tags = new List<string>();
            if (!string.IsNullOrEmpty(template.TagsJson))
            {
                try
                {
                    tags = JsonSerializer.Deserialize<List<string>>(template.TagsJson) ?? new List<string>();
                }
                catch (JsonException ex)
                {
                    _logger.LogWarning(ex, "Failed to parse tags JSON for template {TemplateId}", templateId);
                }
            }

            return new WorkflowTemplateDto(
                template.TemplateId,
                template.Name,
                template.Description,
                template.Category,
                template.TemplateDefinitionJson,
                parameters,
                tags,
                template.CreatedAt,
                template.CreatedBy,
                template.UsageCount,
                template.IsPublic
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get template {TemplateId}", templateId);
            return null;
        }
    }

    public async Task<bool> UpdateTemplateAsync(Guid templateId, WorkflowTemplateDto template, string userId)
    {
        try
        {
            _logger.LogInformation("Updating template {TemplateId} for user {UserId}", templateId, userId);

            // Validate template definition
            var validation = await _dslParser.ValidateDSLAsync(template.TemplateDefinition);
            if (!validation.IsValid)
            {
                throw new ArgumentException($"Invalid template definition: {string.Join(", ", validation.Errors.Select(e => e.Message))}");
            }

            // Get existing template to ensure user has permission to update
            var existingTemplate = await _templateRepository.GetTemplateByIdAsync(templateId, userId);
            if (existingTemplate == null)
            {
                _logger.LogWarning("Template {TemplateId} not found or user {UserId} does not have permission to update", templateId, userId);
                return false;
            }

            // Create updated template model
            var updatedTemplate = new WorkflowTemplate
            {
                TemplateId = templateId,
                UserId = existingTemplate.UserId,
                Name = template.Name,
                Description = template.Description,
                Category = template.Category,
                TemplateDefinitionJson = template.TemplateDefinition,
                ParametersJson = JsonSerializer.Serialize(template.Parameters),
                TagsJson = JsonSerializer.Serialize(template.Tags),
                CreatedAt = existingTemplate.CreatedAt,
                UpdatedAt = DateTime.UtcNow,
                CreatedBy = existingTemplate.CreatedBy,
                UsageCount = existingTemplate.UsageCount,
                IsPublic = template.IsPublic,
                IsActive = existingTemplate.IsActive
            };

            return await _templateRepository.UpdateTemplateAsync(updatedTemplate);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update template {TemplateId}", templateId);
            return false;
        }
    }

    public async Task<bool> DeleteTemplateAsync(Guid templateId, string userId)
    {
        try
        {
            _logger.LogInformation("Deleting template {TemplateId} for user {UserId}", templateId, userId);

            // Verify template exists and user has permission to delete
            var template = await _templateRepository.GetTemplateByIdAsync(templateId, userId);
            if (template == null)
            {
                _logger.LogWarning("Template {TemplateId} not found or user {UserId} does not have permission to delete", templateId, userId);
                return false;
            }

            // Only allow deletion if user is the owner
            if (template.UserId != userId)
            {
                _logger.LogWarning("User {UserId} does not have permission to delete template {TemplateId} owned by {OwnerId}", userId, templateId, template.UserId);
                return false;
            }

            return await _templateRepository.DeleteTemplateAsync(templateId, userId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete template {TemplateId}", templateId);
            return false;
        }
    }

    public async Task<PaginatedResult<WorkflowTemplateDto>> SearchTemplatesAsync(
        string? query, string? category, List<string>? tags, bool includePublic, string userId,
        int page = 1, int pageSize = 20)
    {
        try
        {
            _logger.LogDebug("Searching templates for user {UserId} with query '{Query}'", userId, query);

            // Get templates from database with real persistence
            var searchResult = await _templateRepository.SearchTemplatesAsync(query, category, tags, includePublic, userId, page, pageSize);

            // Convert WorkflowTemplate models to DTOs
            var templateDtos = new List<WorkflowTemplateDto>();
            foreach (var template in searchResult.Items)
            {
                // Parse parameters from JSON
                var parameters = new Dictionary<string, ParameterDefinition>();
                if (!string.IsNullOrEmpty(template.ParametersJson))
                {
                    try
                    {
                        parameters = JsonSerializer.Deserialize<Dictionary<string, ParameterDefinition>>(template.ParametersJson) ?? new Dictionary<string, ParameterDefinition>();
                    }
                    catch (JsonException ex)
                    {
                        _logger.LogWarning(ex, "Failed to parse parameters JSON for template {TemplateId}", template.TemplateId);
                    }
                }

                // Parse tags from JSON
                var templateTags = new List<string>();
                if (!string.IsNullOrEmpty(template.TagsJson))
                {
                    try
                    {
                        templateTags = JsonSerializer.Deserialize<List<string>>(template.TagsJson) ?? new List<string>();
                    }
                    catch (JsonException ex)
                    {
                        _logger.LogWarning(ex, "Failed to parse tags JSON for template {TemplateId}", template.TemplateId);
                    }
                }

                templateDtos.Add(new WorkflowTemplateDto(
                    template.TemplateId,
                    template.Name,
                    template.Description,
                    template.Category,
                    template.TemplateDefinitionJson,
                    parameters,
                    templateTags,
                    template.CreatedAt,
                    template.CreatedBy,
                    template.UsageCount,
                    template.IsPublic
                ));
            }

            return new PaginatedResult<WorkflowTemplateDto>(
                templateDtos,
                searchResult.TotalCount,
                searchResult.Page,
                searchResult.PageSize,
                searchResult.TotalPages
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to search templates");
            return new PaginatedResult<WorkflowTemplateDto>(
                new List<WorkflowTemplateDto>(),
                0,
                page,
                pageSize,
                0
            );
        }
    }

    public async Task<List<WorkflowTemplateDto>> GetTemplatesByCategoryAsync(string category, bool includePublic, string userId)
    {
        try
        {
            var searchResult = await SearchTemplatesAsync(null, category, null, includePublic, userId, 1, 100);
            return searchResult.Items;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get templates by category {Category}", category);
            return new List<WorkflowTemplateDto>();
        }
    }

    public async Task<List<WorkflowTemplateDto>> GetPopularTemplatesAsync(int limit = 10, bool includePublic = true)
    {
        try
        {
            _logger.LogDebug("Getting popular templates (limit: {Limit})", limit);

            // Get popular templates from database ordered by usage count
            var popularTemplates = await _templateRepository.GetPopularTemplatesAsync(limit, includePublic);

            // Convert WorkflowTemplate models to DTOs
            var templateDtos = new List<WorkflowTemplateDto>();
            foreach (var template in popularTemplates)
            {
                // Parse parameters from JSON
                var parameters = new Dictionary<string, ParameterDefinition>();
                if (!string.IsNullOrEmpty(template.ParametersJson))
                {
                    try
                    {
                        parameters = JsonSerializer.Deserialize<Dictionary<string, ParameterDefinition>>(template.ParametersJson) ?? new Dictionary<string, ParameterDefinition>();
                    }
                    catch (JsonException ex)
                    {
                        _logger.LogWarning(ex, "Failed to parse parameters JSON for template {TemplateId}", template.TemplateId);
                    }
                }

                // Parse tags from JSON
                var templateTags = new List<string>();
                if (!string.IsNullOrEmpty(template.TagsJson))
                {
                    try
                    {
                        templateTags = JsonSerializer.Deserialize<List<string>>(template.TagsJson) ?? new List<string>();
                    }
                    catch (JsonException ex)
                    {
                        _logger.LogWarning(ex, "Failed to parse tags JSON for template {TemplateId}", template.TemplateId);
                    }
                }

                templateDtos.Add(new WorkflowTemplateDto(
                    template.TemplateId,
                    template.Name,
                    template.Description,
                    template.Category,
                    template.TemplateDefinitionJson,
                    parameters,
                    templateTags,
                    template.CreatedAt,
                    template.CreatedBy,
                    template.UsageCount,
                    template.IsPublic
                ));
            }

            return templateDtos;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get popular templates");
            return new List<WorkflowTemplateDto>();
        }
    }

    public async Task<Guid> CreateWorkflowFromTemplateAsync(Guid templateId, string workflowName,
        Dictionary<string, object>? parameters, string userId)
    {
        try
        {
            _logger.LogInformation("Creating workflow from template {TemplateId} for user {UserId}",
                templateId, userId);

            // Get template
            var template = await GetTemplateByIdAsync(templateId, userId);
            if (template == null)
            {
                throw new ArgumentException($"Template {templateId} not found");
            }

            // Validate parameters
            var validation = await ValidateTemplateParametersAsync(templateId, parameters ?? new Dictionary<string, object>(), userId);
            if (!validation.IsValid)
            {
                throw new ArgumentException($"Invalid parameters: {string.Join(", ", validation.Errors.Select(e => e.Message))}");
            }

            // Parse template definition
            var templateGraph = await _dslParser.ParseWorkflowAsync(template.TemplateDefinition);

            // Apply parameters to create workflow definition
            var workflowDefinition = ApplyParametersToTemplate(templateGraph, parameters ?? new Dictionary<string, object>());

            // Create workflow
            var createRequest = new CreateWorkflowRequest(
                workflowName,
                $"Workflow created from template: {template.Name}",
                workflowDefinition,
                template.Category,
                parameters,
                template.Tags
            );

            var workflowId = await _workflowRepository.CreateWorkflowAsync(createRequest, userId);

            // Increment template usage count with real database update
            await _templateRepository.IncrementTemplateUsageAsync(templateId);

            _logger.LogInformation("Created workflow {WorkflowId} from template {TemplateId}",
                workflowId, templateId);

            return workflowId;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create workflow from template {TemplateId}", templateId);
            throw;
        }
    }

    public async Task<WorkflowValidationResult> ValidateTemplateParametersAsync(Guid templateId,
        Dictionary<string, object> parameters, string userId)
    {
        try
        {
            var template = await GetTemplateByIdAsync(templateId, userId);
            if (template == null)
            {
                return new WorkflowValidationResult(
                    false,
                    new List<ValidationError>
                    {
                        new ValidationError("TEMPLATE_NOT_FOUND", $"Template {templateId} not found")
                    },
                    new List<ValidationWarning>()
                );
            }

            var result = new WorkflowValidationResult(
                true,
                new List<ValidationError>(),
                new List<ValidationWarning>()
            );

            // Validate required parameters
            foreach (var paramDef in template.Parameters.Values.Where(p => p.Required))
            {
                if (!parameters.ContainsKey(paramDef.Name))
                {
                    result.Errors.Add(new ValidationError("MISSING_REQUIRED_PARAMETER", $"Required parameter '{paramDef.Name}' is missing"));
                }
            }

            // Validate parameter types and values
            foreach (var kvp in parameters)
            {
                if (template.Parameters.TryGetValue(kvp.Key, out var paramDef))
                {
                    if (!ValidateParameterValue(kvp.Value, paramDef))
                    {
                        result.Errors.Add(new ValidationError("INVALID_PARAMETER_VALUE", $"Invalid value for parameter '{kvp.Key}'"));
                    }
                }
                else
                {
                    result.Warnings.Add(new ValidationWarning(
                        "UNKNOWN_PARAMETER",
                        $"Parameter '{kvp.Key}' is not defined in template"
                    ));
                }
            }

            // Create new result with updated IsValid status
            result = new WorkflowValidationResult(
                !result.Errors.Any(),
                result.Errors,
                result.Warnings
            );
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to validate template parameters for template {TemplateId}", templateId);
            return new WorkflowValidationResult(
                false,
                new List<ValidationError>
                {
                    new ValidationError(
                        "VALIDATION_ERROR",
                        ex.Message
                    )
                },
                new List<ValidationWarning>()
            );
        }
    }

    public async Task<Dictionary<string, object>> GetTemplateUsageStatsAsync(Guid templateId, string userId)
    {
        try
        {
            // Get real usage statistics from the database
            return await _templateRepository.GetTemplateUsageStatsAsync(templateId, userId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get template usage stats for {TemplateId}", templateId);
            return new Dictionary<string, object>();
        }
    }

    public async Task<byte[]> ExportTemplateAsync(Guid templateId, string userId)
    {
        try
        {
            var template = await GetTemplateByIdAsync(templateId, userId);
            if (template == null)
            {
                throw new ArgumentException($"Template {templateId} not found");
            }

            var exportData = new
            {
                template.Name,
                template.Description,
                template.Category,
                template.TemplateDefinition,
                template.Parameters,
                template.Tags,
                ExportedAt = DateTime.UtcNow,
                ExportedBy = userId,
                Version = "1.0"
            };

            var json = JsonSerializer.Serialize(exportData, new JsonSerializerOptions
            {
                WriteIndented = true
            });

            return System.Text.Encoding.UTF8.GetBytes(json);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to export template {TemplateId}", templateId);
            throw;
        }
    }

    public async Task<Guid> ImportTemplateAsync(byte[] templateData, string userId)
    {
        try
        {
            var json = System.Text.Encoding.UTF8.GetString(templateData);
            var importData = JsonSerializer.Deserialize<Dictionary<string, object>>(json);

            if (importData == null)
            {
                throw new ArgumentException("Invalid template data");
            }

            // Extract template data from import
            var name = importData["Name"]?.ToString() ?? "Imported Template";
            var description = importData["Description"]?.ToString() ?? "Imported workflow template";
            var category = importData["Category"]?.ToString() ?? "General";
            var templateDefinition = importData["TemplateDefinition"]?.ToString() ?? string.Empty;
            var parametersJson = importData.ContainsKey("Parameters") ? JsonSerializer.Serialize(importData["Parameters"]) : "{}";
            var tagsJson = importData.ContainsKey("Tags") ? JsonSerializer.Serialize(importData["Tags"]) : null;

            // Validate template definition
            var validation = await _dslParser.ValidateDSLAsync(templateDefinition);

            if (!validation.IsValid)
            {
                throw new ArgumentException($"Invalid template definition: {string.Join(", ", validation.Errors.Select(e => e.Message))}");
            }

            // Create new template with real database persistence
            var templateId = Guid.NewGuid();
            var now = DateTime.UtcNow;

            var template = new WorkflowTemplate
            {
                TemplateId = templateId,
                UserId = userId,
                Name = name,
                Description = description,
                Category = category,
                TemplateDefinitionJson = templateDefinition,
                ParametersJson = parametersJson,
                TagsJson = tagsJson,
                CreatedAt = now,
                UpdatedAt = now,
                CreatedBy = userId,
                UsageCount = 0,
                IsPublic = false,
                IsActive = true
            };

            await _templateRepository.CreateTemplateAsync(template);

            _logger.LogInformation("Imported template {TemplateId} for user {UserId}", templateId, userId);
            return templateId;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to import template");
            throw;
        }
    }

    private Dictionary<string, ParameterDefinition> ExtractParametersFromWorkflow(DSL.WorkflowGraph workflowGraph)
    {
        var parameters = new Dictionary<string, ParameterDefinition>();

        // Extract parameters from workflow definition
        foreach (var kvp in workflowGraph.Parameters)
        {
            parameters[kvp.Key] = new ParameterDefinition(
                kvp.Key,
                InferParameterType(kvp.Value),
                $"Parameter {kvp.Key}",
                false,
                kvp.Value,
                null,
                null
            );
        }

        // Extract parameters from node configurations
        foreach (var node in workflowGraph.Nodes.Values)
        {
            foreach (var configKvp in node.Configuration)
            {
                var paramName = $"{node.Id}_{configKvp.Key}";
                if (!parameters.ContainsKey(paramName))
                {
                    parameters[paramName] = new ParameterDefinition(
                        paramName,
                        InferParameterType(configKvp.Value),
                        $"Configuration parameter for {node.Name}",
                        false,
                        configKvp.Value,
                        null,
                        null
                    );
                }
            }
        }

        return parameters;
    }

    private string CreateTemplateDefinition(DSL.WorkflowGraph workflowGraph, Dictionary<string, ParameterDefinition> parameters)
    {
        // Convert workflow to template format with parameter placeholders
        var templateGraph = new
        {
            name = "{{name}}",
            description = workflowGraph.Description,
            version = workflowGraph.Version,
            parameters = parameters.Keys.ToList(),
            nodes = workflowGraph.Nodes.ToDictionary(
                kvp => kvp.Key,
                kvp => new
                {
                    name = kvp.Value.Name,
                    type = kvp.Value.Type,
                    configuration = ConvertConfigurationToTemplate(kvp.Value.Configuration),
                    dependencies = kvp.Value.Dependencies
                }
            ),
            edges = workflowGraph.Edges
        };

        return JsonSerializer.Serialize(templateGraph, new JsonSerializerOptions
        {
            WriteIndented = true
        });
    }

    private Dictionary<string, object> ConvertConfigurationToTemplate(Dictionary<string, object> configuration)
    {
        var templateConfig = new Dictionary<string, object>();

        foreach (var kvp in configuration)
        {
            // Convert values to parameter references
            templateConfig[kvp.Key] = $"{{{{{kvp.Key}}}}}";
        }

        return templateConfig;
    }

    private string ApplyParametersToTemplate(DSL.WorkflowGraph templateGraph, Dictionary<string, object> parameters)
    {
        var workflowJson = JsonSerializer.Serialize(templateGraph);

        // Replace parameter placeholders with actual values
        foreach (var kvp in parameters)
        {
            var placeholder = $"{{{{{kvp.Key}}}}}";
            var value = JsonSerializer.Serialize(kvp.Value);
            workflowJson = workflowJson.Replace(placeholder, value.Trim('"'));
        }

        return workflowJson;
    }

    private string InferParameterType(object value)
    {
        return value switch
        {
            string => "string",
            int or long => "integer",
            double or float => "number",
            bool => "boolean",
            DateTime => "datetime",
            _ => "object"
        };
    }

    private bool ValidateParameterValue(object value, ParameterDefinition paramDef)
    {
        // Basic type validation
        var expectedType = paramDef.Type.ToLowerInvariant();

        try
        {
            switch (expectedType)
            {
                case "string":
                    return value is string;
                case "integer":
                    return value is int or long || (value is string s && long.TryParse(s, out _));
                case "number":
                    return value is double or float or int or long || (value is string numStr && double.TryParse(numStr, out _));
                case "boolean":
                    return value is bool || (value is string boolStr && bool.TryParse(boolStr, out _));
                case "datetime":
                    return value is DateTime || (value is string dateStr && DateTime.TryParse(dateStr, out _));
                default:
                    return true; // Allow any type for unknown types
            }
        }
        catch
        {
            return false;
        }
    }
}