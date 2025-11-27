using Hartonomous.CodeAtomizer.Core.Spatial;
using Microsoft.AspNetCore.Mvc;

namespace Hartonomous.CodeAtomizer.Api.Controllers;

/// <summary>
/// Landmark visualization and debugging endpoints
/// </summary>
[ApiController]
[Route("api/v1/[controller]")]
[Produces("application/json")]
public class LandmarksController : ControllerBase
{
    private readonly ILogger<LandmarksController> _logger;

    public LandmarksController(ILogger<LandmarksController> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Get all landmark coordinates for visualization (3D radar chart)
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(LandmarkMapResponse), StatusCodes.Status200OK)]
    public IActionResult GetLandmarks()
    {
        var landmarks = LandmarkProjection.GetAllLandmarks();

        return Ok(new LandmarkMapResponse
        {
            TotalLandmarks = landmarks.Count,
            Dimensions = new DimensionInfo[]
            {
                new() { Axis = "X", Name = "Modality", Description = "Type of information (code, text, image, audio, video)" },
                new() { Axis = "Y", Name = "Category", Description = "Semantic role (class, method, field, literal)" },
                new() { Axis = "Z", Name = "Specificity", Description = "Abstraction level (abstract, concrete, literal)" }
            },
            Landmarks = landmarks.Select(kv => new LandmarkDto
            {
                Key = kv.Key,
                X = kv.Value.X,
                Y = kv.Value.Y,
                Z = kv.Value.Z,
                Description = GetLandmarkDescription(kv.Key)
            }).ToArray()
        });
    }

    /// <summary>
    /// Compute spatial position for a given atom definition
    /// </summary>
    [HttpPost("compute")]
    [ProducesResponseType(typeof(PositionResponse), StatusCodes.Status200OK)]
    public IActionResult ComputePosition([FromBody] PositionRequest request)
    {
        try
        {
            var position = LandmarkProjection.ComputePosition(
                request.Modality,
                request.Category,
                request.Specificity,
                request.Identifier
            );

            return Ok(new PositionResponse
            {
                X = position.X,
                Y = position.Y,
                Z = position.Z,
                Modality = request.Modality,
                Category = request.Category,
                Specificity = request.Specificity ?? "concrete",
                Identifier = request.Identifier
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Position computation failed");
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Get nearest landmark for a given coordinate
    /// </summary>
    [HttpPost("nearest")]
    [ProducesResponseType(typeof(NearestLandmarkResponse), StatusCodes.Status200OK)]
    public IActionResult GetNearestLandmark([FromBody] CoordinateRequest request)
    {
        var allLandmarks = LandmarkProjection.GetAllLandmarks();
        
        var nearest = allLandmarks
            .Select(kv => new
            {
                Key = kv.Key,
                Position = kv.Value,
                Distance = LandmarkProjection.ComputeDistance(
                    (request.X, request.Y, request.Z),
                    kv.Value
                )
            })
            .OrderBy(x => x.Distance)
            .First();

        return Ok(new NearestLandmarkResponse
        {
            LandmarkKey = nearest.Key,
            X = nearest.Position.X,
            Y = nearest.Position.Y,
            Z = nearest.Position.Z,
            Distance = nearest.Distance,
            Description = GetLandmarkDescription(nearest.Key)
        });
    }

    private static string GetLandmarkDescription(string key)
    {
        var parts = key.Split(':');
        if (parts.Length != 3) return key;

        return $"{parts[0]} {parts[1]} ({parts[2]} level)";
    }
}

#region DTOs

public record LandmarkMapResponse
{
    public int TotalLandmarks { get; init; }
    public required DimensionInfo[] Dimensions { get; init; }
    public required LandmarkDto[] Landmarks { get; init; }
}

public record DimensionInfo
{
    public required string Axis { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }
}

public record LandmarkDto
{
    public required string Key { get; init; }
    public double X { get; init; }
    public double Y { get; init; }
    public double Z { get; init; }
    public required string Description { get; init; }
}

public record PositionRequest
{
    public required string Modality { get; init; }
    public required string Category { get; init; }
    public string? Specificity { get; init; }
    public string? Identifier { get; init; }
}

public record PositionResponse
{
    public double X { get; init; }
    public double Y { get; init; }
    public double Z { get; init; }
    public required string Modality { get; init; }
    public required string Category { get; init; }
    public required string Specificity { get; init; }
    public string? Identifier { get; init; }
}

public record CoordinateRequest
{
    public double X { get; init; }
    public double Y { get; init; }
    public double Z { get; init; }
}

public record NearestLandmarkResponse
{
    public required string LandmarkKey { get; init; }
    public double X { get; init; }
    public double Y { get; init; }
    public double Z { get; init; }
    public double Distance { get; init; }
    public required string Description { get; init; }
}

#endregion
