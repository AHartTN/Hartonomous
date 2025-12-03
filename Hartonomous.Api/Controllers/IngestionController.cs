using Hartonomous.Shared.Interfaces;
using Hartonomous.Shared.Models;
using Microsoft.AspNetCore.Mvc;

namespace Hartonomous.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class IngestionController : ControllerBase
{
    private readonly IIngestionService _ingestionService;

    public IngestionController(IIngestionService ingestionService)
    {
        _ingestionService = ingestionService;
    }

    [HttpPost("jobs")]
    public async Task<ActionResult<IngestionJobDto>> CreateJob([FromBody] CreateIngestionJobRequest request)
    {
        var job = await _ingestionService.CreateJobAsync(request);
        return CreatedAtAction(nameof(GetJob), new { id = job.Id }, job);
    }

    [HttpGet("jobs/{id}")]
    public async Task<ActionResult<IngestionJobDto>> GetJob(long id)
    {
        var job = await _ingestionService.GetJobByIdAsync(id);
        return job == null ? NotFound() : Ok(job);
    }

    [HttpGet("jobs/active")]
    public async Task<ActionResult<List<IngestionJobDto>>> GetActiveJobs()
    {
        var jobs = await _ingestionService.GetActiveJobsAsync();
        return Ok(jobs);
    }

    [HttpPut("jobs/{id}/status")]
    public async Task<ActionResult> UpdateJobStatus(long id, [FromBody] IngestionJobStatusUpdate update)
    {
        update.JobId = id;
        await _ingestionService.UpdateJobStatusAsync(update);
        return NoContent();
    }

    [HttpPost("jobs/{id}/cancel")]
    public async Task<ActionResult> CancelJob(long id)
    {
        var result = await _ingestionService.CancelJobAsync(id);
        return result ? NoContent() : NotFound();
    }
}
