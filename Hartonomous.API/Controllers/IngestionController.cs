using Microsoft.AspNetCore.Mvc;
using Hartonomous.Marshal;

namespace Hartonomous.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class IngestionController : ControllerBase
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<IngestionController> _logger;

    public IngestionController(IConfiguration configuration, ILogger<IngestionController> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    [HttpPost("text")]
    public IActionResult IngestText([FromBody] IngestTextRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Text))
            return BadRequest("Text is required");

        return ExecuteIngestion((ingester) => 
        {
            if (NativeMethods.IngestText(ingester, request.Text, out var stats))
            {
                return Ok(stats);
            }
            return StatusCode(500, "Ingestion failed internally. " + GetLastError());
        });
    }

    [HttpPost("file")]
    public IActionResult IngestFile([FromBody] IngestFileRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.FilePath))
            return BadRequest("FilePath is required");

        return ExecuteIngestion((ingester) => 
        {
            if (NativeMethods.IngestFile(ingester, request.FilePath, out var stats))
            {
                return Ok(stats);
            }
            return StatusCode(500, "File ingestion failed internally.");
        });
    }

    private IActionResult ExecuteIngestion(Func<IntPtr, IActionResult> action)
    {
        var connString = _configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrEmpty(connString))
            return StatusCode(500, "Database connection string not configured.");

        // 1. Connect to DB
        var dbHandle = NativeMethods.DbCreate(connString);
        if (dbHandle == IntPtr.Zero)
            return StatusCode(500, "Failed to connect to native database. " + GetLastError());

        try
        {
            // 2. Create Ingester
            var ingesterHandle = NativeMethods.IngesterCreate(dbHandle);
            if (ingesterHandle == IntPtr.Zero)
                return StatusCode(500, "Failed to create native ingester. " + GetLastError());

            try
            {
                // 3. Execute Action
                return action(ingesterHandle);
            }
            finally
            {
                NativeMethods.IngesterDestroy(ingesterHandle);
            }
        }
        finally
        {
            NativeMethods.DbDestroy(dbHandle);
        }
    }

    private string GetLastError()
    {
        var ptr = NativeMethods.GetLastError();
        return ptr != IntPtr.Zero ? System.Runtime.InteropServices.Marshal.PtrToStringAnsi(ptr) ?? "Unknown error" : "Unknown error";
    }

    public class IngestTextRequest
    {
        public string Text { get; set; } = string.Empty;
    }

    public class IngestFileRequest
    {
        public string FilePath { get; set; } = string.Empty;
    }
}
