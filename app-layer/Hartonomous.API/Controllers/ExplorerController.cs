using System.Runtime.InteropServices;
using Hartonomous.Core.Services;
using Hartonomous.Core.Services.Domain;
using Hartonomous.Marshal;
using Hartonomous.Shared.Models;
using Microsoft.AspNetCore.Mvc;

namespace Hartonomous.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ExplorerController : ControllerBase
{
    private readonly EngineService _engine;
    private readonly IDomainRegistry _domains;
    private readonly ILogger<ExplorerController> _logger;

    public ExplorerController(EngineService engine, IDomainRegistry domains, ILogger<ExplorerController> logger)
    {
        _engine = engine;
        _domains = domains;
        _logger = logger;
    }

    [HttpGet("domains")]
    public IActionResult GetDomains()
    {
        return Ok(_domains.GetDomains());
    }

    [HttpGet("search")]
    public unsafe IActionResult Search([FromQuery] string q, [FromQuery] int limit = 20)
    {
        if (string.IsNullOrWhiteSpace(q)) return BadRequest("Query is required");

        // Use QueryRelated
        var queryHandle = NativeMethods.QueryCreate(_engine.DbHandle);
        try
        {
            if (NativeMethods.QueryRelated(queryHandle, q, (nuint)limit, out var resultsPtr, out var count))
            {
                var nodes = new List<ExplorerNode>();
                var sizeOfResult = System.Runtime.InteropServices.Marshal.SizeOf<QueryResult>();
                
                for (nuint i = 0; i < count; i++)
                {
                    // Access array at offset
                    var ptr = resultsPtr + (int)i * sizeOfResult;
                    var result = System.Runtime.InteropServices.Marshal.PtrToStructure<QueryResult>(ptr);
                    
                    var text = System.Runtime.InteropServices.Marshal.PtrToStringAnsi(result.Text);

                    if (!string.IsNullOrEmpty(text))
                    {
                        nodes.Add(new ExplorerNode
                        {
                            Id = text,
                            Name = text,
                            Type = "Concept",
                            Score = result.Confidence
                        });
                    }
                }
                
                NativeMethods.QueryFreeResults(resultsPtr, count);
                return Ok(nodes);
            }
            return Ok(new List<ExplorerNode>()); // Return empty if no results
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Explorer Search failed");
            return StatusCode(500, new { error = ex.Message });
        }
        finally
        {
            NativeMethods.QueryDestroy(queryHandle);
        }
    }

    [HttpGet("details/{text}")]
    public unsafe IActionResult GetDetails(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return BadRequest("Text is required");

        // 1. Hash text to get ID (simulated by codepoint or just hash)
        // 2. Get S3 position
        
        var bytes = System.Text.Encoding.UTF8.GetBytes(text);
        byte* hash = stackalloc byte[16];
        fixed (byte* ptr = bytes)
            NativeMethods.Blake3Hash(ptr, (nuint)bytes.Length, hash);

        var details = new ExplorerDetails { Text = text };

        double* pos = stackalloc double[4];
        if (NativeMethods.CompositionPosition(_engine.DbHandle, hash, pos))
        {
             details.S3Position = [pos[0], pos[1], pos[2], pos[3]];
             
             // Compute Hilbert
             ulong hi, lo;
             NativeMethods.S3ToHilbert(pos, 0, &hi, &lo);
             details.HilbertIndexHi = hi;
             details.HilbertIndexLo = lo;
             
             // Fake Elo for now or fetch if available
             details.Elo = 1200; 
        }
        else
        {
            // Not found, return 404
             return NotFound(new { error = "Concept not found in substrate" });
        }

        return Ok(details);
    }
}
