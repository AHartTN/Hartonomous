using Hartonomous.CodeAtomizer.Core.Atomizers;
using Hartonomous.CodeAtomizer.Core.Models;
using Hartonomous.CodeAtomizer.Core.Services;
using Hartonomous.CodeAtomizer.Core.Spatial;
using Microsoft.AspNetCore.Mvc;
using System.Text;
using System.Text.Json;

namespace Hartonomous.CodeAtomizer.Api.Controllers;

/// <summary>
/// Code generation endpoint using memory retrieval and AI-guided synthesis
/// </summary>
[ApiController]
[Route("api/v1/[controller]")]
[Produces("application/json")]
public class GenerateController : ControllerBase
{
    private readonly ILogger<GenerateController> _logger;
    private readonly AtomMemoryService _memoryService;
    private readonly LanguageProfileLoader _profileLoader;

    public GenerateController(
        ILogger<GenerateController> logger,
        AtomMemoryService memoryService,
        LanguageProfileLoader profileLoader)
    {
        _logger = logger;
        _memoryService = memoryService;
        _profileLoader = profileLoader;
    }

    /// <summary>
    /// Generate code based on natural language prompt and memory context
    /// </summary>
    /// <param name="request">Generation request with language, prompt, and optional context</param>
    /// <returns>Generated code with confidence scores and context atoms used</returns>
    [HttpPost]
    [ProducesResponseType(typeof(GenerateCodeResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public IActionResult GenerateCode([FromBody] GenerateCodeRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Language))
            return BadRequest(new { error = "Language is required" });

        if (string.IsNullOrWhiteSpace(request.Prompt))
            return BadRequest(new { error = "Prompt is required" });

        try
        {
            _logger.LogInformation(
                "Generating {Language} code from prompt: {Prompt}",
                request.Language,
                request.Prompt);

            // 1. Determine semantic category from prompt
            var category = InferCategoryFromPrompt(request.Prompt);

            // 2. Compute spatial position for this request
            var (x, y, z, hilbertIndex) = LandmarkProjection.ComputePositionWithHilbert(
                modality: "code",
                category: category,
                specificity: "concrete",
                identifier: $"{request.Language}:{category}:{request.Prompt}"
            );

            // 3. Build generation context from memory
            var context = _memoryService.BuildContext(
                request.Language,
                category,
                x, y, z,
                request.ProximityRadius ?? 0.2,
                request.MaxContextAtoms ?? 30
            );

            // 4. Generate code using retrieved context
            var generatedCode = GenerateCodeFromContext(
                request.Language,
                request.Prompt,
                context,
                request.Style ?? "idiomatic"
            );

            // 5. Compute confidence score
            var confidence = ComputeConfidenceScore(context, request.Language);

            return Ok(new GenerateCodeResponse
            {
                GeneratedCode = generatedCode,
                Language = request.Language,
                Confidence = confidence,
                ContextAtomsUsed = context.ContextAtoms.Count,
                ContextAtoms = context.ContextAtoms.Select(a => new AtomSummary
                {
                    CanonicalText = a.CanonicalText ?? "",
                    Category = a.Subtype,
                    Modality = a.Modality,
                    SpatialDistance = (float)ComputeDistance(a.SpatialKey, context.FocalPoint)
                }).ToList(),
                SpatialPosition = new SpatialPositionDto
                {
                    X = x,
                    Y = y,
                    Z = z
                },
                Metadata = new GenerationMetadata
                {
                    Category = category,
                    Style = request.Style ?? "idiomatic",
                    RelationsFound = context.Relations.Count,
                    GenerationMethod = "memory-retrieval-synthesis"
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Code generation failed");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    /// <summary>
    /// Get generation context without generating code (for inspection)
    /// </summary>
    [HttpPost("context")]
    [ProducesResponseType(typeof(GenerationContextResponse), StatusCodes.Status200OK)]
    public IActionResult GetGenerationContext([FromBody] GenerateCodeRequest request)
    {
        var category = InferCategoryFromPrompt(request.Prompt);
        var (x, y, z, _) = LandmarkProjection.ComputePositionWithHilbert(
            modality: "code",
            category: category,
            specificity: "concrete",
            identifier: $"{request.Language}:{category}:{request.Prompt}"
        );

        var context = _memoryService.BuildContext(
            request.Language,
            category,
            x, y, z,
            request.ProximityRadius ?? 0.2,
            request.MaxContextAtoms ?? 30
        );

        return Ok(new GenerationContextResponse
        {
            Category = category,
            FocalPoint = new SpatialPositionDto { X = x, Y = y, Z = z },
            ContextAtoms = context.ContextAtoms.Select(a => new AtomDetail
            {
                CanonicalText = a.CanonicalText ?? "",
                Category = a.Subtype,
                Modality = a.Modality,
                Metadata = a.Metadata,
                SpatialPosition = new SpatialPositionDto
                {
                    X = a.SpatialKey.X,
                    Y = a.SpatialKey.Y,
                    Z = a.SpatialKey.Z
                }
            }).ToList(),
            Relations = context.Relations.Select(r => new RelationSummary
            {
                RelationType = r.RelationType,
                Weight = r.Weight
            }).ToList()
        });
    }

    /// <summary>
    /// Get memory statistics
    /// </summary>
    [HttpGet("stats")]
    [ProducesResponseType(typeof(MemoryStatistics), StatusCodes.Status200OK)]
    public IActionResult GetMemoryStatistics()
    {
        var stats = _memoryService.GetStatistics();
        return Ok(stats);
    }

    private string InferCategoryFromPrompt(string prompt)
    {
        var lowerPrompt = prompt.ToLowerInvariant();

        if (lowerPrompt.Contains("function") || lowerPrompt.Contains("method") || lowerPrompt.Contains("calculate"))
            return "function";
        if (lowerPrompt.Contains("class") || lowerPrompt.Contains("type") || lowerPrompt.Contains("model"))
            return "class";
        if (lowerPrompt.Contains("import") || lowerPrompt.Contains("require") || lowerPrompt.Contains("using"))
            return "import";
        if (lowerPrompt.Contains("test") || lowerPrompt.Contains("unit test"))
            return "test";
        if (lowerPrompt.Contains("api") || lowerPrompt.Contains("endpoint") || lowerPrompt.Contains("route"))
            return "api";

        return "function"; // Default to function
    }

    private string GenerateCodeFromContext(
        string language,
        string prompt,
        GenerationContext context,
        string style)
    {
        // Phase 1: Template-based generation using memory context
        // TODO: Replace with AI model integration (GPT-4, Claude, local LLM)

        var sb = new StringBuilder();
        var profile = _profileLoader.GetProfile(language.ToLowerInvariant());

        // Generate comment header
        sb.AppendLine($"# Generated from prompt: {prompt}");
        sb.AppendLine($"# Language: {language}, Style: {style}");
        sb.AppendLine($"# Context: {context.ContextAtoms.Count} atoms, {context.Relations.Count} relations");
        sb.AppendLine();

        // Analyze context atoms to extract patterns
        var functionAtoms = context.ContextAtoms.Where(a => a.Subtype == "function").ToList();
        var classAtoms = context.ContextAtoms.Where(a => a.Subtype == "class").ToList();

        if (context.Category == "function" && functionAtoms.Any())
        {
            // Generate function based on similar functions in context
            sb.AppendLine(GenerateFunctionFromContext(language, prompt, functionAtoms, profile));
        }
        else if (context.Category == "class" && classAtoms.Any())
        {
            // Generate class based on similar classes in context
            sb.AppendLine(GenerateClassFromContext(language, prompt, classAtoms, profile));
        }
        else
        {
            // Fallback: Generate simple template
            sb.AppendLine(GenerateTemplateCode(language, prompt, context.Category));
        }

        return sb.ToString();
    }

    private string GenerateFunctionFromContext(
        string language,
        string prompt,
        List<Atom> functionAtoms,
        LanguageSemanticProfile? profile)
    {
        var sb = new StringBuilder();

        // Extract function name from prompt
        var words = prompt.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var verbIndex = Array.FindIndex(words, w => 
            w.Equals("calculate", StringComparison.OrdinalIgnoreCase) ||
            w.Equals("compute", StringComparison.OrdinalIgnoreCase) ||
            w.Equals("create", StringComparison.OrdinalIgnoreCase) ||
            w.Equals("generate", StringComparison.OrdinalIgnoreCase));

        var functionName = verbIndex >= 0 && verbIndex < words.Length - 1
            ? string.Join("_", words.Skip(verbIndex).Take(2)).ToLowerInvariant()
            : "generated_function";

        if (language.Equals("python", StringComparison.OrdinalIgnoreCase))
        {
            sb.AppendLine($"def {functionName}():");
            sb.AppendLine($"    \"\"\"");
            sb.AppendLine($"    {prompt}");
            sb.AppendLine($"    ");
            sb.AppendLine($"    Similar to: {string.Join(", ", functionAtoms.Take(3).Select(a => a.CanonicalText))}");
            sb.AppendLine($"    \"\"\"");
            sb.AppendLine($"    # TODO: Implement based on context");
            sb.AppendLine($"    pass");
        }
        else if (language.Equals("csharp", StringComparison.OrdinalIgnoreCase))
        {
            var pascalName = string.Concat(functionName.Split('_').Select(w => 
                char.ToUpper(w[0]) + w.Substring(1)));
            sb.AppendLine($"/// <summary>");
            sb.AppendLine($"/// {prompt}");
            sb.AppendLine($"/// Similar to: {string.Join(", ", functionAtoms.Take(3).Select(a => a.CanonicalText))}");
            sb.AppendLine($"/// </summary>");
            sb.AppendLine($"public void {pascalName}()");
            sb.AppendLine($"{{");
            sb.AppendLine($"    // TODO: Implement based on context");
            sb.AppendLine($"}}");
        }

        return sb.ToString();
    }

    private string GenerateClassFromContext(
        string language,
        string prompt,
        List<Atom> classAtoms,
        LanguageSemanticProfile? profile)
    {
        var sb = new StringBuilder();

        // Extract class name from prompt
        var words = prompt.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var className = words.LastOrDefault(w => char.IsUpper(w[0])) ?? "GeneratedClass";

        if (language.Equals("python", StringComparison.OrdinalIgnoreCase))
        {
            sb.AppendLine($"class {className}:");
            sb.AppendLine($"    \"\"\"");
            sb.AppendLine($"    {prompt}");
            sb.AppendLine($"    \"\"\"");
            sb.AppendLine($"    def __init__(self):");
            sb.AppendLine($"        pass");
        }
        else if (language.Equals("csharp", StringComparison.OrdinalIgnoreCase))
        {
            sb.AppendLine($"/// <summary>");
            sb.AppendLine($"/// {prompt}");
            sb.AppendLine($"/// </summary>");
            sb.AppendLine($"public class {className}");
            sb.AppendLine($"{{");
            sb.AppendLine($"    // TODO: Implement based on context");
            sb.AppendLine($"}}");
        }

        return sb.ToString();
    }

    private string GenerateTemplateCode(string language, string prompt, string category)
    {
        if (language.Equals("python", StringComparison.OrdinalIgnoreCase))
        {
            return $"# {prompt}\n# TODO: Implement {category}\npass";
        }
        else if (language.Equals("csharp", StringComparison.OrdinalIgnoreCase))
        {
            return $"// {prompt}\n// TODO: Implement {category}";
        }

        return $"// {prompt}";
    }

    private double ComputeConfidenceScore(GenerationContext context, string language)
    {
        // Confidence based on context richness
        var baseScore = 0.3; // Minimum confidence

        // More context atoms = higher confidence
        var contextScore = Math.Min(context.ContextAtoms.Count / 30.0, 0.4);

        // More relations = higher confidence
        var relationScore = Math.Min(context.Relations.Count / 10.0, 0.2);

        // Language match bonus
        var languageScore = context.ContextAtoms.Any(a => 
            a.Metadata?.Contains(language, StringComparison.OrdinalIgnoreCase) ?? false) ? 0.1 : 0.0;

        return Math.Min(baseScore + contextScore + relationScore + languageScore, 1.0);
    }

    private double ComputeDistance(SpatialPosition a, SpatialPosition b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        var dz = a.Z - b.Z;
        return Math.Sqrt(dx * dx + dy * dy + dz * dz);
    }
}

// DTOs
public class GenerateCodeRequest
{
    public string Language { get; set; } = string.Empty;
    public string Prompt { get; set; } = string.Empty;
    public string? Style { get; set; }
    public double? ProximityRadius { get; set; }
    public int? MaxContextAtoms { get; set; }
}

public class GenerateCodeResponse
{
    public string GeneratedCode { get; set; } = string.Empty;
    public string Language { get; set; } = string.Empty;
    public double Confidence { get; set; }
    public int ContextAtomsUsed { get; set; }
    public List<AtomSummary> ContextAtoms { get; set; } = new();
    public SpatialPositionDto SpatialPosition { get; set; } = new();
    public GenerationMetadata Metadata { get; set; } = new();
}

public class GenerationContextResponse
{
    public string Category { get; set; } = string.Empty;
    public SpatialPositionDto FocalPoint { get; set; } = new();
    public List<AtomDetail> ContextAtoms { get; set; } = new();
    public List<RelationSummary> Relations { get; set; } = new();
}

public class AtomSummary
{
    public string CanonicalText { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Modality { get; set; } = string.Empty;
    public double SpatialDistance { get; set; }
}

public class AtomDetail
{
    public string CanonicalText { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Modality { get; set; } = string.Empty;
    public string? Metadata { get; set; }
    public SpatialPositionDto SpatialPosition { get; set; } = new();
}

public class RelationSummary
{
    public string RelationType { get; set; } = string.Empty;
    public double Weight { get; set; }
}

public class GenerationMetadata
{
    public string Category { get; set; } = string.Empty;
    public string Style { get; set; } = string.Empty;
    public int RelationsFound { get; set; }
    public string GenerationMethod { get; set; } = string.Empty;
}
