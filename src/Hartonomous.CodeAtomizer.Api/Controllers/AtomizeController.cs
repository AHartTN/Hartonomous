using Hartonomous.CodeAtomizer.Core.Atomizers;
using Hartonomous.CodeAtomizer.Core.Models;
using Hartonomous.CodeAtomizer.Core.Services;
using Microsoft.AspNetCore.Mvc;
using System.Text;

namespace Hartonomous.CodeAtomizer.Api.Controllers;

/// <summary>
/// Code atomization endpoint using Roslyn semantic analysis and Tree-sitter multi-language parsing
/// </summary>
[ApiController]
[Route("api/v1/[controller]")]
[Produces("application/json")]
public class AtomizeController : ControllerBase
{
    private readonly ILogger<AtomizeController> _logger;
    private readonly RoslynCSharpAtomizer _roslynAtomizer;
    private readonly LanguageProfileLoader _profileLoader;
    private readonly AtomMemoryService _memoryService;

    public AtomizeController(
        ILogger<AtomizeController> logger,
        LanguageProfileLoader profileLoader,
        AtomMemoryService memoryService)
    {
        _logger = logger;
        _roslynAtomizer = new RoslynCSharpAtomizer();
        _profileLoader = profileLoader;
        _memoryService = memoryService;
    }

    /// <summary>
    /// Atomize C# source code into atoms, compositions, and relations
    /// </summary>
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

            var result = _roslynAtomizer.Atomize(
                request.Code,
                request.FileName ?? "code.cs",
                request.Metadata);

            // Store in memory for code generation context
            _memoryService.Store(result);

            return Ok(BuildResponse(result, "Roslyn"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "C# atomization failed");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    /// <summary>
    /// Atomize code in any supported language (Python, JS, Go, Rust, Java, etc.)
    /// Uses Tree-sitter for multi-language support
    /// </summary>
    [HttpPost("{language}")]
    [ProducesResponseType(typeof(AtomizeResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public IActionResult AtomizeAnyLanguage(string language, [FromBody] AtomizeRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Code))
        {
            return BadRequest(new { error = "Code cannot be empty" });
        }

        // Route C# to Roslyn for semantic analysis
        if (language.Equals("csharp", StringComparison.OrdinalIgnoreCase) ||
            language.Equals("cs", StringComparison.OrdinalIgnoreCase))
        {
            return AtomizeCSharp(request);
        }

        try
        {
            _logger.LogInformation(
                "Atomizing {Language} code: {FileName} ({Length} bytes)",
                language,
                request.FileName ?? $"unnamed.{language}",
                request.Code.Length);

            var atomizer = new TreeSitterAtomizer(_profileLoader);
            var result = atomizer.Atomize(
                request.Code,
                request.FileName ?? $"code.{language}",
                request.Metadata);

            // Store in memory for code generation context
            _memoryService.Store(result);

            return Ok(BuildResponse(result, "Tree-sitter"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "{Language} atomization failed", language);
            return StatusCode(500, new { error = ex.Message });
        }
    }

    /// <summary>
    /// Atomize code file (auto-detect language from extension)
    /// </summary>
    [HttpPost("file")]
    [ProducesResponseType(typeof(AtomizeResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> AtomizeFile(IFormFile file, [FromQuery] string? metadata = null)
    {
        if (file == null || file.Length == 0)
        {
            return BadRequest(new { error = "File is required" });
        }

        try
        {
            using var stream = file.OpenReadStream();
            using var reader = new StreamReader(stream);
            var code = await reader.ReadToEndAsync();

            var ext = Path.GetExtension(file.FileName);
            
            // Route to appropriate atomizer based on extension
            if (ext.Equals(".cs", StringComparison.OrdinalIgnoreCase))
            {
                var result = _roslynAtomizer.Atomize(code, file.FileName, metadata);
                return Ok(BuildResponse(result, "Roslyn"));
            }
            else if (TreeSitterAtomizer.CanHandle(ext))
            {
                var atomizer = new TreeSitterAtomizer(_profileLoader);
                var result = atomizer.Atomize(code, file.FileName, metadata);
                return Ok(BuildResponse(result, "Tree-sitter"));
            }
            else
            {
                return BadRequest(new { error = $"Unsupported file extension: {ext}" });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "File atomization failed");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    /// <summary>
    /// Get supported languages
    /// </summary>
    [HttpGet("languages")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult GetSupportedLanguages()
    {
        return Ok(new
        {
            languages = new
            {
                roslyn = new[] { "csharp", "vb" },
                treesitter = new[]
                {
                    "python", "javascript", "typescript", "go", "rust",
                    "java", "cpp", "c", "ruby", "php", "swift", "kotlin",
                    "scala", "bash", "json", "yaml", "toml", "sql"
                }
            },
            total = 20
        });
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
            version = "0.2.0",
            parsers = new[] { "Roslyn", "Tree-sitter" },
            languages = 20
        });
    }

    private AtomizeResponse BuildResponse(AtomizationResult result, string parser)
    {
        _logger.LogInformation(
            "Atomization complete ({Parser}): {TotalAtoms} atoms, {Compositions} compositions, {Relations} relations",
            parser,
            result.TotalAtoms,
            result.Compositions.Length,
            result.Relations.Length);

        return new AtomizeResponse
        {
            Success = true,
            TotalAtoms = result.TotalAtoms,
            UniqueAtoms = result.UniqueAtoms,
            TotalCompositions = result.Compositions.Length,
            TotalRelations = result.Relations.Length,
            Parser = parser,
            Atoms = result.Atoms.Select(a => new AtomDto
            {
                ContentHash = Convert.ToBase64String(a.ContentHash),
                CanonicalText = a.CanonicalText,
                Modality = a.Modality,
                Subtype = a.Subtype,
                HilbertIndex = a.HilbertIndex,
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
        };
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
    public required string Parser { get; init; }
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
    public long HilbertIndex { get; init; }
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
