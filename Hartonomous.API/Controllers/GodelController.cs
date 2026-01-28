using Microsoft.AspNetCore.Mvc;
using Hartonomous.Marshal;

namespace Hartonomous.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class GodelController : ControllerBase
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<GodelController> _logger;

    public GodelController(IConfiguration configuration, ILogger<GodelController> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    [HttpPost("analyze")]
    public IActionResult AnalyzeProblem([FromBody] AnalyzeRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Problem))
            return BadRequest("Problem statement is required");

        var connString = _configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrEmpty(connString))
            return StatusCode(500, "Database connection string not configured.");

        var dbHandle = NativeMethods.DbCreate(connString);
        if (dbHandle == IntPtr.Zero)
            return StatusCode(500, "Failed to connect to native database.");

        try
        {
            var godelHandle = NativeMethods.GodelCreate(dbHandle);
            if (godelHandle == IntPtr.Zero)
                return StatusCode(500, "Failed to create Godel engine.");

            try
            {
                if (NativeMethods.GodelAnalyze(godelHandle, request.Problem, out var plan))
                {
                    // Marshal the plan to a managed object (simplified for JSON response)
                    // In a real app, you'd map the pointers to C# objects manually here 
                    // or use a more sophisticated marshaler.
                    // For now, we return a summary.
                    
                    var result = new
                    {
                        Plan = new
                        {
                            TotalSteps = plan.TotalSteps,
                            SolvableSteps = plan.SolvableSteps,
                            SubProblemsCount = plan.SubProblemsCount,
                            KnowledgeGapsCount = plan.KnowledgeGapsCount
                        }
                    };

                    // Free the native plan memory
                    NativeMethods.GodelFreePlan(ref plan);

                    return Ok(result);
                }
                
                return StatusCode(500, "Godel analysis failed.");
            }
            finally
            {
                NativeMethods.GodelDestroy(godelHandle);
            }
        }
        finally
        {
            NativeMethods.DbDestroy(dbHandle);
        }
    }

    public class AnalyzeRequest
    {
        public string Problem { get; set; } = string.Empty;
    }
}
