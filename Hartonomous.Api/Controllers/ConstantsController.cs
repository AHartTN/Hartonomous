using Hartonomous.Core.Application.Commands.ContentIngestion;
using Hartonomous.Core.Application.Common;
using Hartonomous.Core.Application.Queries.Constants;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Hartonomous.API.Controllers;

/// <summary>
/// API endpoints for managing content-addressable constants.
/// Provides ingestion, retrieval, and spatial query capabilities.
/// </summary>
[ApiController]
[Route("api/v1/[controller]")]
[Authorize]
[EnableRateLimiting("fixed")]
[Produces("application/json")]
public sealed class ConstantsController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ILogger<ConstantsController> _logger;

    public ConstantsController(IMediator mediator, ILogger<ConstantsController> logger)
    {
        _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Ingest content and decompose into constants.
    /// </summary>
    /// <param name="request">Content ingestion request</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Ingestion result with statistics</returns>
    [HttpPost("ingest")]
    [Authorize(Policy = "WritePolicy")]
    [ProducesResponseType(typeof(IngestContentCommandResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<IngestContentCommandResult>> IngestContent(
        [FromBody] IngestContentCommand request,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Ingesting content with {ByteCount} bytes",
            request.ContentData.Length);

        var result = await _mediator.Send(request, cancellationToken);

        if (!result.IsSuccess)
        {
            return BadRequest(new { error = result.Error });
        }

        return Ok(result.Value);
    }

    /// <summary>
    /// Retrieve constant by SHA-256 hash.
    /// </summary>
    /// <param name="hash">64-character hexadecimal SHA-256 hash</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Constant details</returns>
    [HttpGet("{hash}")]
    [ProducesResponseType(typeof(ConstantDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ResponseCache(Duration = 300, VaryByQueryKeys = new[] { "hash" })]
    public async Task<ActionResult<ConstantDto>> GetConstantByHash(
        string hash,
        CancellationToken cancellationToken)
    {
        var query = new GetConstantByHashQuery { Hash = hash };
        var result = await _mediator.Send(query, cancellationToken);

        if (!result.IsSuccess)
        {
            return NotFound(new { error = result.Error });
        }

        return Ok(result.Value);
    }

    /// <summary>
    /// Find constants near a spatial coordinate.
    /// </summary>
    /// <param name="x">X coordinate (-1.0 to 1.0)</param>
    /// <param name="y">Y coordinate (-1.0 to 1.0)</param>
    /// <param name="z">Z coordinate (-1.0 to 1.0)</param>
    /// <param name="radius">Search radius</param>
    /// <param name="limit">Maximum results (default 100, max 1000)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of nearby constants</returns>
    [HttpGet("nearby")]
    [ProducesResponseType(typeof(IReadOnlyList<ConstantDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ResponseCache(Duration = 60, VaryByQueryKeys = new[] { "x", "y", "z", "radius", "limit" })]
    public async Task<ActionResult<IReadOnlyList<ConstantDto>>> GetNearbyConstants(
        [FromQuery] double x,
        [FromQuery] double y,
        [FromQuery] double z,
        [FromQuery] double radius = 0.1,
        [FromQuery] int limit = 100,
        CancellationToken cancellationToken = default)
    {
        if (limit > 1000) limit = 1000;

        var query = new GetConstantsNearCoordinateQuery
        {
            X = x,
            Y = y,
            Z = z,
            Radius = radius,
            Limit = limit
        };

        var result = await _mediator.Send(query, cancellationToken);

        if (!result.IsSuccess)
        {
            return BadRequest(new { error = result.Error });
        }

        return Ok(result.Value);
    }

    /// <summary>
    /// Get constants by status.
    /// </summary>
    /// <param name="status">Constant status (0=Created, 1=Projected, 2=Active, 3=Archived)</param>
    /// <param name="page">Page number (1-based)</param>
    /// <param name="pageSize">Page size (max 100)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Paginated list of constants</returns>
    [HttpGet("by-status/{status}")]
    [ProducesResponseType(typeof(PaginatedResult<ConstantDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ResponseCache(Duration = 30, VaryByQueryKeys = new[] { "status", "page", "pageSize" })]
    public async Task<ActionResult<PaginatedResult<ConstantDto>>> GetConstantsByStatus(
        int status,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        if (pageSize > 100) pageSize = 100;
        if (page < 1) page = 1;

        var query = new GetConstantsByStatusQuery
        {
            Status = status,
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
    /// Find k-nearest neighbors to a target constant.
    /// </summary>
    /// <param name="hash">Target constant hash</param>
    /// <param name="k">Number of neighbors (max 100)</param>
    /// <param name="useGpu">Use GPU acceleration if available</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of nearest constants with distances</returns>
    [HttpGet("{hash}/neighbors")]
    [ProducesResponseType(typeof(IReadOnlyList<NearestConstantDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ResponseCache(Duration = 120, VaryByQueryKeys = new[] { "hash", "k", "useGpu" })]
    public async Task<ActionResult<IReadOnlyList<NearestConstantDto>>> GetNearestNeighbors(
        string hash,
        [FromQuery] int k = 10,
        [FromQuery] bool useGpu = true,
        CancellationToken cancellationToken = default)
    {
        if (k > 100) k = 100;
        if (k < 1) k = 1;

        var query = new FindNearestNeighborsQuery
        {
            Hash = hash,
            K = k,
            UseGpu = useGpu
        };

        var result = await _mediator.Send(query, cancellationToken);

        if (!result.IsSuccess)
        {
            return NotFound(new { error = result.Error });
        }

        return Ok(result.Value);
    }

    /// <summary>
    /// Get constant statistics.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>System-wide constant statistics</returns>
    [HttpGet("statistics")]
    [ProducesResponseType(typeof(ConstantStatisticsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ResponseCache(Duration = 60)]
    public async Task<ActionResult<ConstantStatisticsDto>> GetStatistics(
        CancellationToken cancellationToken)
    {
        var query = new GetConstantStatisticsQuery();
        var result = await _mediator.Send(query, cancellationToken);

        if (!result.IsSuccess)
        {
            return BadRequest(new { error = result.Error });
        }

        return Ok(result.Value);
    }
}
