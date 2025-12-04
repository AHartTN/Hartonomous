using Hartonomous.Core.Application.Common;
using Hartonomous.Core.Application.Queries.Constants;
using Hartonomous.Core.Application.Queries.ContentIngestion;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Hartonomous.API.Controllers;

/// <summary>
/// API endpoints for content ingestion management and monitoring.
/// Provides ingestion history, statistics, and analytics.
/// </summary>
[ApiController]
[Route("api/v1/[controller]")]
[Authorize]
[EnableRateLimiting("fixed")]
[Produces("application/json")]
public sealed class IngestionsController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ILogger<IngestionsController> _logger;

    public IngestionsController(IMediator mediator, ILogger<IngestionsController> logger)
    {
        _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Get ingestion by ID.
    /// </summary>
    /// <param name="id">Ingestion ID</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Ingestion details</returns>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(ContentIngestionDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ResponseCache(Duration = 300, VaryByQueryKeys = new[] { "id" })]
    public async Task<ActionResult<ContentIngestionDto>> GetIngestionById(
        Guid id,
        CancellationToken cancellationToken)
    {
        var query = new GetContentIngestionByIdQuery { IngestionId = id };
        var result = await _mediator.Send(query, cancellationToken);

        if (!result.IsSuccess)
        {
            return NotFound(new { error = result.Error });
        }

        return Ok(result.Value);
    }

    /// <summary>
    /// Get recent ingestions (paginated).
    /// </summary>
    /// <param name="page">Page number (1-based)</param>
    /// <param name="pageSize">Page size (max 100)</param>
    /// <param name="successOnly">Return only successful ingestions</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Paginated list of recent ingestions</returns>
    [HttpGet]
    [ProducesResponseType(typeof(PaginatedResult<ContentIngestionDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ResponseCache(Duration = 30, VaryByQueryKeys = new[] { "page", "pageSize", "successOnly" })]
    public async Task<ActionResult<PaginatedResult<ContentIngestionDto>>> GetRecentIngestions(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        [FromQuery] bool successOnly = false,
        CancellationToken cancellationToken = default)
    {
        if (pageSize > 100) pageSize = 100;
        if (page < 1) page = 1;

        var query = new GetRecentIngestionsQuery
        {
            Page = page,
            PageSize = pageSize,
            SuccessOnly = successOnly
        };

        var result = await _mediator.Send(query, cancellationToken);

        if (!result.IsSuccess)
        {
            return BadRequest(new { error = result.Error });
        }

        return Ok(result.Value);
    }

    /// <summary>
    /// Get ingestion statistics.
    /// </summary>
    /// <param name="timeRange">Time range: hour, day, week, month, all (default: day)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Aggregated ingestion statistics</returns>
    [HttpGet("statistics")]
    [ProducesResponseType(typeof(IngestionStatisticsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ResponseCache(Duration = 60, VaryByQueryKeys = new[] { "timeRange" })]
    public async Task<ActionResult<IngestionStatisticsDto>> GetStatistics(
        [FromQuery] string timeRange = "day",
        CancellationToken cancellationToken = default)
    {
        var query = new GetIngestionStatisticsQuery { TimeRange = timeRange };
        var result = await _mediator.Send(query, cancellationToken);

        if (!result.IsSuccess)
        {
            return BadRequest(new { error = result.Error });
        }

        return Ok(result.Value);
    }

    /// <summary>
    /// Get ingestion performance metrics.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Performance metrics including throughput and deduplication ratios</returns>
    [HttpGet("metrics")]
    [ProducesResponseType(typeof(IngestionMetricsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ResponseCache(Duration = 60)]
    public async Task<ActionResult<IngestionMetricsDto>> GetMetrics(
        CancellationToken cancellationToken)
    {
        var query = new GetIngestionMetricsQuery();
        var result = await _mediator.Send(query, cancellationToken);

        if (!result.IsSuccess)
        {
            return BadRequest(new { error = result.Error });
        }

        return Ok(result.Value);
    }

    /// <summary>
    /// Get ingestions by date range.
    /// </summary>
    /// <param name="startDate">Start date (ISO 8601)</param>
    /// <param name="endDate">End date (ISO 8601)</param>
    /// <param name="page">Page number</param>
    /// <param name="pageSize">Page size</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Paginated ingestions in date range</returns>
    [HttpGet("range")]
    [ProducesResponseType(typeof(PaginatedResult<ContentIngestionDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ResponseCache(Duration = 120, VaryByQueryKeys = new[] { "startDate", "endDate", "page", "pageSize" })]
    public async Task<ActionResult<PaginatedResult<ContentIngestionDto>>> GetIngestionsByDateRange(
        [FromQuery] DateTime startDate,
        [FromQuery] DateTime endDate,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        if (endDate <= startDate)
        {
            return BadRequest(new { error = "End date must be after start date" });
        }

        if (pageSize > 100) pageSize = 100;
        if (page < 1) page = 1;

        var query = new GetIngestionsByDateRangeQuery
        {
            StartDate = startDate,
            EndDate = endDate,
            Page = page,
            PageSize = pageSize
        };

        var result = await _mediator.Send(query, cancellationToken);

        if (!result.IsSuccess)
        {
            return BadRequest(new { error = result.Error });
        }

        return Ok(result.Value);
    }

    /// <summary>
    /// Get failed ingestions for troubleshooting.
    /// </summary>
    /// <param name="page">Page number</param>
    /// <param name="pageSize">Page size</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Paginated failed ingestions</returns>
    [HttpGet("failures")]
    [Authorize(Policy = "AdminPolicy")]
    [ProducesResponseType(typeof(PaginatedResult<ContentIngestionDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ResponseCache(Duration = 30, VaryByQueryKeys = new[] { "page", "pageSize" })]
    public async Task<ActionResult<PaginatedResult<ContentIngestionDto>>> GetFailedIngestions(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        if (pageSize > 100) pageSize = 100;
        if (page < 1) page = 1;

        var query = new GetFailedIngestionsQuery
        {
            Page = page,
            PageSize = pageSize
        };

        var result = await _mediator.Send(query, cancellationToken);

        if (!result.IsSuccess)
        {
            return BadRequest(new { error = result.Error });
        }

        return Ok(result.Value);
    }

    /// <summary>
    /// Ingest an entire Git repository.
    /// Recursively processes all files and optionally learns BPE vocabulary.
    /// </summary>
    /// <param name="request">Repository ingestion request</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Repository ingestion results</returns>
    [HttpPost("repository")]
    [Authorize(Policy = "WritePolicy")]
    [ProducesResponseType(typeof(Hartonomous.Core.Application.Commands.ContentIngestion.IngestRepositoryResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<Hartonomous.Core.Application.Commands.ContentIngestion.IngestRepositoryResponse>> IngestRepository(
        [FromBody] Hartonomous.Core.Application.Commands.ContentIngestion.IngestRepositoryCommand request,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(request, cancellationToken);

        if (!result.IsSuccess)
        {
            return BadRequest(new { error = result.Error });
        }

        return Ok(result.Value);
    }
}
