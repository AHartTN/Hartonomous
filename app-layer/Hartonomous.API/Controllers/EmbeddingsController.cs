using System.Text.Json;
using Hartonomous.Core.Models;
using Hartonomous.Core.Services;
using Hartonomous.Marshal;
using Microsoft.AspNetCore.Mvc;

namespace Hartonomous.API.Controllers;

/// <summary>
/// OpenAI-compatible /v1/embeddings endpoint.
/// Returns S3 hypersphere positions (4D) as embedding vectors.
/// </summary>
[ApiController]
[Route("v1")]
public class EmbeddingsController : ControllerBase
{
    private readonly EngineService _engine;
    private readonly ILogger<EmbeddingsController> _logger;

    public EmbeddingsController(EngineService engine, ILogger<EmbeddingsController> logger)
    {
        _engine = engine;
        _logger = logger;
    }

    [HttpPost("embeddings")]
    public unsafe IActionResult CreateEmbedding([FromBody] EmbeddingRequest request)
    {
        var inputs = ExtractInputs(request.Input);
        if (inputs.Count == 0)
            return BadRequest(new { error = new { message = "input is required", type = "invalid_request_error" } });

        var data = new List<EmbeddingData>();
        byte* hash = stackalloc byte[16];
        for (var i = 0; i < inputs.Count; i++)
        {
            var bytes = System.Text.Encoding.UTF8.GetBytes(inputs[i]);
            fixed (byte* ptr = bytes)
                NativeMethods.Blake3Hash(ptr, (nuint)bytes.Length, hash);

            // Get S3 position via codepoint projection of first char,
            // or compute centroid of all codepoints in the text
            var position = new double[4];
            fixed (double* pos = position)
            {
                if (!NativeMethods.CompositionPosition(_engine.DbHandle, hash, pos))
                {
                    // Fallback: use BLAKE3 hash → S3 via super fibonacci
                    // For now, return zero vector if composition doesn't exist
                    position = [0, 0, 0, 1]; // North pole of S3
                }
            }

            data.Add(new EmbeddingData
            {
                Index = i,
                Embedding = position,
            });
        }

        return Ok(new EmbeddingResponse
        {
            Data = data,
            Usage = new EmbeddingUsage
            {
                PromptTokens = inputs.Sum(s => Math.Max(1, s.Length / 4)),
                TotalTokens = inputs.Sum(s => Math.Max(1, s.Length / 4)),
            },
        });
    }

    private static List<string> ExtractInputs(object input)
    {
        if (input is string s) return [s];
        if (input is JsonElement je)
        {
            if (je.ValueKind == JsonValueKind.String) return [je.GetString()!];
            if (je.ValueKind == JsonValueKind.Array)
                return je.EnumerateArray().Select(e => e.GetString()!).ToList();
        }
        return [];
    }
}
