using Hartonomous.Core.Services;
using Microsoft.AspNetCore.Mvc;

namespace Hartonomous.API.Controllers;

/// <summary>
/// Semantic query endpoints — gravitational truth, co-occurrence, Q&A.
/// These are Hartonomous-native (not OpenAI-compatible) for direct substrate access.
/// </summary>
[ApiController]
[Route("api/query")]
public class QueryController : ControllerBase
{
    private readonly QueryService _query;
    private readonly ILogger<QueryController> _logger;

    public QueryController(QueryService query, ILogger<QueryController> logger)
    {
        _query = query;
        _logger = logger;
    }

    /// <summary>
    /// Find compositions that co-occur with query text.
    /// </summary>
    [HttpPost("related")]
    public IActionResult FindRelated([FromBody] RelatedRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Text))
            return BadRequest(new { error = "text is required" });

        try
        {
            var results = _query.FindRelated(request.Text, request.Limit);
            return Ok(new { results, count = results.Count });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Related query failed");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    /// <summary>
    /// Find gravitational truth — topological consensus on S3.
    /// "Truths Cluster, Lies Scatter."
    /// </summary>
    [HttpPost("truth")]
    public IActionResult FindTruth([FromBody] TruthRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Text))
            return BadRequest(new { error = "text is required" });

        try
        {
            var results = _query.FindGravitationalTruth(request.Text, request.MinElo, request.Limit);
            return Ok(new { results, count = results.Count });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Truth query failed");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    /// <summary>
    /// Answer a natural language question via relationship traversal.
    /// </summary>
    [HttpPost("answer")]
    public IActionResult AnswerQuestion([FromBody] AnswerRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Question))
            return BadRequest(new { error = "question is required" });

        try
        {
            var result = _query.AnswerQuestion(request.Question);
            if (result == null)
                return Ok(new { answer = (string?)null, confidence = 0.0 });

            return Ok(new { answer = result.Text, confidence = result.Confidence });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Answer query failed");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    public sealed class RelatedRequest
    {
        public string Text { get; set; } = "";
        public int Limit { get; set; } = 10;
    }

    public sealed class TruthRequest
    {
        public string Text { get; set; } = "";
        public double MinElo { get; set; } = 1500.0;
        public int Limit { get; set; } = 10;
    }

    public sealed class AnswerRequest
    {
        public string Question { get; set; } = "";
    }
}
