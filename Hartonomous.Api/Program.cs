using Hartonomous.API.Configuration;
using Hartonomous.API.Middleware;
using Hartonomous.Core.Application.Extensions;
using Hartonomous.Data.Extensions;
using Hartonomous.Infrastructure.Extensions;
using Hartonomous.Infrastructure.Health;
using Hartonomous.Infrastructure.Security;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;

namespace Hartonomous.API;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        
        // Add Aspire service defaults (OpenTelemetry, health checks, etc.)
        builder.AddServiceDefaults();

        // Add application layer (MediatR, FluentValidation)
        builder.Services.AddApplicationLayer();

        // Add data layer (EF Core, repositories)
        builder.Services.AddDataLayer(builder.Configuration);

        // Add infrastructure services (GPU, caching, current user, etc.)
        builder.Services.AddInfrastructureServices(builder.Configuration);

        // Configure forwarded headers for reverse proxy (Nginx)
        builder.Services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            options.KnownIPNetworks.Clear();
            options.KnownProxies.Clear();
        });

        // Add Zero Trust authentication and authorization
        builder.Services.AddZeroTrustAuthentication(builder.Configuration, builder.Environment);
        builder.Services.AddZeroTrustAuthorization();
        builder.Services.AddClaimsTransformation();

        // Add rate limiting
        builder.Services.AddRateLimitingConfiguration(builder.Configuration);

        // Add comprehensive health checks
        builder.Services.AddComprehensiveHealthChecks(builder.Configuration);

        // Add CORS
        builder.Services.AddCorsConfiguration(builder.Configuration);

        // Add exception handling
        builder.Services.AddExceptionHandlingConfiguration();

        // Add API versioning
        builder.Services.AddApiVersioningConfiguration();

        // Add Swagger/OpenAPI
        builder.Services.AddSwaggerConfiguration();

        // Add controllers with JSON options
        builder.Services.AddControllers()
            .AddJsonOptions(options =>
            {
                options.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
                options.JsonSerializerOptions.DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull;
            });
        
        // Add response compression
        builder.Services.AddResponseCompression(options =>
        {
            options.EnableForHttps = true;
            options.MimeTypes = new[]
            {
                "application/json",
                "application/xml",
                "text/plain",
                "text/html",
                "text/css",
                "text/javascript",
                "application/javascript"
            };
        });

        // Add response caching
        builder.Services.AddResponseCaching();

        var app = builder.Build();

        // Configure the HTTP request pipeline
        
        // Use forwarded headers first (important for reverse proxy scenarios)
        app.UseForwardedHeaders();

        // Map Aspire default endpoints (health, etc.)
        app.MapDefaultEndpoints();

        // Exception handling
        app.UseExceptionHandlingConfiguration(app.Environment);

        // Enable response compression
        app.UseResponseCompression();

        // Enable response caching
        app.UseResponseCaching();

        // Correlation ID middleware (before request logging)
        app.UseCorrelationId();

        // Request logging middleware
        app.UseMiddleware<RequestLoggingMiddleware>();

        // Security headers middleware
        app.UseSecurityHeaders();

        // Enable CORS
        app.UseCorsConfiguration();

        // Enable rate limiting
        app.UseRateLimiter();

        // HTTPS redirection
        if (!app.Environment.IsDevelopment())
        {
            app.UseHttpsRedirection();
        }

        // Swagger/OpenAPI
        if (app.Environment.IsDevelopment())
        {
            app.UseSwaggerConfiguration();
        }

        // Authentication & Authorization
        app.UseAuthentication();
        app.UseAuthorization();

        // Map controllers
        app.MapControllers()
            .RequireAuthorization(); // Require authentication by default

        // Health check endpoints
        app.MapHealthChecks("/health/live", new HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains("live")
        });

        app.MapHealthChecks("/health/ready", new HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains("ready")
        });

        app.MapHealthChecks("/health", new HealthCheckOptions
        {
            Predicate = _ => true,
            ResponseWriter = HealthCheckResponseWriter.WriteDetailedResponse
        });

        app.Run();
    }
}
