using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Hartonomous.Core.Models;
using Hartonomous.Core.Services;
using Microsoft.AspNetCore.Mvc;

namespace Hartonomous.API.Controllers;

/// <summary>
/// OpenAI-compatible chat completions API.
/// Walk Engine generates text via geometric traversal of the semantic substrate.
/// </summary>
[ApiController]
[Route("v1")]
public class ChatController : ControllerBase
{
    private readonly WalkService _walk;
    private readonly ILogger<ChatController> _logger;
    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    public ChatController(WalkService walk, ILogger<ChatController> logger)
    {
        _walk = walk;
        _logger = logger;
    }

    [HttpPost("chat/completions")]
    public async Task<IActionResult> ChatCompletions([FromBody] ChatCompletionRequest request)
    {
        if (request.Messages.Count == 0)
            return BadRequest(new { error = new { message = "messages is required", type = "invalid_request_error" } });

        // Extract the last user message as the prompt
        var prompt = request.Messages.LastOrDefault(m => m.Role == "user")?.Content;
        if (string.IsNullOrWhiteSpace(prompt))
            return BadRequest(new { error = new { message = "No user message found", type = "invalid_request_error" } });

        var temperature = request.Temperature ?? 0.7;
        var maxTokens = request.MaxTokens ?? 200;
        var stopText = request.Stop is string s ? s : null;
        var requestId = $"hart-{Guid.NewGuid():N}";

        // Extract system prompt context for goal-directed walks
        var systemPrompt = request.Messages.FirstOrDefault(m => m.Role == "system")?.Content;
        if (stopText == null && systemPrompt != null)
        {
            // System prompt can serve as context/goal for the walk
        }

        if (request.Stream)
            return await StreamCompletion(requestId, prompt, temperature, maxTokens, stopText);

        return NonStreamCompletion(requestId, prompt, temperature, maxTokens, stopText);
    }

    private IActionResult NonStreamCompletion(string requestId, string prompt,
        double temperature, int maxTokens, string? stopText)
    {
        try
        {
            var output = _walk.Generate(prompt, temperature, maxTokens, stopText: stopText);

            var response = new ChatCompletionResponse
            {
                Id = requestId,
                Choices =
                [
                    new ChatChoice
                    {
                        Index = 0,
                        Message = new ChatMessage { Role = "assistant", Content = output.Text },
                        FinishReason = MapFinishReason(output.FinishReason),
                    }
                ],
                Usage = new ChatUsage
                {
                    PromptTokens = EstimatePromptTokens(prompt),
                    CompletionTokens = output.Steps,
                    TotalTokens = EstimatePromptTokens(prompt) + output.Steps,
                },
            };

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Generation failed for prompt: {Prompt}", prompt[..Math.Min(50, prompt.Length)]);
            return StatusCode(500, new { error = new { message = ex.Message, type = "server_error" } });
        }
    }

    private async Task<IActionResult> StreamCompletion(string requestId, string prompt,
        double temperature, int maxTokens, string? stopText)
    {
        Response.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-cache";
        Response.Headers.Connection = "keep-alive";

        try
        {
            // Send initial chunk with role
            var roleChunk = new ChatCompletionChunk
            {
                Id = requestId,
                Choices =
                [
                    new ChatChunkChoice
                    {
                        Index = 0,
                        Delta = new ChatDelta { Role = "assistant" },
                    }
                ],
            };
            await WriteSSE(roleChunk);

            string? lastFinishReason = null;

            var output = _walk.GenerateStream(prompt,
                onFragment: (fragment, step, energy) =>
                {
                    var chunk = new ChatCompletionChunk
                    {
                        Id = requestId,
                        Choices =
                        [
                            new ChatChunkChoice
                            {
                                Index = 0,
                                Delta = new ChatDelta { Content = fragment },
                            }
                        ],
                    };
                    // Sync write — the callback is called from C++ thread via P/Invoke
                    var json = JsonSerializer.Serialize(chunk, s_jsonOptions);
                    var bytes = Encoding.UTF8.GetBytes($"data: {json}\n\n");
                    Response.Body.Write(bytes);
                    Response.Body.Flush();
                },
                temperature: temperature,
                maxTokens: maxTokens,
                stopText: stopText);

            lastFinishReason = MapFinishReason(output.FinishReason);

            // Send final chunk with finish_reason
            var endChunk = new ChatCompletionChunk
            {
                Id = requestId,
                Choices =
                [
                    new ChatChunkChoice
                    {
                        Index = 0,
                        Delta = new ChatDelta(),
                        FinishReason = lastFinishReason,
                    }
                ],
            };
            await WriteSSE(endChunk);

            // Send [DONE]
            await Response.WriteAsync("data: [DONE]\n\n");
            await Response.Body.FlushAsync();

            return new EmptyResult();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Streaming generation failed");
            var errorBytes = Encoding.UTF8.GetBytes($"data: {{\"error\": \"{ex.Message}\"}}\n\n");
            await Response.Body.WriteAsync(errorBytes);
            await Response.Body.FlushAsync();
            return new EmptyResult();
        }
    }

    private async Task WriteSSE(object data)
    {
        var json = JsonSerializer.Serialize(data, s_jsonOptions);
        await Response.WriteAsync($"data: {json}\n\n");
        await Response.Body.FlushAsync();
    }

    private static string MapFinishReason(string engineReason)
    {
        if (engineReason.Contains("energy", StringComparison.OrdinalIgnoreCase)) return "length";
        if (engineReason.Contains("goal", StringComparison.OrdinalIgnoreCase)) return "stop";
        if (engineReason.Contains("length", StringComparison.OrdinalIgnoreCase)) return "length";
        if (engineReason.Contains("trapped", StringComparison.OrdinalIgnoreCase)) return "stop";
        return "stop";
    }

    private static int EstimatePromptTokens(string prompt)
    {
        // Rough approximation: 1 composition ≈ 3-4 chars
        return Math.Max(1, prompt.Length / 4);
    }
}
