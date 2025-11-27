using Hartonomous.CodeAtomizer.Core.Atomizers;
using Microsoft.AspNetCore.Mvc;
using System.Text;

namespace Hartonomous.CodeAtomizer.Api.Controllers;

/// <summary>
/// Code atomization endpoint using Roslyn semantic analysis
/// </summary>
[ApiController]
[Route("api/v1/[controller]")]
[Produces("application/json")]
public class AtomizeController : ControllerBase
{
    private readonly ILogger<AtomizeController> _logger;
    private readonly RoslynCSharpAtomizer _atomizer;

    public AtomizeController(ILogger<AtomizeController> logger)
    {
        _logger = logger;
        _atomizer = new RoslynCSharpAtomizer();
    }

    /// <summary>
    /// Atomize C# source code into atoms, compositions, and relations
    /// </summary>
    /// <param name="request">Code atomization request</param>
    /// <returns>Atomization result with atoms, compositions, and relations</returns>
    [HttpPost("csharp")]
    [ProducesResponseType(typeof(AtomizeResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public IActionResult AtomizeCSharp([FromBody] AtomizeRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Code))
        {
            return BadRequest(new { error = "Code cannot be empty" });
        }

        try
        {
            _logger.LogInformation(
                "Atomizing C# code: {FileName} ({Length} bytes)",
                request.FileName ?? "unnamed.cs",
                request.Code.Length);

            var result = _atomizer.Atomize(
                request.Code,
                request.FileName ?? "code.cs",
                request.Metadata);

            _logger.LogInformation(
                "Atomization complete: {TotalAtoms} atoms, {Compositions} compositions, {Relations} relations",
                result.TotalAtoms,
                result.Compositions.Length,
                result.Relations.Length);

            return Ok(new AtomizeResponse
            {
                Success = true,
                TotalAtoms = result.TotalAtoms,
                UniqueAtoms = result.UniqueAtoms,
                TotalCompositions = result.Compositions.Length,
                TotalRelations = result.Relations.Length,
                Atoms = result.Atoms.Select(a => new AtomDto
                {
                    ContentHash = Convert.ToBase64String(a.ContentHash),
                    CanonicalText = a.CanonicalText,
                    Modality = a.Modality,
                    Subtype = a.Subtype,
                    SpatialKey = new SpatialPositionDto
                    {
                        X = a.SpatialKey.X,
                        Y = a.SpatialKey.Y,
                        Z = a.SpatialKey.Z
                    },
                    Metadata = a.Metadata
                }).ToArray(),
                Compositions = result.Compositions.Select(c => new CompositionDto
                {
                    ParentHash = Convert.ToBase64String(c.ParentAtomHash),
                    ComponentHash = Convert.ToBase64String(c.ComponentAtomHash),
                    SequenceIndex = c.SequenceIndex
                }).ToArray(),
                Relations = result.Relations.Select(r => new RelationDto
                {
                    SourceHash = Convert.ToBase64String(r.SourceAtomHash),
                    TargetHash = Convert.ToBase64String(r.TargetAtomHash),
                    RelationType = r.RelationType,
                    Weight = r.Weight,
                    Metadata = r.Metadata
                }).ToArray()
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Atomization failed");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    /// <summary>
    /// Atomize C# code from uploaded file
    /// </summary>
    [HttpPost("csharp/file")]
    [ProducesResponseType(typeof(AtomizeResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> AtomizeCSharpFile(IFormFile file, [FromQuery] string? metadata = null)
    {
        if (file == null || file.Length == 0)
        {
            return BadRequest(new { error = "File is required" });
        }

        if (!file.FileName.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest(new { error = "Only .cs files are supported" });
        }

        try
        {
            using var stream = file.OpenReadStream();
            using var reader = new StreamReader(stream);
            var code = await reader.ReadToEndAsync();

            var request = new AtomizeRequest
            {
                Code = code,
                FileName = file.FileName,
                Metadata = metadata
            };

            return AtomizeCSharp(request);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "File atomization failed");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    /// <summary>
    /// Health check endpoint
    /// </summary>
    [HttpGet("health")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult Health()
    {
        return Ok(new
        {
            status = "healthy",
            service = "Hartonomous Code Atomizer",
            version = "0.1.0",
            capabilities = new[] { "csharp" }
        });
    }
}

#region DTOs

public record AtomizeRequest
{
    public required string Code { get; init; }
    public string? FileName { get; init; }
    public string? Metadata { get; init; }
}

public record AtomizeResponse
{
    public bool Success { get; init; }
    public int TotalAtoms { get; init; }
    public int UniqueAtoms { get; init; }
    public int TotalCompositions { get; init; }
    public int TotalRelations { get; init; }
    public required AtomDto[] Atoms { get; init; }
    public required CompositionDto[] Compositions { get; init; }
    public required RelationDto[] Relations { get; init; }
}

public record AtomDto
{
    public required string ContentHash { get; init; }
    public required string CanonicalText { get; init; }
    public required string Modality { get; init; }
    public string? Subtype { get; init; }
    public required SpatialPositionDto SpatialKey { get; init; }
    public required string Metadata { get; init; }
}

public record SpatialPositionDto
{
    public double X { get; init; }
    public double Y { get; init; }
    public double Z { get; init; }
}

public record CompositionDto
{
    public required string ParentHash { get; init; }
    public required string ComponentHash { get; init; }
    public int SequenceIndex { get; init; }
}

public record RelationDto
{
    public required string SourceHash { get; init; }
    public required string TargetHash { get; init; }
    public required string RelationType { get; init; }
    public double Weight { get; init; }
    public string? Metadata { get; init; }
}

#endregion
