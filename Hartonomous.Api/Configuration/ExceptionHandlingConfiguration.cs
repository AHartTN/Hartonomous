using Hartonomous.API.Middleware;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using System.Net;

namespace Hartonomous.API.Configuration;

/// <summary>
/// Configures global exception handling with ProblemDetails responses.
/// </summary>
public static class ExceptionHandlingConfiguration
{
    public static IServiceCollection AddExceptionHandlingConfiguration(this IServiceCollection services)
    {
        services.AddProblemDetails(options =>
        {
            options.CustomizeProblemDetails = context =>
            {
                context.ProblemDetails.Instance = $"{context.HttpContext.Request.Method} {context.HttpContext.Request.Path}";

                // Add correlation ID
                if (context.HttpContext.Items.TryGetValue("CorrelationId", out var correlationId))
                {
                    context.ProblemDetails.Extensions["correlationId"] = correlationId;
                }

                // Add trace ID
                context.ProblemDetails.Extensions["traceId"] = context.HttpContext.TraceIdentifier;

                // Add timestamp
                context.ProblemDetails.Extensions["timestamp"] = DateTimeOffset.UtcNow;
            };
        });

        return services;
    }

    public static IApplicationBuilder UseExceptionHandlingConfiguration(
        this IApplicationBuilder app,
        IHostEnvironment environment)
    {
        if (environment.IsDevelopment())
        {
            app.UseDeveloperExceptionPage();
        }
        else
        {
            app.UseExceptionHandler(errorApp =>
            {
                errorApp.Run(async context =>
                {
                    var exceptionHandlerFeature = context.Features.Get<IExceptionHandlerFeature>();
                    var exception = exceptionHandlerFeature?.Error;

                    var logger = context.RequestServices.GetRequiredService<ILogger<Program>>();
                    logger.LogError(exception, "Unhandled exception: {Message}", exception?.Message);

                    var problemDetails = new ProblemDetails
                    {
                        Status = (int)HttpStatusCode.InternalServerError,
                        Title = "An error occurred while processing your request",
                        Detail = environment.IsProduction() ? null : exception?.Message,
                        Instance = $"{context.Request.Method} {context.Request.Path}"
                    };

                    // Add correlation ID
                    if (context.Items.TryGetValue("CorrelationId", out var correlationId))
                    {
                        problemDetails.Extensions["correlationId"] = correlationId;
                    }

                    problemDetails.Extensions["traceId"] = context.TraceIdentifier;
                    problemDetails.Extensions["timestamp"] = DateTimeOffset.UtcNow;

                    context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
                    context.Response.ContentType = "application/problem+json";

                    await context.Response.WriteAsJsonAsync(problemDetails);
                });
            });
        }

        return app;
    }
}
