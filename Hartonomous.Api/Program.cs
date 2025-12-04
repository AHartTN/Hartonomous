using Hartonomous.Infrastructure.Health;
using Hartonomous.Infrastructure.Security;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Identity.Web;

namespace Hartonomous.API;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        
        // Add Aspire service defaults (OpenTelemetry, health checks, etc.)
        builder.AddServiceDefaults();

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
        builder.Services.AddApiRateLimiting(builder.Configuration);

        // Add comprehensive health checks
        builder.Services.AddComprehensiveHealthChecks(builder.Configuration);

        // Add CORS policy
        builder.Services.AddCors(options =>
        {
            options.AddDefaultPolicy(policy =>
            {
                var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? Array.Empty<string>();
                
                policy.WithOrigins(allowedOrigins)
                    .AllowAnyMethod()
                    .AllowAnyHeader()
                    .AllowCredentials();
            });
        });

        // Add controllers
        builder.Services.AddControllers();
        
        // Add OpenAPI/Swagger
        builder.Services.AddOpenApi();
        
        // Add response compression
        builder.Services.AddResponseCompression(options =>
        {
            options.EnableForHttps = true;
        });

        var app = builder.Build();

        // Configure the HTTP request pipeline
        
        // Use forwarded headers first (important for reverse proxy scenarios)
        app.UseForwardedHeaders();

        // Map Aspire default endpoints (health, etc.)
        app.MapDefaultEndpoints();

        // Enable response compression
        app.UseResponseCompression();

        // Enable CORS
        app.UseCors();

        // Enable rate limiting
        app.UseRateLimiter();

        // HTTPS redirection
        if (!app.Environment.IsDevelopment())
        {
            app.UseHttpsRedirection();
        }

        // Security headers
        app.Use(async (context, next) =>
        {
            context.Response.Headers.Append("X-Content-Type-Options", "nosniff");
            context.Response.Headers.Append("X-Frame-Options", "DENY");
            context.Response.Headers.Append("X-XSS-Protection", "1; mode=block");
            context.Response.Headers.Append("Referrer-Policy", "strict-origin-when-cross-origin");
            context.Response.Headers.Append("Permissions-Policy", "accelerometer=(), camera=(), geolocation=(), gyroscope=(), magnetometer=(), microphone=(), payment=(), usb=()");
            
            if (!app.Environment.IsDevelopment())
            {
                context.Response.Headers.Append("Strict-Transport-Security", "max-age=31536000; includeSubDomains");
            }
            
            await next();
        });

        // OpenAPI in development only
        if (app.Environment.IsDevelopment())
        {
            app.MapOpenApi();
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
            Predicate = _ => true
        });

        app.Run();
    }
}
