using Hartonomous.Shared.Models;
using Microsoft.AspNetCore.Mvc;

namespace Hartonomous.API.Controllers;

/// <summary>
/// OpenAI-compatible /v1/models endpoint.
/// </summary>
[ApiController]
[Route("v1")]
public class ModelsController : ControllerBase
{
    [HttpGet("models")]
    public IActionResult ListModels()
    {
        var response = new ModelListResponse
        {
            Data =
            [
                new ModelInfo
                {
                    Id = "hartonomous-walk-v1",
                    Created = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds(),
                },
                new ModelInfo
                {
                    Id = "hartonomous-s3-v1",
                    Created = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds(),
                },
            ],
        };

        return Ok(response);
    }

    [HttpGet("models/{modelId}")]
    public IActionResult GetModel(string modelId)
    {
        if (modelId is "hartonomous-walk-v1" or "hartonomous-s3-v1")
        {
            return Ok(new ModelInfo
            {
                Id = modelId,
                Created = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds(),
            });
        }
        return NotFound(new { error = new { message = $"Model '{modelId}' not found", type = "invalid_request_error" } });
    }
}
