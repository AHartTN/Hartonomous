using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace Hartonomous.API.Controllers;

/// <summary>
/// Development-only API for generating JWT tokens for local testing.
/// </summary>
[ApiController]
[Route("api/v{version:apiVersion}/[controller]")]
[ApiVersion("1.0")]
[AllowAnonymous]
[Produces("application/json")]
#if !DEBUG
[ApiExplorerSettings(IgnoreApi = true)]
#endif
public sealed class DevTokenController : ControllerBase
{
    private readonly IConfiguration _configuration;
    private readonly IWebHostEnvironment _environment;
    private readonly ILogger<DevTokenController> _logger;

    public DevTokenController(
        IConfiguration configuration,
        IWebHostEnvironment environment,
        ILogger<DevTokenController> logger)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _environment = environment ?? throw new ArgumentNullException(nameof(environment));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Generate a development JWT token for testing API endpoints.
    /// DEVELOPMENT ONLY - This endpoint is disabled in production.
    /// </summary>
    /// <param name="request">Token generation request</param>
    /// <returns>JWT token for authentication</returns>
    [HttpPost("token")]
    [ProducesResponseType(typeof(DevTokenResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public ActionResult<DevTokenResponseDto> GenerateToken([FromBody] DevTokenRequestDto request)
    {
        // Only allow in Development environment
        if (!_environment.IsDevelopment())
        {
            _logger.LogWarning("Attempted to generate dev token in non-development environment");
            return Forbid();
        }

        var tenantId = _configuration["AzureAd:TenantId"]
            ?? throw new InvalidOperationException("AzureAd:TenantId not configured");
        var clientId = _configuration["AzureAd:ClientId"]
            ?? throw new InvalidOperationException("AzureAd:ClientId not configured");
        var audience = _configuration["AzureAd:Audience"] ?? $"api://{clientId}";

        // Generate a symmetric key for signing (dev only - production uses Azure AD public keys)
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(
            "development-secret-key-min-32-chars-long-for-hmac-sha256-signing"));

        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        // Build claims based on request
        var claims = new List<Claim>
        {
            new Claim(JwtRegisteredClaimNames.Sub, request.UserId ?? "dev-user-123"),
            new Claim(JwtRegisteredClaimNames.Email, request.Email ?? "dev@hartonomous.local"),
            new Claim(JwtRegisteredClaimNames.Name, request.Name ?? "Development User"),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new Claim("tid", tenantId),
            new Claim("aud", audience),
            new Claim("azp", clientId)
        };

        // Add roles
        var roles = request.Roles ?? new[] { "User" };
        foreach (var role in roles)
        {
            claims.Add(new Claim(ClaimTypes.Role, role));
            claims.Add(new Claim("roles", role));
        }

        // Add scopes
        var scopes = request.Scopes ?? new[] { "api.read", "access_as_user" };
        claims.Add(new Claim("scope", string.Join(" ", scopes)));
        claims.Add(new Claim("scp", string.Join(" ", scopes)));

        // Create token
        var token = new JwtSecurityToken(
            issuer: $"https://login.microsoftonline.com/{tenantId}/v2.0",
            audience: audience,
            claims: claims,
            notBefore: DateTime.UtcNow,
            expires: DateTime.UtcNow.AddHours(request.ExpirationHours ?? 8),
            signingCredentials: credentials
        );

        var tokenString = new JwtSecurityTokenHandler().WriteToken(token);

        _logger.LogInformation(
            "Generated dev token for user {UserId} with roles {Roles} and scopes {Scopes}",
            request.UserId ?? "dev-user-123",
            string.Join(", ", roles),
            string.Join(", ", scopes));

        return Ok(new DevTokenResponseDto
        {
            Token = tokenString,
            TokenType = "Bearer",
            ExpiresIn = (int)TimeSpan.FromHours(request.ExpirationHours ?? 8).TotalSeconds,
            Scope = string.Join(" ", scopes),
            IssuedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddHours(request.ExpirationHours ?? 8)
        });
    }

    /// <summary>
    /// Get sample token requests for common scenarios.
    /// </summary>
    /// <returns>Sample token configurations</returns>
    [HttpGet("samples")]
    [ProducesResponseType(typeof(IReadOnlyList<DevTokenSampleDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public ActionResult<IReadOnlyList<DevTokenSampleDto>> GetSamples()
    {
        if (!_environment.IsDevelopment())
        {
            return Forbid();
        }

        var samples = new List<DevTokenSampleDto>
        {
            new()
            {
                Name = "Admin User",
                Description = "Full access with admin role and all scopes",
                Request = new DevTokenRequestDto
                {
                    UserId = "admin-user-123",
                    Email = "admin@hartonomous.local",
                    Name = "Admin User",
                    Roles = new[] { "Admin", "User" },
                    Scopes = new[] { "api.access", "api.admin", "api.read", "api.write", "access_as_user" },
                    ExpirationHours = 8
                }
            },
            new()
            {
                Name = "Standard User",
                Description = "Basic read access",
                Request = new DevTokenRequestDto
                {
                    UserId = "user-456",
                    Email = "user@hartonomous.local",
                    Name = "Standard User",
                    Roles = new[] { "User" },
                    Scopes = new[] { "api.read", "access_as_user" },
                    ExpirationHours = 8
                }
            },
            new()
            {
                Name = "Data Scientist",
                Description = "Read and write access for data science work",
                Request = new DevTokenRequestDto
                {
                    UserId = "scientist-789",
                    Email = "scientist@hartonomous.local",
                    Name = "Data Scientist",
                    Roles = new[] { "DataScientist", "User" },
                    Scopes = new[] { "api.read", "api.write", "access_as_user" },
                    ExpirationHours = 8
                }
            },
            new()
            {
                Name = "Read-Only User",
                Description = "Only read permissions",
                Request = new DevTokenRequestDto
                {
                    UserId = "reader-321",
                    Email = "reader@hartonomous.local",
                    Name = "Read Only User",
                    Roles = new[] { "User" },
                    Scopes = new[] { "api.read", "access_as_user" },
                    ExpirationHours = 8
                }
            }
        };

        return Ok(samples);
    }
}

/// <summary>
/// Request DTO for generating a development token.
/// </summary>
public sealed record DevTokenRequestDto
{
    /// <summary>User ID for the token subject claim. Defaults to "dev-user-123".</summary>
    public string? UserId { get; init; }

    /// <summary>Email address for the token. Defaults to "dev@hartonomous.local".</summary>
    public string? Email { get; init; }

    /// <summary>Display name for the user. Defaults to "Development User".</summary>
    public string? Name { get; init; }

    /// <summary>Roles to include in the token. Defaults to ["User"].</summary>
    public string[]? Roles { get; init; }

    /// <summary>OAuth scopes to include. Defaults to ["api.read", "access_as_user"].</summary>
    public string[]? Scopes { get; init; }

    /// <summary>Token expiration time in hours. Defaults to 8 hours.</summary>
    public int? ExpirationHours { get; init; }
}

/// <summary>
/// Response DTO containing the generated JWT token.
/// </summary>
public sealed record DevTokenResponseDto
{
    /// <summary>The JWT token string.</summary>
    public required string Token { get; init; }

    /// <summary>Token type (always "Bearer").</summary>
    public required string TokenType { get; init; }

    /// <summary>Token expiration in seconds.</summary>
    public required int ExpiresIn { get; init; }

    /// <summary>Space-separated list of scopes.</summary>
    public required string Scope { get; init; }

    /// <summary>Token issue timestamp.</summary>
    public required DateTime IssuedAt { get; init; }

    /// <summary>Token expiration timestamp.</summary>
    public required DateTime ExpiresAt { get; init; }
}

/// <summary>
/// Sample token configuration DTO.
/// </summary>
public sealed record DevTokenSampleDto
{
    /// <summary>Sample name.</summary>
    public required string Name { get; init; }

    /// <summary>Sample description.</summary>
    public required string Description { get; init; }

    /// <summary>Token request configuration.</summary>
    public required DevTokenRequestDto Request { get; init; }
}
