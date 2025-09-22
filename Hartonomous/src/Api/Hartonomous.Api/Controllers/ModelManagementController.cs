using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Hartonomous.ModelService.Services;
using Hartonomous.Api.Hubs;

namespace Hartonomous.Api.Controllers;

/// <summary>
/// API controller for model management and ingestion
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ModelManagementController : ControllerBase
{
    private readonly OllamaDiscoveryService _discoveryService;
    private readonly ModelIngestionService _ingestionService;
    private readonly IngestionProgressService _progressService;
    private readonly IHubContext<ModelIngestionHub> _hubContext;
    private readonly ILogger<ModelManagementController> _logger;

    public ModelManagementController(
        OllamaDiscoveryService discoveryService,
        ModelIngestionService ingestionService,
        IngestionProgressService progressService,
        IHubContext<ModelIngestionHub> hubContext,
        ILogger<ModelManagementController> logger)
    {
        _discoveryService = discoveryService;
        _ingestionService = ingestionService;
        _progressService = progressService;
        _hubContext = hubContext;
        _logger = logger;
    }

    /// <summary>
    /// Discover available Ollama models
    /// </summary>
    [HttpGet("discover")]
    public async Task<ActionResult<List<DiscoveredModel>>> DiscoverModels()
    {
        try
        {
            var models = await _discoveryService.DiscoverModelsAsync();
            return Ok(models);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error discovering models");
            return StatusCode(500, new { error = "Failed to discover models", details = ex.Message });
        }
    }

    /// <summary>
    /// Get detailed information about a specific model
    /// </summary>
    [HttpGet("{modelName}/details")]
    public async Task<ActionResult<ModelDetails>> GetModelDetails(string modelName)
    {
        try
        {
            var details = await _discoveryService.GetModelDetailsAsync(modelName);
            if (details == null)
            {
                return NotFound(new { error = "Model not found" });
            }

            return Ok(details);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting model details for {ModelName}", modelName);
            return StatusCode(500, new { error = "Failed to get model details", details = ex.Message });
        }
    }

    /// <summary>
    /// Validate a model file before ingestion
    /// </summary>
    [HttpPost("validate")]
    public async Task<ActionResult<ValidationResult>> ValidateModel([FromBody] ValidateModelRequest request)
    {
        try
        {
            var result = await _discoveryService.ValidateModelAsync(request.FilePath);
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating model at {FilePath}", request.FilePath);
            return StatusCode(500, new { error = "Failed to validate model", details = ex.Message });
        }
    }

    /// <summary>
    /// Start model ingestion process
    /// </summary>
    [HttpPost("ingest")]
    public async Task<ActionResult<IngestionStartResponse>> IngestModel([FromBody] IngestModelRequest request)
    {
        try
        {
            var userId = GetUserId();

            // Validate model first
            var validation = await _discoveryService.ValidateModelAsync(request.FilePath);
            if (!validation.IsValid)
            {
                return BadRequest(new { error = "Model validation failed", details = validation.ErrorMessage });
            }

            // Start ingestion in background
            var modelId = Guid.NewGuid();
            _progressService.StartIngestion(modelId, request.ModelName, userId);

            // Wire up progress notifications
            _progressService.ProgressUpdated += async (sender, args) =>
            {
                if (args.Progress.ModelId == modelId)
                {
                    await _hubContext.Clients.Group($"user_{userId}")
                        .SendAsync("IngestionProgress", args.Progress);
                }
            };

            // Start ingestion asynchronously
            _ = Task.Run(async () =>
            {
                try
                {
                    _progressService.UpdateProgress(modelId, IngestionStage.Validation, 100, "Validation completed");

                    var result = await _ingestionService.IngestAsync(request.FilePath, request.ModelName, userId);

                    if (result.Success)
                    {
                        _progressService.CompleteIngestion(modelId, result.ComponentCount, "Ingestion completed successfully");
                    }
                    else
                    {
                        _progressService.FailIngestion(modelId, result.ErrorMessage ?? "Unknown error");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Ingestion failed for model {ModelName}", request.ModelName);
                    _progressService.FailIngestion(modelId, ex.Message);
                }
            });

            return Ok(new IngestionStartResponse
            {
                ModelId = modelId,
                Message = "Ingestion started successfully"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error starting ingestion for {ModelName}", request.ModelName);
            return StatusCode(500, new { error = "Failed to start ingestion", details = ex.Message });
        }
    }

    /// <summary>
    /// Get ingestion progress for a specific model
    /// </summary>
    [HttpGet("{modelId:guid}/progress")]
    public ActionResult<IngestionProgress> GetIngestionProgress(Guid modelId)
    {
        var progress = _progressService.GetProgress(modelId);
        if (progress == null)
        {
            return NotFound(new { error = "Ingestion not found" });
        }

        return Ok(progress);
    }

    /// <summary>
    /// Get all active ingestions for the current user
    /// </summary>
    [HttpGet("ingestions")]
    public ActionResult<List<IngestionProgress>> GetActiveIngestions()
    {
        var userId = GetUserId();
        var ingestions = _progressService.GetActiveIngestions(userId);
        return Ok(ingestions);
    }

    private string GetUserId()
    {
        return User?.Identity?.Name ?? "anonymous";
    }
}

public class ValidateModelRequest
{
    public string FilePath { get; set; } = string.Empty;
}

public class IngestModelRequest
{
    public string FilePath { get; set; } = string.Empty;
    public string ModelName { get; set; } = string.Empty;
}

public class IngestionStartResponse
{
    public Guid ModelId { get; set; }
    public string Message { get; set; } = string.Empty;
}