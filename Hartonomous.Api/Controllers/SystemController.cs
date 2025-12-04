using Hartonomous.Core.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Hartonomous.API.Controllers;

/// <summary>
/// API endpoints for system information and health monitoring.
/// </summary>
[ApiController]
[Route("api/v1/[controller]")]
[EnableRateLimiting("fixed")]
[Produces("application/json")]
public sealed class SystemController : ControllerBase
{
    private readonly IGpuService _gpuService;
    private readonly ILogger<SystemController> _logger;

    public SystemController(IGpuService gpuService, ILogger<SystemController> logger)
    {
        _gpuService = gpuService ?? throw new ArgumentNullException(nameof(gpuService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Get system information.
    /// </summary>
    /// <returns>System version and configuration</returns>
    [HttpGet("info")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(SystemInfoDto), StatusCodes.Status200OK)]
    [ResponseCache(Duration = 3600)]
    public ActionResult<SystemInfoDto> GetSystemInfo()
    {
        var info = new SystemInfoDto
        {
            Version = "1.0.0",
            Environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Unknown",
            ApiVersion = "v1",
            Features = new Dictionary<string, bool>
            {
                ["GpuAcceleration"] = true,
                ["SpatialIndexing"] = true,
                ["BpeLearning"] = true,
                ["ContentDeduplication"] = true,
                ["LandmarkDetection"] = true
            }
        };

        return Ok(info);
    }

    /// <summary>
    /// Check GPU availability and capabilities.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>GPU status and capabilities</returns>
    [HttpGet("gpu-status")]
    [Authorize]
    [ProducesResponseType(typeof(GpuStatusDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ResponseCache(Duration = 300)]
    public async Task<ActionResult<GpuStatusDto>> GetGpuStatus(
        CancellationToken cancellationToken)
    {
        var capabilities = await _gpuService.CheckGpuAvailabilityAsync(cancellationToken);

        var status = new GpuStatusDto
        {
            IsAvailable = capabilities.IsAvailable,
            HasCuPy = capabilities.HasCuPy,
            HasCuMl = capabilities.HasCuMl,
            GpuCount = capabilities.GpuCount,
            GpuMemoryMb = capabilities.GpuMemoryMb,
            Status = capabilities.IsAvailable ? "Available" : "Unavailable",
            ErrorMessage = capabilities.ErrorMessage
        };

        return Ok(status);
    }

    /// <summary>
    /// Get API rate limit information.
    /// </summary>
    /// <returns>Current rate limits</returns>
    [HttpGet("rate-limits")]
    [Authorize]
    [ProducesResponseType(typeof(RateLimitInfoDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ResponseCache(Duration = 600)]
    public ActionResult<RateLimitInfoDto> GetRateLimits()
    {
        var info = new RateLimitInfoDto
        {
            WindowSeconds = 60,
            PermitLimit = 100,
            QueueLimit = 10,
            Policy = "Fixed Window"
        };

        return Ok(info);
    }

    /// <summary>
    /// Get supported API versions.
    /// </summary>
    /// <returns>List of supported API versions</returns>
    [HttpGet("versions")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(IReadOnlyList<string>), StatusCodes.Status200OK)]
    [ResponseCache(Duration = 86400)]
    public ActionResult<IReadOnlyList<string>> GetApiVersions()
    {
        var versions = new[] { "v1" };
        return Ok(versions);
    }
}

/// <summary>
/// System information DTO.
/// </summary>
public sealed record SystemInfoDto
{
    public required string Version { get; init; }
    public required string Environment { get; init; }
    public required string ApiVersion { get; init; }
    public required Dictionary<string, bool> Features { get; init; }
}

/// <summary>
/// GPU status DTO.
/// </summary>
public sealed record GpuStatusDto
{
    public required bool IsAvailable { get; init; }
    public required bool HasCuPy { get; init; }
    public required bool HasCuMl { get; init; }
    public required int GpuCount { get; init; }
    public required long GpuMemoryMb { get; init; }
    public required string Status { get; init; }
    public string? ErrorMessage { get; init; }
}

/// <summary>
/// Rate limit information DTO.
/// </summary>
public sealed record RateLimitInfoDto
{
    public required int WindowSeconds { get; init; }
    public required int PermitLimit { get; init; }
    public required int QueueLimit { get; init; }
    public required string Policy { get; init; }
}
