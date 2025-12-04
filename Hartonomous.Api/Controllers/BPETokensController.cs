using Hartonomous.Core.Application.Commands.BPETokens;
using Hartonomous.Core.Application.Common;
using Hartonomous.Core.Application.Queries.BPETokens;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Hartonomous.API.Controllers;

/// <summary>
/// API endpoints for Byte Pair Encoding (BPE) token management.
/// Handles vocabulary learning, token merging, and pattern analysis.
/// </summary>
[ApiController]
[Route("api/v1/[controller]")]
[Authorize]
[EnableRateLimiting("fixed")]
[Produces("application/json")]
public sealed class BPETokensController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ILogger<BPETokensController> _logger;

    public BPETokensController(IMediator mediator, ILogger<BPETokensController> logger)
    {
        _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Get BPE vocabulary (paginated).
    /// </summary>
    /// <param name="page">Page number (1-based)</param>
    /// <param name="pageSize">Page size (max 200)</param>
    /// <param name="sortBy">Sort field (frequency, rank, merge_level)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Paginated BPE token vocabulary</returns>
    [HttpGet("vocabulary")]
    [ProducesResponseType(typeof(PaginatedResult<BPETokenDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ResponseCache(Duration = 120, VaryByQueryKeys = new[] { "page", "pageSize", "sortBy" })]
    public async Task<ActionResult<PaginatedResult<BPETokenDto>>> GetVocabulary(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        [FromQuery] string sortBy = "frequency",
        CancellationToken cancellationToken = default)
    {
        if (pageSize > 200) pageSize = 200;
        if (page < 1) page = 1;

        var query = new GetBPEVocabularyQuery
        {
            PageNumber = page,
            PageSize = pageSize
        };

        var result = await _mediator.Send(query, cancellationToken);

        if (!result.IsSuccess)
        {
            return BadRequest(new { error = result.Error });
        }

        return Ok(result.Value);
    }

    /// <summary>
    /// Get BPE token by ID.
    /// </summary>
    /// <param name="tokenId">Token ID (256+ for learned tokens)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Token details</returns>
    [HttpGet("{tokenId:int}")]
    [ProducesResponseType(typeof(BPETokenDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ResponseCache(Duration = 300, VaryByQueryKeys = new[] { "tokenId" })]
    public async Task<ActionResult<BPETokenDto>> GetTokenById(
        int tokenId,
        CancellationToken cancellationToken)
    {
        var query = new GetBPETokenByIdQuery { TokenId = tokenId };
        var result = await _mediator.Send(query, cancellationToken);

        if (!result.IsSuccess)
        {
            return NotFound(new { error = result.Error });
        }

        return Ok(result.Value);
    }

    /// <summary>
    /// Merge byte pair to create new BPE token.
    /// </summary>
    /// <param name="request">Merge request with byte pair</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Newly created token</returns>
    [HttpPost("merge")]
    [Authorize(Policy = "AdminPolicy")]
    [ProducesResponseType(typeof(MergeBytePairCommandResult), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<MergeBytePairCommandResult>> MergeBytePair(
        [FromBody] MergeBytePairCommand request,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Merging byte pair: [{First}, {Second}] at level {Level}",
            request.FirstByte, request.SecondByte, request.MergeLevel);

        var result = await _mediator.Send(request, cancellationToken);

        if (!result.IsSuccess)
        {
            return BadRequest(new { error = result.Error });
        }

        return CreatedAtAction(
            nameof(GetTokenById),
            new { tokenId = result.Value?.TokenId },
            result.Value);
    }

    /// <summary>
    /// Learn BPE vocabulary from recent content.
    /// </summary>
    /// <param name="maxVocabSize">Maximum tokens to learn (default 1000, max 10000)</param>
    /// <param name="minFrequency">Minimum pair frequency (default 2)</param>
    /// <param name="sampleSize">Content samples to analyze (default 1000, max 10000)</param>
    /// <param name="useGpu">Use GPU acceleration (default true)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Learned vocabulary statistics</returns>
    [HttpPost("learn")]
    [Authorize(Policy = "AdminPolicy")]
    [ProducesResponseType(typeof(LearnBPEVocabularyCommandResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<LearnBPEVocabularyCommandResult>> LearnVocabulary(
        [FromQuery] int maxVocabSize = 1000,
        [FromQuery] int minFrequency = 2,
        [FromQuery] int sampleSize = 1000,
        [FromQuery] bool useGpu = true,
        CancellationToken cancellationToken = default)
    {
        if (maxVocabSize > 10000) maxVocabSize = 10000;
        if (sampleSize > 10000) sampleSize = 10000;

        _logger.LogInformation(
            "Learning BPE vocabulary: maxVocab={MaxVocab}, minFreq={MinFreq}, samples={Samples}, gpu={UseGpu}",
            maxVocabSize, minFrequency, sampleSize, useGpu);

        var command = new LearnBPEVocabularyCommand
        {
            MaxVocabSize = maxVocabSize,
            MinFrequency = minFrequency,
            SampleSize = sampleSize,
            UseGpu = useGpu
        };

        var result = await _mediator.Send(command, cancellationToken);

        if (!result.IsSuccess)
        {
            return BadRequest(new { error = result.Error });
        }

        return Ok(result.Value);
    }

    /// <summary>
    /// Get BPE token statistics.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Vocabulary statistics</returns>
    [HttpGet("statistics")]
    [ProducesResponseType(typeof(BPEStatisticsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ResponseCache(Duration = 60)]
    public async Task<ActionResult<BPEStatisticsDto>> GetStatistics(
        CancellationToken cancellationToken)
    {
        var query = new GetBPEStatisticsQuery();
        var result = await _mediator.Send(query, cancellationToken);

        if (!result.IsSuccess)
        {
            return BadRequest(new { error = result.Error });
        }

        return Ok(result.Value);
    }

    /// <summary>
    /// Get tokens by merge level.
    /// </summary>
    /// <param name="mergeLevel">Merge level (0 for base bytes, 1+ for learned tokens)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of tokens at specified merge level</returns>
    [HttpGet("by-level/{mergeLevel:int}")]
    [ProducesResponseType(typeof(IReadOnlyList<BPETokenDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ResponseCache(Duration = 180, VaryByQueryKeys = new[] { "mergeLevel" })]
    public async Task<ActionResult<IReadOnlyList<BPETokenDto>>> GetTokensByMergeLevel(
        int mergeLevel,
        CancellationToken cancellationToken)
    {
        if (mergeLevel < 0)
        {
            return BadRequest(new { error = "Merge level must be non-negative" });
        }

        var query = new GetTokensByMergeLevelQuery { MergeLevel = mergeLevel };
        var result = await _mediator.Send(query, cancellationToken);

        if (!result.IsSuccess)
        {
            return BadRequest(new { error = result.Error });
        }

        return Ok(result.Value);
    }
}
