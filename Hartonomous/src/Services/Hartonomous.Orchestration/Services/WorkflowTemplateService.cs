using Hartonomous.Orchestration.DTOs;
using Hartonomous.Orchestration.Interfaces;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace Hartonomous.Orchestration.Services;

/// <summary>
/// Workflow template service implementation
/// </summary>
public class WorkflowTemplateService : IWorkflowTemplateService
{
    private readonly IWorkflowRepository _workflowRepository;
    private readonly IWorkflowDSLParser _dslParser;
    private readonly ILogger<WorkflowTemplateService> _logger;

    public WorkflowTemplateService(
        IWorkflowRepository workflowRepository,
        IWorkflowDSLParser dslParser,
        ILogger<WorkflowTemplateService> logger)
    {
        _workflowRepository = workflowRepository;
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

            // Create template record (simulated - in real implementation this would go to database)
            var templateId = Guid.NewGuid();

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
            // In a real implementation, this would query the database
            // For now, return a sample template
            return new WorkflowTemplateDto(
                templateId,
                "Sample Template",
                "A sample workflow template",
                "General",
                JsonSerializer.Serialize(new { name = "sample", nodes = new { } }),
                new Dictionary<string, ParameterDefinition>
                {
                    ["input"] = new ParameterDefinition(
                        "input",
                        "string",
                        "Input data for the workflow",
                        true,
                        null,
                        null,
                        null
                    )
                },
                new List<string> { "sample", "demo" },
                DateTime.UtcNow.AddDays(-30),
                userId,
                5,
                false
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

            // In a real implementation, this would update the database
            return true;
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

            // In a real implementation, this would delete from database
            return true;
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

            // In a real implementation, this would query the database
            var sampleTemplates = new List<WorkflowTemplateDto>
            {
                new WorkflowTemplateDto(
                    Guid.NewGuid(),
                    "Data Processing Template",
                    "Template for data processing workflows",
                    "Data",
                    JsonSerializer.Serialize(new { name = "data-processing" }),
                    new Dictionary<string, ParameterDefinition>(),
                    new List<string> { "data", "processing" },
                    DateTime.UtcNow.AddDays(-15),
                    userId,
                    10,
                    false
                ),
                new WorkflowTemplateDto(
                    Guid.NewGuid(),
                    "Notification Template",
                    "Template for notification workflows",
                    "Communication",
                    JsonSerializer.Serialize(new { name = "notification" }),
                    new Dictionary<string, ParameterDefinition>(),
                    new List<string> { "notification", "email" },
                    DateTime.UtcNow.AddDays(-7),
                    userId,
                    3,
                    true
                )
            };

            // Apply filters
            if (!string.IsNullOrEmpty(query))
            {
                sampleTemplates = sampleTemplates
                    .Where(t => t.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                               t.Description.Contains(query, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }

            if (!string.IsNullOrEmpty(category))
            {
                sampleTemplates = sampleTemplates
                    .Where(t => t.Category.Equals(category, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }

            if (tags?.Any() == true)
            {
                sampleTemplates = sampleTemplates
                    .Where(t => tags.Any(tag => t.Tags.Contains(tag, StringComparer.OrdinalIgnoreCase)))
                    .ToList();
            }

            // Pagination
            var totalCount = sampleTemplates.Count;
            var totalPages = (int)Math.Ceiling((double)totalCount / pageSize);
            var pagedTemplates = sampleTemplates
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToList();

            return new PaginatedResult<WorkflowTemplateDto>(
                pagedTemplates,
                totalCount,
                page,
                pageSize,
                totalPages
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

            // In a real implementation, this would query the database ordered by usage count
            var popularTemplates = new List<WorkflowTemplateDto>
            {
                new WorkflowTemplateDto(
                    Guid.NewGuid(),
                    "CI/CD Pipeline",
                    "Continuous integration and deployment pipeline",
                    "DevOps",
                    JsonSerializer.Serialize(new { name = "cicd-pipeline" }),
                    new Dictionary<string, ParameterDefinition>(),
                    new List<string> { "cicd", "devops", "pipeline" },
                    DateTime.UtcNow.AddDays(-30),
                    "system",
                    50,
                    true
                ),
                new WorkflowTemplateDto(
                    Guid.NewGuid(),
                    "ETL Process",
                    "Extract, Transform, Load data processing workflow",
                    "Data",
                    JsonSerializer.Serialize(new { name = "etl-process" }),
                    new Dictionary<string, ParameterDefinition>(),
                    new List<string> { "etl", "data", "processing" },
                    DateTime.UtcNow.AddDays(-20),
                    "system",
                    35,
                    true
                )
            };

            return popularTemplates.OrderByDescending(t => t.UsageCount).Take(limit).ToList();
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

            // Update template usage count (in a real implementation)
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
            // In a real implementation, this would query usage statistics from the database
            return new Dictionary<string, object>
            {
                ["templateId"] = templateId,
                ["totalUsage"] = 25,
                ["lastUsed"] = DateTime.UtcNow.AddDays(-2),
                ["averageExecutionTime"] = 45.6,
                ["successRate"] = 0.92,
                ["popularParameters"] = new List<string> { "input", "output", "environment" }
            };
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

            // Validate template definition
            var templateDefinition = importData["TemplateDefinition"].ToString() ?? string.Empty;
            var validation = await _dslParser.ValidateDSLAsync(templateDefinition);

            if (!validation.IsValid)
            {
                throw new ArgumentException($"Invalid template definition: {string.Join(", ", validation.Errors.Select(e => e.Message))}");
            }

            // Create new template (in a real implementation, this would save to database)
            var templateId = Guid.NewGuid();

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