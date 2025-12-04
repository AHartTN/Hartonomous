using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Identity.Web;
using Microsoft.IdentityModel.Tokens;
using System.Security.Claims;

namespace Hartonomous.Infrastructure.Security;

/// <summary>
/// Zero Trust authentication configuration with JWT Bearer and Microsoft Entra ID
/// </summary>
public static class AuthenticationConfiguration
{
    /// <summary>
    /// Configures JWT Bearer authentication with Microsoft Entra ID (Azure AD)
    /// Uses Managed Identity when available, falls back to Client Credentials
    /// </summary>
    public static IServiceCollection AddZeroTrustAuthentication(
        this IServiceCollection services,
        IConfiguration configuration,
        IWebHostEnvironment environment)
    {
        // Get authentication settings from configuration
        var authority = configuration["Authentication:Authority"] ?? 
            $"https://login.microsoftonline.com/{configuration["Authentication:TenantId"]}";
        var audience = configuration["Authentication:Audience"];
        var clientId = configuration["Authentication:ClientId"];

        // Configure JWT Bearer authentication
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddMicrosoftIdentityWebApi(options =>
            {
                // Token validation parameters
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ValidIssuer = authority,
                    ValidAudience = audience,
                    ClockSkew = TimeSpan.FromMinutes(5),
                    NameClaimType = ClaimTypes.NameIdentifier,
                    RoleClaimType = ClaimTypes.Role
                };

                // Events for logging and diagnostics
                options.Events = new JwtBearerEvents
                {
                    OnAuthenticationFailed = context =>
                    {
                        var logger = context.HttpContext.RequestServices
                            .GetRequiredService<ILogger<Program>>();
                        
                        logger.LogError(context.Exception,
                            "Authentication failed. Token: {Token}",
                            context.Request.Headers.Authorization);
                        
                        return Task.CompletedTask;
                    },
                    OnTokenValidated = context =>
                    {
                        var logger = context.HttpContext.RequestServices
                            .GetRequiredService<ILogger<Program>>();
                        
                        var claimsIdentity = context.Principal?.Identity as ClaimsIdentity;
                        logger.LogInformation(
                            "Token validated for user: {User} with roles: {Roles}",
                            claimsIdentity?.Name,
                            string.Join(", ", claimsIdentity?.Claims
                                .Where(c => c.Type == ClaimTypes.Role)
                                .Select(c => c.Value) ?? Array.Empty<string>()));
                        
                        return Task.CompletedTask;
                    },
                    OnChallenge = context =>
                    {
                        var logger = context.HttpContext.RequestServices
                            .GetRequiredService<ILogger<Program>>();
                        
                        logger.LogWarning(
                            "Challenge issued. Error: {Error}, Description: {Description}",
                            context.Error,
                            context.ErrorDescription);
                        
                        return Task.CompletedTask;
                    },
                    OnForbidden = context =>
                    {
                        var logger = context.HttpContext.RequestServices
                            .GetRequiredService<ILogger<Program>>();
                        
                        logger.LogWarning(
                            "Forbidden access attempt by user: {User} to resource: {Path}",
                            context.Principal?.Identity?.Name,
                            context.Request.Path);
                        
                        return Task.CompletedTask;
                    }
                };

                // HTTPS requirement
                options.RequireHttpsMetadata = !environment.IsDevelopment();
                
                // Save tokens for downstream API calls
                options.SaveToken = true;
                
                // Map inbound claims to standard claim types
                options.MapInboundClaims = false;
            },
            options =>
            {
                options.ClientId = clientId;
                options.TenantId = configuration["Authentication:TenantId"];
                options.Instance = configuration["Authentication:Instance"] ?? 
                    "https://login.microsoftonline.com/";
                
                // Enable caching of tokens
                options.EnableCachingForBearerTokens = true;
            });

        return services;
    }

    /// <summary>
    /// Configures authorization policies with role-based and claims-based access control
    /// </summary>
    public static IServiceCollection AddZeroTrustAuthorization(
        this IServiceCollection services)
    {
        services.AddAuthorizationBuilder()
            // Fallback policy - require authentication for all endpoints by default
            .SetFallbackPolicy(policy => policy
                .RequireAuthenticatedUser()
                .Build())
            
            // Admin policy - requires Admin role
            .AddPolicy("AdminPolicy", policy => policy
                .RequireRole("Admin", "Administrator")
                .RequireClaim("scope", "api.admin"))
            
            // User policy - requires User role
            .AddPolicy("UserPolicy", policy => policy
                .RequireRole("User", "Reader")
                .RequireClaim("scope", "api.read"))
            
            // Write policy - requires write permissions
            .AddPolicy("WritePolicy", policy => policy
                .RequireClaim("scope", "api.write"))
            
            // API Scope policy - requires specific API scope
            .AddPolicy("ApiScopePolicy", policy => policy
                .RequireClaim("scope", "api.access"));

        return services;
    }

    /// <summary>
    /// Adds claims transformation to enrich user identity
    /// </summary>
    public static IServiceCollection AddClaimsTransformation(
        this IServiceCollection services)
    {
        services.AddScoped<IClaimsTransformation, CustomClaimsTransformation>();
        return services;
    }
}

/// <summary>
/// Custom claims transformation to add application-specific claims
/// </summary>
public class CustomClaimsTransformation : IClaimsTransformation
{
    private readonly ILogger<CustomClaimsTransformation> _logger;

    public CustomClaimsTransformation(ILogger<CustomClaimsTransformation> logger)
    {
        _logger = logger;
    }

    public Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        var claimsIdentity = new ClaimsIdentity();
        
        // Add application-specific claims
        var userId = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!string.IsNullOrEmpty(userId))
        {
            // Add custom claims based on user ID or other logic
            claimsIdentity.AddClaim(new Claim("app_user_id", userId));
            
            _logger.LogDebug("Added custom claims for user {UserId}", userId);
        }

        // Add the new claims to the principal
        var newPrincipal = new ClaimsPrincipal(claimsIdentity);
        newPrincipal.AddIdentities(principal.Identities);

        return Task.FromResult(newPrincipal);
    }
}
