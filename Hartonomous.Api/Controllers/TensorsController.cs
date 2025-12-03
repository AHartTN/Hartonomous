using Hartonomous.Shared.Interfaces;
using Hartonomous.Shared.Models;
using Microsoft.AspNetCore.Mvc;

namespace Hartonomous.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class TensorsController : ControllerBase
{
    private readonly ITensorService _tensorService;

    public TensorsController(ITensorService tensorService)
    {
        _tensorService = tensorService;
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<TensorChunkDto>> GetById(long id)
    {
        var chunk = await _tensorService.GetChunkByIdAsync(id);
        return chunk == null ? NotFound() : Ok(chunk);
    }

    [HttpGet("tensor/{tensorName}")]
    public async Task<ActionResult<List<TensorChunkDto>>> GetByTensorName(string tensorName, [FromQuery] int skip = 0, [FromQuery] int take = 100)
    {
        var chunks = await _tensorService.GetChunksByTensorNameAsync(tensorName, skip, take);
        return Ok(chunks);
    }

    [HttpPost]
    public async Task<ActionResult<TensorChunkDto>> Create([FromBody] CreateTensorChunkRequest request)
    {
        var chunk = await _tensorService.CreateChunkAsync(request);
        return CreatedAtAction(nameof(GetById), new { id = chunk.Id }, chunk);
    }

    [HttpPost("search")]
    public async Task<ActionResult<List<TensorChunkDto>>> SearchSimilar([FromBody] TensorSearchRequest request)
    {
        var chunks = await _tensorService.SearchSimilarChunksAsync(request);
        return Ok(chunks);
    }
}
