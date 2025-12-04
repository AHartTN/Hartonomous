using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Identity.Web;
using Microsoft.IdentityModel.Tokens;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Authorization;

namespace Hartonomous.Infrastructure.Security;

/// <summary>
/// Zero Trust authentication configuration with JWT Bearer and Microsoft Entra ID
/// </summary>
public static class AuthenticationConfiguration
{
    /// <summary>
    /// Configures JWT Bearer authentication with Microsoft Entra ID using standard Microsoft.Identity.Web binding.
    /// Uses DefaultAzureCredential for Azure resources, user secrets for local development.
    /// </summary>
    public static IServiceCollection AddZeroTrustAuthentication(
        this IServiceCollection services,
        IConfiguration configuration,
        IWebHostEnvironment environment)
    {
        // Configure JWT Bearer authentication using standard Microsoft.Identity.Web pattern
        // This automatically reads from "AzureAd" configuration section
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddMicrosoftIdentityWebApi(
                jwtOptions =>
                {
                    // HTTPS requirement
                    jwtOptions.RequireHttpsMetadata = !environment.IsDevelopment();

                    // Save tokens for downstream API calls
                    jwtOptions.SaveToken = true;

                    // Map inbound claims to standard claim types
                    jwtOptions.MapInboundClaims = false;

                    // In development, also accept dev tokens signed with symmetric key
                    if (environment.IsDevelopment())
                    {
                        var devKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(
                            "development-secret-key-min-32-chars-long-for-hmac-sha256-signing"));

                        var clientId = configuration["AzureAd:ClientId"];
                        var audience = configuration["AzureAd:Audience"] ?? $"api://{clientId}";

                        jwtOptions.TokenValidationParameters.IssuerSigningKeyResolver = (token, securityToken, kid, parameters) =>
                        {
                            // Try dev key first, then fall back to Azure AD keys
                            var keys = new List<SecurityKey> { devKey };
                            return keys;
                        };

                        // More lenient validation for dev tokens
                        jwtOptions.TokenValidationParameters.ValidateIssuerSigningKey = true;
                        jwtOptions.TokenValidationParameters.ValidateIssuer = false; // Dev tokens may have different issuer
                        jwtOptions.TokenValidationParameters.ValidateAudience = true;
                        jwtOptions.TokenValidationParameters.ValidAudiences = new[] { audience, clientId };
                        jwtOptions.TokenValidationParameters.ValidateLifetime = true;
                    }

                    // Events for logging and diagnostics
                    jwtOptions.Events = new JwtBearerEvents
                    {
                        OnAuthenticationFailed = context =>
                        {
                            var logger = context.HttpContext.RequestServices
                                .GetRequiredService<ILoggerFactory>()
                                .CreateLogger("Authentication");
                            
                            logger.LogError(context.Exception,
                                "Authentication failed. Token: {Token}",
                                context.Request.Headers.Authorization.ToString());
                            
                            return Task.CompletedTask;
                        },
                        OnTokenValidated = context =>
                        {
                            var logger = context.HttpContext.RequestServices
                                .GetRequiredService<ILoggerFactory>()
                                .CreateLogger("Authentication");
                            
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
                                .GetRequiredService<ILoggerFactory>()
                                .CreateLogger("Authentication");
                            
                            logger.LogWarning(
                                "Challenge issued. Error: {Error}, Description: {Description}",
                                context.Error,
                                context.ErrorDescription);
                            
                            return Task.CompletedTask;
                        },
                        OnForbidden = context =>
                        {
                            var logger = context.HttpContext.RequestServices
                                .GetRequiredService<ILoggerFactory>()
                                .CreateLogger("Authentication");
                            
                            logger.LogWarning(
                                "Forbidden access attempt by user: {User} to resource: {Path}",
                                context.Principal?.Identity?.Name,
                                context.Request.Path);
                            
                            return Task.CompletedTask;
                        }
                    };
                },
                microsoftIdentityOptions =>
                {
                    // Standard Microsoft.Identity.Web configuration binding
                    // Reads from "AzureAd" section: Instance, Domain, TenantId, ClientId, etc.
                    configuration.Bind("AzureAd", microsoftIdentityOptions);
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
            .SetFallbackPolicy(new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .Build())
            
            // Admin policy - requires Admin role
            .AddPolicy("AdminPolicy", policy =>
            {
                policy.RequireRole("Admin", "Administrator");
                policy.RequireClaim("scope", "api.admin");
            })
            
            // User policy - requires User role
            .AddPolicy("UserPolicy", policy =>
            {
                policy.RequireRole("User", "Reader");
                policy.RequireClaim("scope", "api.read");
            })
            
            // Write policy - requires write permissions (scope for delegated, roles for app-only)
            .AddPolicy("WritePolicy", policy =>
            {
                policy.RequireAssertion(context =>
                    context.User.HasClaim(c => c.Type == "scope" && c.Value.Contains("api.write")) ||
                    context.User.HasClaim(c => c.Type == "roles" && c.Value == "Writer") ||
                    context.User.HasClaim(c => c.Type == "http://schemas.microsoft.com/ws/2008/06/identity/claims/role" && c.Value == "Writer"));
            })
            
            // API Scope policy - requires specific API scope
            .AddPolicy("ApiScopePolicy", policy =>
            {
                policy.RequireClaim("scope", "api.access");
            });

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
