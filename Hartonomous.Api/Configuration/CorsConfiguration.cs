namespace Hartonomous.API.Configuration;

/// <summary>
/// Configures CORS policies for cross-origin requests.
/// </summary>
public static class CorsConfiguration
{
    private const string DefaultPolicyName = "DefaultCorsPolicy";

    public static IServiceCollection AddCorsConfiguration(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var allowedOrigins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
            ?? Array.Empty<string>();

        services.AddCors(options =>
        {
            options.AddPolicy(DefaultPolicyName, builder =>
            {
                if (allowedOrigins.Length > 0 && !allowedOrigins.Contains("*"))
                {
                    builder.WithOrigins(allowedOrigins)
                           .AllowAnyMethod()
                           .AllowAnyHeader()
                           .AllowCredentials()
                           .WithExposedHeaders("X-Correlation-Id", "X-Request-Id");
                }
                else
                {
                    // Development: allow all origins (NOT for production)
                    builder.AllowAnyOrigin()
                           .AllowAnyMethod()
                           .AllowAnyHeader()
                           .WithExposedHeaders("X-Correlation-Id", "X-Request-Id");
                }
            });

            // Blazor-specific CORS policy
            options.AddPolicy("BlazorPolicy", builder =>
            {
                var blazorOrigins = configuration.GetSection("Cors:BlazorOrigins").Get<string[]>()
                    ?? new[] { "https://localhost:7002", "http://localhost:5002" };

                builder.WithOrigins(blazorOrigins)
                       .AllowAnyMethod()
                       .AllowAnyHeader()
                       .AllowCredentials()
                       .WithExposedHeaders("X-Correlation-Id", "X-Request-Id");
            });
        });

        return services;
    }

    public static IApplicationBuilder UseCorsConfiguration(this IApplicationBuilder app)
    {
        app.UseCors(DefaultPolicyName);
        return app;
    }
}
