using System.Runtime.InteropServices;
using Hartonomous.Core.Services;
using Hartonomous.Marshal;
using Microsoft.AspNetCore.Mvc;

namespace Hartonomous.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class GodelController : ControllerBase
{
    private readonly EngineService _engine;
    private readonly ILogger<GodelController> _logger;

    public GodelController(EngineService engine, ILogger<GodelController> logger)
    {
        _engine = engine;
        _logger = logger;
    }

    [HttpPost("analyze")]
    public IActionResult AnalyzeProblem([FromBody] AnalyzeRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Problem))
            return BadRequest("Problem statement is required");

        var godelHandle = NativeMethods.GodelCreate(_engine.DbHandle);
        if (godelHandle == IntPtr.Zero)
            return StatusCode(500, new { error = EngineService.GetLastError() });

        try
        {
            if (NativeMethods.GodelAnalyze(godelHandle, request.Problem, out var plan))
            {
                var result = new
                {
                    Plan = new
                    {
                        plan.TotalSteps,
                        plan.SolvableSteps,
                        SubProblemsCount = (int)plan.SubProblemsCount,
                        KnowledgeGapsCount = (int)plan.KnowledgeGapsCount,
                    }
                };

                NativeMethods.GodelFreePlan(ref plan);
                return Ok(result);
            }
            return StatusCode(500, new { error = "Godel analysis failed" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Godel analysis failed");
            return StatusCode(500, new { error = ex.Message });
        }
        finally
        {
            NativeMethods.GodelDestroy(godelHandle);
        }
    }

    public sealed class AnalyzeRequest
    {
        public string Problem { get; set; } = string.Empty;
    }
}
