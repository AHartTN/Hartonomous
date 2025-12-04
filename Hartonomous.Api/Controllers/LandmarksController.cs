using Hartonomous.Core.Application.Commands.Landmarks;
using Hartonomous.Core.Application.Queries.Landmarks;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Hartonomous.API.Controllers;

/// <summary>
/// API endpoints for managing spatial landmarks.
/// Landmarks serve as reference points in 3D content space.
/// </summary>
[ApiController]
[Route("api/v1/[controller]")]
[Authorize]
[EnableRateLimiting("fixed")]
[Produces("application/json")]
public sealed class LandmarksController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ILogger<LandmarksController> _logger;

    public LandmarksController(IMediator mediator, ILogger<LandmarksController> logger)
    {
        _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Create a new spatial landmark.
    /// </summary>
    /// <param name="request">Landmark creation request</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Created landmark details</returns>
    [HttpPost]
    [Authorize(Policy = "WritePolicy")]
    [ProducesResponseType(typeof(CreateLandmarkCommandResult), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CreateLandmarkCommandResult>> CreateLandmark(
        [FromBody] CreateLandmarkCommand request,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Creating landmark '{Name}' at ({X:F3}, {Y:F3}, {Z:F3})",
            request.Name, request.CenterX, request.CenterY, request.CenterZ);

        var result = await _mediator.Send(request, cancellationToken);

        if (!result.IsSuccess)
        {
            if (result.Error?.Contains("already exists") == true)
            {
                return Conflict(new { error = result.Error });
            }
            return BadRequest(new { error = result.Error });
        }

        return CreatedAtAction(
            nameof(GetLandmarkByName),
            new { name = request.Name },
            result.Value);
    }

    /// <summary>
    /// Get landmark by name.
    /// </summary>
    /// <param name="name">Landmark name</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Landmark details</returns>
    [HttpGet("{name}")]
    [ProducesResponseType(typeof(LandmarkDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ResponseCache(Duration = 300, VaryByQueryKeys = new[] { "name" })]
    public async Task<ActionResult<LandmarkDto>> GetLandmarkByName(
        string name,
        CancellationToken cancellationToken)
    {
        var query = new GetLandmarkByNameQuery { Name = name };
        var result = await _mediator.Send(query, cancellationToken);

        if (!result.IsSuccess)
        {
            return NotFound(new { error = result.Error });
        }

        return Ok(result.Value);
    }

    /// <summary>
    /// List all landmarks.
    /// </summary>
    /// <param name="includeInactive">Include inactive landmarks</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of all landmarks</returns>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<LandmarkDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ResponseCache(Duration = 120, VaryByQueryKeys = new[] { "includeInactive" })]
    public async Task<ActionResult<IReadOnlyList<LandmarkDto>>> GetAllLandmarks(
        [FromQuery] bool includeInactive = false,
        CancellationToken cancellationToken = default)
    {
        var query = new GetAllLandmarksQuery { IncludeInactive = includeInactive };
        var result = await _mediator.Send(query, cancellationToken);

        if (!result.IsSuccess)
        {
            return BadRequest(new { error = result.Error });
        }

        return Ok(result.Value);
    }

    /// <summary>
    /// Find landmarks near a coordinate.
    /// </summary>
    /// <param name="x">X coordinate</param>
    /// <param name="y">Y coordinate</param>
    /// <param name="z">Z coordinate</param>
    /// <param name="radius">Search radius (default 0.2)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of nearby landmarks</returns>
    [HttpGet("nearby")]
    [ProducesResponseType(typeof(IReadOnlyList<LandmarkDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ResponseCache(Duration = 60, VaryByQueryKeys = new[] { "x", "y", "z", "radius" })]
    public async Task<ActionResult<IReadOnlyList<LandmarkDto>>> GetNearbyLandmarks(
        [FromQuery] double x,
        [FromQuery] double y,
        [FromQuery] double z,
        [FromQuery] double radius = 0.2,
        CancellationToken cancellationToken = default)
    {
        var query = new GetLandmarksNearCoordinateQuery
        {
            X = x,
            Y = y,
            Z = z,
            Radius = radius
        };

        var result = await _mediator.Send(query, cancellationToken);

        if (!result.IsSuccess)
        {
            return BadRequest(new { error = result.Error });
        }

        return Ok(result.Value);
    }

    /// <summary>
    /// Update landmark (deactivate, change description).
    /// </summary>
    /// <param name="name">Landmark name</param>
    /// <param name="request">Update request</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>No content on success</returns>
    [HttpPatch("{name}")]
    [Authorize(Policy = "WritePolicy")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> UpdateLandmark(
        string name,
        [FromBody] UpdateLandmarkCommand request,
        CancellationToken cancellationToken)
    {
        request = request with { Name = name };

        _logger.LogInformation("Updating landmark '{Name}'", name);

        var result = await _mediator.Send(request, cancellationToken);

        if (!result.IsSuccess)
        {
            if (result.Error?.Contains("not found") == true)
            {
                return NotFound(new { error = result.Error });
            }
            return BadRequest(new { error = result.Error });
        }

        return NoContent();
    }

    /// <summary>
    /// Delete (soft delete) a landmark.
    /// </summary>
    /// <param name="name">Landmark name</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>No content on success</returns>
    [HttpDelete("{name}")]
    [Authorize(Policy = "AdminPolicy")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> DeleteLandmark(
        string name,
        CancellationToken cancellationToken)
    {
        _logger.LogWarning("Deleting landmark '{Name}'", name);

        var command = new DeleteLandmarkCommand { Name = name };
        var result = await _mediator.Send(command, cancellationToken);

        if (!result.IsSuccess)
        {
            if (result.Error?.Contains("not found") == true)
            {
                return NotFound(new { error = result.Error });
            }
            return BadRequest(new { error = result.Error });
        }

        return NoContent();
    }

    /// <summary>
    /// Auto-detect landmark candidates from clustering.
    /// </summary>
    /// <param name="epsilon">DBSCAN epsilon (default 0.1)</param>
    /// <param name="minSamples">Minimum cluster size (default 10)</param>
    /// <param name="minClusterSize">Minimum points for landmark (default 20)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of landmark candidates</returns>
    [HttpGet("detect")]
    [Authorize(Policy = "AdminPolicy")]
    [ProducesResponseType(typeof(IReadOnlyList<LandmarkCandidateDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ResponseCache(Duration = 300, VaryByQueryKeys = new[] { "epsilon", "minSamples", "minClusterSize" })]
    public async Task<ActionResult<IReadOnlyList<LandmarkCandidateDto>>> DetectLandmarks(
        [FromQuery] double epsilon = 0.1,
        [FromQuery] int minSamples = 10,
        [FromQuery] int minClusterSize = 20,
        CancellationToken cancellationToken = default)
    {
        var query = new DetectLandmarkCandidatesQuery
        {
            Epsilon = epsilon,
            MinSamples = minSamples,
            MinClusterSize = minClusterSize
        };

        var result = await _mediator.Send(query, cancellationToken);

        if (!result.IsSuccess)
        {
            return BadRequest(new { error = result.Error });
        }

        return Ok(result.Value);
    }
}
