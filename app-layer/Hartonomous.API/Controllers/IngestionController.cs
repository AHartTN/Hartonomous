using Hartonomous.Core.Services;
using Microsoft.AspNetCore.Mvc;

namespace Hartonomous.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class IngestionController : ControllerBase
{
    private readonly IngestionService _ingestion;
    private readonly ILogger<IngestionController> _logger;

    public IngestionController(IngestionService ingestion, ILogger<IngestionController> logger)
    {
        _ingestion = ingestion;
        _logger = logger;
    }

    [HttpPost("text")]
    public IActionResult IngestText([FromBody] IngestTextRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Text))
            return BadRequest("Text is required");

        try
        {
            var stats = _ingestion.IngestText(request.Text);
            return Ok(stats);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Text ingestion failed");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    [HttpPost("file")]
    public IActionResult IngestFile([FromBody] IngestFileRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.FilePath))
            return BadRequest("FilePath is required");

        try
        {
            var stats = _ingestion.IngestFile(request.FilePath);
            return Ok(stats);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "File ingestion failed");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    public sealed class IngestTextRequest
    {
        public string Text { get; set; } = string.Empty;
    }

    public sealed class IngestFileRequest
    {
        public string FilePath { get; set; } = string.Empty;
    }
}
