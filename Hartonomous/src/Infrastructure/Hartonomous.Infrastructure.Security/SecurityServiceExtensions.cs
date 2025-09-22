using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Identity.Web;
using System.Security.Claims;
using Hartonomous.Infrastructure.Security.Services;

namespace Hartonomous.Infrastructure.Security;

public static class SecurityServiceExtensions
{
    /// <summary>
    /// Add Azure Entra ID authentication for Hartonomous platform
    /// Configures JWT Bearer authentication with Microsoft Identity Web
    /// </summary>
    public static IServiceCollection AddHartonomousAuthentication(this IServiceCollection services, IConfiguration config)
    {
        // Configure Azure Entra ID authentication
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddMicrosoftIdentityWebApi(config.GetSection("AzureAd"));

        // Register HTTP context accessor for claims access
        services.AddHttpContextAccessor();

        // Register current user service for accessing authenticated user info
        var isDevelopment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") == "Development";
        
        if (isDevelopment && config.GetValue<bool>("Development:UseMockAuthentication", false))
        {
            // Use development mock service for testing
            services.AddScoped<ICurrentUserService, DevelopmentCurrentUserService>();
        }
        else
        {
            // Use real Azure Entra ID service
            services.AddScoped<ICurrentUserService, DefaultCurrentUserService>();
        }

        return services;
    }

    /// <summary>
    /// Add Azure Entra External ID authentication for external users
    /// Supports B2C and External ID scenarios
    /// </summary>
    public static IServiceCollection AddHartonomousExternalAuthentication(this IServiceCollection services, IConfiguration config)
    {
        // Configure Azure Entra External ID (B2C)
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddMicrosoftIdentityWebApi(config.GetSection("AzureEntraExternalId"));

        // Register HTTP context accessor and user service
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUserService, DefaultCurrentUserService>();

        return services;
    }

    /// <summary>
    /// Extension method to get user ID from ClaimsPrincipal (backward compatibility)
    /// </summary>
    public static string GetUserId(this ClaimsPrincipal principal)
    {
        return principal.FindFirst("oid")?.Value ??
               principal.FindFirst(ClaimTypes.NameIdentifier)?.Value ??
               principal.FindFirst("sub")?.Value ??
               throw new UnauthorizedAccessException("User ID claim not found in token.");
    }
}